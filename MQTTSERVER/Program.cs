using MQTTnet;
using MQTTnet.Server;
using System.Buffers;
using System.Text;
namespace MQTTSERVER
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // 5.x：服务端由工厂创建，选项构建器为 MqttServerOptionsBuilder
            var options = new MqttServerOptionsBuilder()
                .WithDefaultEndpoint()               // 监听 TCP 默认端点
                .WithDefaultEndpointPort(1883)         // 端口
                                                       // 如需允许局域网设备接入，5.x 用这种方式指定网卡地址：
                                                       // .WithDefaultEndpointBoundIPAddress(System.Net.IPAddress.Any)
                .Build();
            using var server = new MqttServerFactory().CreateMqttServer(options);

            // 1) 客户端接入 / 断开
            server.ClientConnectedAsync += e =>
            {
                Console.WriteLine($"[连接] ClientId={e.ClientId}");
                return Task.CompletedTask;
            };

            server.ClientDisconnectedAsync += e =>
            {
                Console.WriteLine($"[断开] ClientId={e.ClientId}  类型={e.DisconnectType}");
                return Task.CompletedTask;
            };

            // 2) 客户端订阅
            server.ClientSubscribedTopicAsync += e =>
            {
                Console.WriteLine($"[订阅] {e.ClientId} -> {e.TopicFilter.Topic} (QoS={(int)e.TopicFilter.QualityOfServiceLevel})");
                return Task.CompletedTask;
            };

            // 3) 拦截收到的消息（联调看报文最好用这个）
            server.InterceptingPublishAsync += e =>
            {
                var payload = e.ApplicationMessage.Payload;   // 5.x：ReadOnlySequence<byte>
                var text = payload.IsSingleSegment
                    ? Encoding.UTF8.GetString(payload.FirstSpan)
                    : Encoding.UTF8.GetString(payload.ToArray());

                Console.WriteLine($"[消息] {e.ClientId} -> {e.ApplicationMessage.Topic} (QoS={(int)e.ApplicationMessage.QualityOfServiceLevel}) : {text}");
                return Task.CompletedTask;
            };
            
            // 4) 连接校验（可选）：5.x 用 ValidatingConnectionAsync 做 ClientId / 账号检查
            server.ValidatingConnectionAsync += e =>
            {
                if (string.IsNullOrWhiteSpace(e.ClientId))
                    e.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.ClientIdentifierNotValid;
                // e.ReasonCode 保持默认 Success 即放行
                return Task.CompletedTask;
            };

            await server.StartAsync();
            Console.WriteLine("MQTT Broker 已启动：mqtt://127.0.0.1:1883  (Ctrl+C 退出)");

            var stop = new TaskCompletionSource();
            Console.CancelKeyPress += (_, ev) => { ev.Cancel = true; stop.TrySetResult(); };
            await stop.Task;

            await server.StopAsync();
        }
    }
}
