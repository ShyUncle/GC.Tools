using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
namespace MQTTDEMO;
// 可靠 MQTT 客户端。
// 提供六项可靠性能力：指数退避、随机抖动、重连锁、自动重订阅、应用层心跳兜底、离线消息缓冲。
public sealed class ReliableMqttClient : IAsyncDisposable
{
    #region 常量
    // 默认初始退避秒数。
    private const int DefaultBaseDelaySec = 1;
    // 默认退避上限秒数。
    private const int DefaultMaxDelaySec = 30;
    // 默认离线缓冲最大消息数。
    private const int DefaultMaxOutboxSize = 10000;
    // 默认应用层心跳间隔秒数。
    private const int DefaultHeartbeatIntervalSec = 30;
    // 单次重连尝试的超时秒数。
    private const int ReconnectTimeoutSec = 15;
    // 单次心跳等待 PINGRESP 的超时秒数，超时视为心跳失败。
    private const int PingTimeoutSec = 5;
    #endregion
    #region 字段
    // 底层 MQTTnet 客户端实例，负责实际的协议收发。
    private readonly IMqttClient _client;
    // MQTT 连接选项（服务端地址、客户端 ID、保活、CleanSession 等）。
    private readonly MqttClientOptions _options;
    // 日志记录器，用于输出连接、重连、心跳等运行信息。
    private readonly ILogger _logger;
    // 订阅登记表：重连后按它自动重订阅。
    private readonly ConcurrentDictionary<string, MqttQualityOfServiceLevel> _subscriptions = new();
    // 断线期间的待发消息缓冲队列，重连后按 FIFO 顺序补发。
    private readonly ConcurrentQueue<MqttApplicationMessage> _outbox = new();
    // 重连锁：保证同一时刻只有一个重连循环在跑，避免并发重连。
    private readonly SemaphoreSlim _reconnectLock = new(1, 1);
    // 全局取消令牌：Dispose 时停掉心跳与重连循环。
    private readonly CancellationTokenSource _cts = new();
    // 随机数生成器，用于指数退避的抖动计算。
    private readonly Random _rand = new();
    // 连续失败次数：连接成功时清零，失败时自增，用于计算指数退避。
    private int _attempt;
    // 指数退避初始秒数。
    private readonly int _baseDelaySec;
    // 指数退避上限秒数，超过后不再增长。
    private readonly int _maxDelaySec;
    // 离线缓冲队列最大长度，超出后新消息丢弃。
    private readonly int _maxOutboxSize;
    // 应用层心跳间隔。
    private readonly TimeSpan _heartbeatInterval;
    private Task? _heartbeatTask; 
    #endregion
    #region 事件
    // 收到 broker 推送的应用消息时触发。
    public event Func<MqttApplicationMessageReceivedEventArgs, Task>? ApplicationMessageReceived;
    #endregion
    #region 构造函数
    // 创建一个可靠 MQTT 客户端实例。
    public ReliableMqttClient(
        string brokerHost,
        int brokerPort,
        string clientId,
        ILogger logger,
        int baseDelaySec = DefaultBaseDelaySec,
        int maxDelaySec = DefaultMaxDelaySec,
        int maxOutboxSize = DefaultMaxOutboxSize,
        int heartbeatIntervalSec = DefaultHeartbeatIntervalSec)
    {
        // 保存配置
        _logger = logger;
        _baseDelaySec = baseDelaySec;
        _maxDelaySec = maxDelaySec;
        _maxOutboxSize = maxOutboxSize;
        _heartbeatInterval = TimeSpan.FromSeconds(heartbeatIntervalSec);
        // 创建底层客户端
        _client = new MqttClientFactory().CreateMqttClient();
        // 构建连接选项：固定 clientId + 30s 保活 + CleanSession=false（会话持久化，便于重订阅）
        _options = new MqttClientOptionsBuilder()
            .WithTcpServer(brokerHost, brokerPort)
            .WithClientId(clientId)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithCleanSession(false)
            .Build();
        // 订阅连接/断线事件
        _client.ConnectedAsync += OnConnectedAsync;
        _client.DisconnectedAsync += OnDisconnectedAsync;
        // 订阅消息接收事件（只注册一次，避免重连后重复挂接导致一条消息被回调多次）
        _client.ApplicationMessageReceivedAsync += args =>
        {
            args.AutoAcknowledge = false;
            return ApplicationMessageReceived?.Invoke(args) ?? Task.CompletedTask;
        };
    }
    #endregion
    #region 公共方法
    // 启动客户端：建立连接并启动应用层心跳循环。初始连接失败会自动进入重连流程。
    public async Task StartAsync()
    {
        // 1) 后台启动心跳循环
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_cts.Token));
        // 2) 尝试首次连接
        try
        {
            await _client.ConnectAsync(_options, _cts.Token);
        }
        catch (Exception ex)
        {
            // 3) 初始连接失败：记录日志并触发重连循环
            _logger.LogError(ex, "初始连接失败");
            _ = ReconnectLoopAsync();
        }
    }
    // 订阅主题。登记到订阅表中，连接建立后立即订阅；断线期间登记的主题会在重连后自动恢复。
    public async Task SubscribeAsync(string topic, MqttQualityOfServiceLevel qos)
    {
        // 1) 登记到订阅表，重连后会按此表恢复
        _subscriptions[topic] = qos;
        // 2) 已连接则立即下发 SUBSCRIBE
        if (_client.IsConnected)
        {
            await _client.SubscribeAsync(
                new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(topic, qos)
                    .Build());
        }
    }
    // 发布消息。在线时直接发送；断线或发送失败时进入离线缓冲队列，待重连后自动补发。
    public async Task PublishAsync(MqttApplicationMessage message)
    {
        // 1) 在线则直接发送
        if (_client.IsConnected)
        {
            try
            {
                await _client.PublishAsync(message);
                return;
            }
            catch (Exception ex)
            {
                // 2) 在线但发送失败：降级到离线缓冲
                _logger.LogWarning(ex, "发布失败，转入离线缓冲");
            }
        }
        // 3) 离线缓冲：队列未满则入队，否则丢弃
        if (_outbox.Count < _maxOutboxSize)
        {
            _outbox.Enqueue(message);
        }
        else
        {
            _logger.LogWarning("离线缓冲已满，消息被丢弃");
        }
    }
    // 释放资源：停止心跳与重连循环，断开连接并释放底层客户端。
    public async ValueTask DisposeAsync()
    {
        // 1) 取消所有后台循环
        _cts.Cancel();
        // 2) 若仍在线则主动断开
        if (_heartbeatTask != null) { try { await _heartbeatTask; } catch { } }
        if (_client.IsConnected) { try { await _client.DisconnectAsync(); } catch { } }
        // 3) 释放底层资源
        _client.Dispose();
        _cts.Dispose();
        _reconnectLock.Dispose();
    }
    #endregion
    #region 连接事件处理
    // 连接成功回调：重置退避计数、恢复订阅、补发离线缓冲消息。
    private async Task OnConnectedAsync(MqttClientConnectedEventArgs e)
    {
        // 1) 重置连续失败计数，下次重连从 base 开始
        Interlocked.Exchange(ref _attempt, 0);
        _logger.LogInformation("MQTT 连接成功");
        // 2) 按订阅表逐个恢复订阅
        foreach (var (topic, qos) in _subscriptions)
        {
            await _client.SubscribeAsync(
                new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(topic, qos)
                    .Build());
        }
        // 3) 补发断线期间缓冲的消息
        while (_outbox.TryDequeue(out var msg))
        {
            try
            {
                await _client.PublishAsync(msg);
            }
            catch (Exception ex)
            {
                // 补发失败：重新入队并中断本次补发，等下次连接成功再试
                _logger.LogWarning(ex, "补发离线消息失败，重新入队");
                _outbox.Enqueue(msg);
                break;
            }
        }
    }
    // 断线回调：由曾经在线触发的断线才启动重连循环，避免初始连接失败被重复触发。
    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs e)
    {
        _logger.LogWarning("MQTT 连接断开：{Reason}", e.Reason);
        // 仅当之前确实在线时才触发重连，避免初始连接失败被事件重复触发
        if (e.ClientWasConnected)
        {
            _ = ReconnectLoopAsync();
        }
        return Task.CompletedTask;
    }
    #endregion
    #region 重连与退避
    // 重连循环：通过信号量保证同一时刻只有一个循环；按指数退避 + 随机抖动等待后重试。
    private async Task ReconnectLoopAsync()
    {
        // 1) 非阻塞抢锁：已有人在重连则直接退出
        if (!await _reconnectLock.WaitAsync(0))
        {
            return;
        }
        try
        {
            // 2) 循环重试，直到取消或连接成功
            while (!_cts.IsCancellationRequested && !_client.IsConnected)
            {
                // 2.1 计算本次等待时长（指数退避 + 抖动）
                var delay = NextDelay();
                _logger.LogInformation(
                    "第 {Attempt} 次重连尝试，等待 {Delay:0.00} 秒",
                    _attempt,
                    delay.TotalSeconds);
                // 2.2 退避等待，期间被取消则退出
                try
                {
                    await Task.Delay(delay, _cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                // 2.3 带超时地尝试连接
                try
                {
                    using var timeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(ReconnectTimeoutSec));
                    await _client.ConnectAsync(_options, timeout.Token);
                    // 连接成功，OnConnectedAsync 会重置 _attempt 并做重订阅/补发
                    return;
                }
                catch (Exception ex)
                {
                    // 2.4 失败：记录日志并自增失败次数，进入下一轮更长的等待
                    _logger.LogWarning(ex, "重连失败");
                    Interlocked.Increment(ref _attempt);
                }
            }
        }
        finally
        {
            // 3) 释放重连锁
            _reconnectLock.Release();
        }
    }
    // 计算下一次重连等待时间：base * 2^(n-1)，封顶 max，并乘以 [0.5, 1.0] 的随机抖动。
    private TimeSpan NextDelay()
    {
        // 指数部分：base * 2^n
        var n = _attempt + 1;
        var cap = Math.Min(_maxDelaySec, _baseDelaySec * Math.Pow(2, n - 1));
        // 抖动：0.5 ~ 1.0 之间的随机系数，避免大量客户端同时重连造成惊群
        var jitter = 0.5 + Random.Shared.NextDouble() * 0.5;
        return TimeSpan.FromSeconds(cap * jitter);
    }
    #endregion
    #region 应用层心跳
    // 心跳循环：周期发送 MQTT PINGREQ；失败时主动断开连接，让重连流程接管。
    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 1) 按配置间隔等待
            try { await Task.Delay(_heartbeatInterval, ct); } catch (OperationCanceledException) { break; }
            // 2) 不在线则跳过（重连循环会负责恢复）
            if (!_client.IsConnected)
            {
                continue;
            }
            // 3) 发送应用层心跳（PINGREQ），带超时避免半开连接永久挂起
            bool pong;
            try
            {
                using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pingCts.CancelAfter(TimeSpan.FromSeconds(PingTimeoutSec));
                pong = await _client.TryPingAsync(pingCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 超时（非外部取消）：视为心跳失败
                pong = false;
            }
            // 4) 心跳失败：连接可能已半开，强制断开以触发重连
            if (!pong)
            {
                _logger.LogWarning("心跳探测失败，强制重连");
                _ = ForceReconnectAsync();
            }
        }
    }
    // 主动断开当前连接，触发 DisconnectedAsync 事件进入重连流程。
    private async Task ForceReconnectAsync()
    {
        try
        {
            // 仅在仍在线时才需要断开
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "强制断开连接失败");
        }
    }
    #endregion
    #region 测试缝（internal，仅用于单元/集成测试）
    // 当前连续重连失败次数（测试用）。
    internal int AttemptCount => Volatile.Read(ref _attempt);
    // 当前离线缓冲队列长度（测试用）。
    internal int OutboxCount => _outbox.Count;
    // 当前是否处于已连接状态（测试用）。
    internal bool IsConnected => _client.IsConnected;
    // 当前订阅表的只读快照（测试用）。
    internal IReadOnlyDictionary<string, MqttQualityOfServiceLevel> Subscriptions =>
        _subscriptions.ToDictionary(kv => kv.Key, kv => kv.Value);
    // 计算下一次重连延迟，不实际启动重连（测试用）。
    internal TimeSpan NextDelayForTest()
    {
        return NextDelay();
    }
    // 手动触发一次重连循环（测试用）。
    internal Task TriggerReconnectForTest()
    {
        return ReconnectLoopAsync();
    }
    #endregion
}