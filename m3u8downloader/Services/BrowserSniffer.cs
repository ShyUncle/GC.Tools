using System.Diagnostics;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace m3u8downloader.Services;

public sealed class BrowserSniffOptions
{
    /// <summary>true=无头后台渲染；false=打开可见窗口，便于人工登录 / 点播放。</summary>
    public bool Headless { get; init; } = true;
    /// <summary>有头模式为最长监听时间（可随时手动结束），无头模式为固定等待时间。</summary>
    public int WaitSeconds { get; init; } = 12;
    public string? BrowserPath { get; init; }
    /// <summary>无头模式自动触发播放器（点常见播放按钮、调用 video.play）。</summary>
    public bool AutoPlay { get; init; } = true;
    /// <summary>复用固定 profile，保留登录态；false 时每次用临时 profile 且结束后删除。</summary>
    public bool PersistentProfile { get; init; } = true;
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public ProxyOptions? Proxy { get; init; }
    public Action<string>? Log { get; set; }
}

/// <summary>
/// 通过 Chrome DevTools Protocol 让真实浏览器渲染页面，监听全部网络请求，
/// 捕获由 JS 动态生成的 m3u8 地址及其完整请求头（Cookie / Referer / Authorization 等）。
/// 不依赖 Playwright / Selenium，只用 .NET 内置的 ClientWebSocket。
/// </summary>
public sealed class SniffSession : IAsyncDisposable
{
    private static readonly HashSet<string> SkipHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "host", "content-length", "accept-encoding", "content-encoding", "transfer-encoding",
        "connection", "keep-alive", "upgrade", "te", "trailer", "proxy-authorization",
        "sec-ch-ua", "sec-ch-ua-mobile", "sec-ch-ua-platform"
    };

    private const string AutoPlayScript = """
        (function(){
          try{
            document.querySelectorAll('video').forEach(function(v){
              if(v.paused){ v.muted=true; try{var p=v.play(); if(p&&p.catch)p.catch(function(){});}catch(e){} }
            });
            var sels=['.vjs-big-play-button','.prism-big-play-btn','.xgplayer-play','.dplayer-play-icon',
                      '[class*="big-play"]','[class*="play-btn"]','[class*="playBtn"]',
                      '[class*="player-play"]','[aria-label*="播放"]','[title*="播放"]'];
            for(var i=0;i<sels.length;i++){
              var el=document.querySelector(sels[i]);
              if(el){ var r=el.getBoundingClientRect(); if(r.width>0&&r.height>0){ el.click(); break; } }
            }
            if(document.body) window.scrollBy(0, Math.min(360, document.body.scrollHeight/4));
          }catch(e){}
        })()
        """;

    private readonly BrowserSniffOptions _options;
    private readonly Process _process;
    private readonly ClientWebSocket _socket;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _profileDir;
    private readonly bool _deleteProfile;
    private readonly object _gate = new();
    private readonly List<MediaCandidate> _candidates = [];
    private readonly List<string> _recentRequests = [];
    private readonly Dictionary<string, RequestInfo> _requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<MediaCandidate>> _capturedByRequest =
        new(StringComparer.Ordinal);
    private readonly Task _pumpTask;
    private Task? _interactionTask;
    private int _commandId;
    private bool _stopped;
    private string _status = "正在启动浏览器…";

    private sealed class RequestInfo
    {
        public string Url = "";
        public string? Type;
        public Dictionary<string, string> Headers = new(StringComparer.OrdinalIgnoreCase);
    }

    private SniffSession(BrowserSniffOptions options, Process process, ClientWebSocket socket,
        string profileDir, bool deleteProfile)
    {
        _options = options;
        _process = process;
        _socket = socket;
        _profileDir = profileDir;
        _deleteProfile = deleteProfile;
        _pumpTask = Task.Run(PumpAsync);
    }

    public IReadOnlyList<MediaCandidate> Candidates
    {
        get { lock (_gate) return _candidates.OrderByDescending(c => c.Score).ToList(); }
    }

    /// <summary>最近观察到的 XHR / Fetch / Media 请求，用于「抓不到地址」时排查。</summary>
    public IReadOnlyList<string> RecentRequests
    {
        get { lock (_gate) return _recentRequests.ToList(); }
    }

    public string Status => Volatile.Read(ref _status);
    public bool IsRunning => !_stopped && _socket.State == WebSocketState.Open;

    /// <summary>每捕获到一个新候选触发（后台线程）。</summary>
    public event Action<MediaCandidate>? CandidateAdded;

    /// <summary>浏览器可执行文件路径；未配置时自动查找 Chrome / Edge。</summary>
    public static string? FindBrowser(string? configured = null)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        var paths = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var root in new[] { pf, pf86, local })
            {
                if (string.IsNullOrEmpty(root)) continue;
                paths.Add(Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"));
                paths.Add(Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe"));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            paths.Add("/Applications/Google Chrome.app/Contents/MacOS/Google Chrome");
            paths.Add("/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge");
        }
        else
        {
            foreach (var name in new[] { "google-chrome", "chromium", "chromium-browser", "microsoft-edge" })
                paths.Add(Path.Combine("/usr/bin", name));
        }

        var found = paths.FirstOrDefault(File.Exists);
        if (found is not null) return found;

        string executable = OperatingSystem.IsWindows() ? "chrome.exe" : "google-chrome";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator))
        {
            try
            {
                var path = Path.Combine(directory.Trim().Trim('"'), executable);
                if (File.Exists(path)) return path;
            }
            catch { /* 忽略非法 PATH 项 */ }
        }
        return null;
    }

    public static string ProfileDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "m3u8downloader", "browser-profile");

    public static async Task<SniffSession> StartAsync(
        string pageUrl, BrowserSniffOptions options, CancellationToken ct = default)
    {
        var browser = FindBrowser(options.BrowserPath)
            ?? throw new InvalidOperationException(
                "未找到 Chrome / Edge 浏览器，请在高级设置中填写浏览器可执行文件路径");

        var deleteProfile = !options.PersistentProfile;
        var profileDir = options.PersistentProfile
            ? ProfileDirectory
            : Path.Combine(Path.GetTempPath(), "m3u8downloader", "browser-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(profileDir);

        var info = new ProcessStartInfo(browser)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = false
        };
        void Arg(string value) => info.ArgumentList.Add(value);

        Arg("--remote-debugging-port=0");
        Arg("--remote-allow-origins=*");
        Arg("--user-data-dir=" + profileDir);
        Arg("--no-first-run");
        Arg("--no-default-browser-check");
        Arg("--disable-background-networking");
        Arg("--disable-blink-features=AutomationControlled");
        Arg("--autoplay-policy=no-user-gesture-required");
        Arg("--window-size=1280,900");
        if (options.Headless) { Arg("--headless=new"); Arg("--disable-gpu"); }
        if (options.Proxy is { } proxy)
        {
            var scheme = proxy.Address.Scheme.Equals("socks5", StringComparison.OrdinalIgnoreCase)
                ? "socks5" : "http";
            Arg($"--proxy-server={scheme}://{proxy.Address.Host}:{proxy.Address.Port}");
            if (!string.IsNullOrEmpty(proxy.Username))
                options.Log?.Invoke("代理需要账号密码时，浏览器可能弹出认证提示；建议改用免密代理或在浏览器窗口内完成认证。");
        }
        Arg("about:blank");

        Process? process;
        try { process = Process.Start(info); }
        catch (Exception ex)
        {
            throw new InvalidOperationException("启动浏览器失败：" + ex.Message, ex);
        }
        if (process is null)
            throw new InvalidOperationException("启动浏览器失败：未获得进程句柄");

        try
        {
            var portFile = Path.Combine(profileDir, "DevToolsActivePort");
            var port = 0;
            for (var i = 0; i < 50 && port == 0; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(portFile))
                {
                    var lines = File.ReadAllLines(portFile);
                    if (lines.Length > 0) int.TryParse(lines[0].Trim(), out port);
                }
                if (port == 0) await Task.Delay(200, ct).ConfigureAwait(false);
            }
            if (port == 0)
                throw new InvalidOperationException(
                    "浏览器调试端口未就绪。若之前异常退出，请在设置中清除浏览器数据后重试。");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var target = await FindPageTargetAsync(http, port, ct).ConfigureAwait(false);

            var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            await socket.ConnectAsync(new Uri(target), ct).ConfigureAwait(false);

            var session = new SniffSession(options, process, socket, profileDir, deleteProfile);
            await session.InitializeAsync(pageUrl, ct).ConfigureAwait(false);
            return session;
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
            if (deleteProfile) TryDeleteDirectory(profileDir);
            throw;
        }
    }

    private static async Task<string> FindPageTargetAsync(HttpClient http, int port, CancellationToken ct)
    {
        var baseUrl = $"http://127.0.0.1:{port}";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var json = await http.GetStringAsync(baseUrl + "/json/list", ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.GetProperty("type").GetString() != "page") continue;
                    if (item.TryGetProperty("webSocketDebuggerUrl", out var ws))
                        return ws.GetString()!;
                }
            }
            catch (HttpRequestException) { /* 端口刚起来，稍后重试 */ }

            // 没有普通页面时显式创建一个
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Put, baseUrl + "/json/new?about:blank");
                using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var ws))
                        return ws.GetString()!;
                }
            }
            catch { /* 下一轮重试 */ }
            await Task.Delay(300, ct).ConfigureAwait(false);
        }
        throw new InvalidOperationException("无法连接浏览器调试页面");
    }

    private async Task InitializeAsync(string pageUrl, CancellationToken ct)
    {
        await SendAsync("Page.enable", null, ct).ConfigureAwait(false);
        await SendAsync("Network.enable", new { maxTotalBufferSize = 20_000_000, maxResourceBufferSize = 10_000_000 }, ct)
            .ConfigureAwait(false);
        await SendAsync("Runtime.enable", null, ct).ConfigureAwait(false);

        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in _options.Headers)
        {
            if (SkipHeaders.Contains(key) || key.StartsWith(':')) continue;
            if (key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
            {
                await SendAsync("Emulation.setUserAgentOverride",
                    new { userAgent = value, acceptLanguage = "zh-CN,zh;q=0.9,en;q=0.8" }, ct)
                    .ConfigureAwait(false);
            }
            extra[key] = value;
        }
        if (extra.Count > 0)
            await SendAsync("Network.setExtraHTTPHeaders", new { headers = extra }, ct).ConfigureAwait(false);

        // Cookie 用 setCookie 注入，比 extra headers 更贴近真实登录态
        if (_options.Headers.TryGetValue("Cookie", out var cookieHeader))
        {
            foreach (var part in cookieHeader.Split(';'))
            {
                var index = part.IndexOf('=');
                if (index <= 0) continue;
                var name = part[..index].Trim();
                var value = part[(index + 1)..].Trim();
                if (name.Length == 0) continue;
                try
                {
                    await SendAsync("Network.setCookie", new { name, value, url = pageUrl }, ct)
                        .ConfigureAwait(false);
                }
                catch { /* 单个 Cookie 非法不影响其它 */ }
            }
        }

        Volatile.Write(ref _status, "正在渲染页面并监听视频请求…");
        await SendAsync("Page.navigate", new { url = pageUrl }, ct).ConfigureAwait(false);
        _interactionTask = Task.Run(() => InteractionLoopAsync(_cts.Token));
    }

    /// <summary>等待指定时长（无头模式），或等到手动 Stop（有头模式）。</summary>
    public async Task WaitAsync(CancellationToken ct = default)
    {
        var seconds = Math.Clamp(_options.WaitSeconds, 3, 300);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        try
        {
            if (_options.Headless)
                await Task.Delay(TimeSpan.FromSeconds(seconds), linked.Token).ConfigureAwait(false);
            else
                await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { /* 正常结束 */ }
    }

    private async Task InteractionLoopAsync(CancellationToken ct)
    {
        try
        {
            for (var i = 0; i < 40 && !ct.IsCancellationRequested; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(2.5), ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;

                var script = _options.Headless && _options.AutoPlay
                    ? AutoPlayScript
                    : "try{window.scrollBy(0,Math.min(240,(document.body||{}).scrollHeight/5||0));}catch(e){}";
                await SendAsync("Runtime.evaluate",
                    new { expression = script, returnByValue = true, awaitPromise = false }, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.WriteLine("[sniffer] interaction: " + ex.Message); }
    }

    private async Task SendAsync(string method, object? parameters, CancellationToken ct)
    {
        if (_socket.State != WebSocketState.Open) return;
        var payload = JsonSerializer.Serialize(new
        {
            id = Interlocked.Increment(ref _commandId),
            method,
            @params = parameters
        });
        var bytes = Encoding.UTF8.GetBytes(payload);
        await _sendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text,
                true, ct).ConfigureAwait(false);
        }
        finally { _sendGate.Release(); }
    }

    private readonly SemaphoreSlim _sendGate = new(1, 1);

    private async Task PumpAsync()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (_socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                using var stream = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token)
                        .ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    stream.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                try { HandleMessage(Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length)); }
                catch (Exception ex) { Debug.WriteLine("[sniffer] message: " + ex.Message); }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex) { Debug.WriteLine("[sniffer] pump: " + ex.Message); }
        finally
        {
            Volatile.Write(ref _status, "监听已结束");
        }
    }

    private void HandleMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("method", out var methodElement)) return;
        if (!root.TryGetProperty("params", out var p)) return;

        switch (methodElement.GetString())
        {
            case "Network.requestWillBeSent": OnRequest(p); break;
            case "Network.requestWillBeSentExtraInfo": OnExtraInfo(p); break;
            case "Network.responseReceived": OnResponse(p); break;
        }
    }

    private void OnRequest(JsonElement p)
    {
        if (!p.TryGetProperty("request", out var request)) return;
        var url = request.GetProperty("url").GetString() ?? "";
        var id = p.GetProperty("requestId").GetString() ?? "";
        var type = p.TryGetProperty("type", out var t) ? t.GetString() : null;
        var headers = ReadHeaders(request);

        lock (_gate)
        {
            _requests[id] = new RequestInfo { Url = url, Type = type, Headers = headers };
            if ((type is "XHR" or "Fetch" or "Media" ||
                 url.Contains("api", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("play", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("video", StringComparison.OrdinalIgnoreCase)) &&
                _recentRequests.Count < 250)
                _recentRequests.Add((type ?? "GET") + " " + url);
        }

        Capture(url, id, "JS 请求", null, headers, type);

        if (p.TryGetProperty("redirectResponse", out var redirect) &&
            redirect.TryGetProperty("url", out var redirectUrl))
            Capture(redirectUrl.GetString() ?? "", id, "重定向", null, headers, type);
    }

    private void OnExtraInfo(JsonElement p)
    {
        var id = p.GetProperty("requestId").GetString() ?? "";
        var headers = ReadHeaders(p);

        // HttpOnly Cookie 不在普通 request.headers 里，CDP 会从 associatedCookies 提供
        if (p.TryGetProperty("associatedCookies", out var cookies) &&
            cookies.ValueKind == JsonValueKind.Array)
        {
            var pairs = new List<string>();
            foreach (var cookie in cookies.EnumerateArray())
            {
                if (cookie.TryGetProperty("blockedReasons", out var blocked) &&
                    blocked.ValueKind == JsonValueKind.Array && blocked.GetArrayLength() > 0) continue;
                if (!cookie.TryGetProperty("cookie", out var detail)) continue;
                var name = detail.GetProperty("name").GetString() ?? "";
                var value = detail.GetProperty("value").GetString() ?? "";
                if (name.Length > 0) pairs.Add($"{name}={value}");
            }
            if (pairs.Count > 0) headers["Cookie"] = string.Join("; ", pairs);
        }

        lock (_gate)
        {
            if (_requests.TryGetValue(id, out var info))
                foreach (var (key, value) in headers) info.Headers[key] = value;
            if (_capturedByRequest.TryGetValue(id, out var captured))
                foreach (var candidate in captured) MergeHeaders(candidate.Headers, headers);
        }
    }

    private void OnResponse(JsonElement p)
    {
        if (!p.TryGetProperty("response", out var response)) return;
        var mime = response.TryGetProperty("mimeType", out var m) ? m.GetString() : null;
        var url = response.GetProperty("url").GetString() ?? "";
        var id = p.GetProperty("requestId").GetString() ?? "";
        if (mime is null ||
            (!mime.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) &&
             !mime.Contains("dash+xml", StringComparison.OrdinalIgnoreCase) &&
             !mime.Contains("x-mpeg", StringComparison.OrdinalIgnoreCase))) return;

        Dictionary<string, string> headers;
        string? type;
        lock (_gate)
        {
            headers = _requests.TryGetValue(id, out var info)
                ? new Dictionary<string, string>(info.Headers, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            type = _requests.TryGetValue(id, out var info2) ? info2.Type : null;
        }
        Capture(url, id, "响应类型", mime, headers, type);
    }

    private void Capture(string url, string requestId, string source, string? mime,
        Dictionary<string, string> headers, string? resourceType)
    {
        if (!MediaUrlRules.IsPlaylist(url, mime, resourceType)) return;
        MediaCandidate? added = null;
        lock (_gate)
        {
            var candidate = _candidates.FirstOrDefault(c =>
                string.Equals(c.Url, url, StringComparison.OrdinalIgnoreCase));
            if (candidate is null)
            {
                candidate = new MediaCandidate(url, source) { MimeType = mime };
                _candidates.Add(candidate);
                added = candidate;
            }
            else if (candidate.MimeType is null && mime is not null)
            {
                candidate.MimeType = mime;
            }

            MergeHeaders(candidate.Headers, headers);
            if (!_capturedByRequest.TryGetValue(requestId, out var list))
            {
                list = [];
                _capturedByRequest[requestId] = list;
            }
            if (!list.Contains(candidate)) list.Add(candidate);
        }
        if (added is not null)
        {
            try { CandidateAdded?.Invoke(added); }
            catch (Exception ex) { Debug.WriteLine("[sniffer] callback: " + ex.Message); }
        }
    }

    private static Dictionary<string, string> ReadHeaders(JsonElement element)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!element.TryGetProperty("headers", out var headers) ||
            headers.ValueKind != JsonValueKind.Object) return result;
        foreach (var property in headers.EnumerateObject())
        {
            var key = property.Name;
            if (key.StartsWith(':') || SkipHeaders.Contains(key)) continue;
            if (property.Value.ValueKind == JsonValueKind.String)
                result[key] = property.Value.GetString() ?? "";
            else if (property.Value.ValueKind == JsonValueKind.Array)
                result[key] = string.Join(", ", property.Value.EnumerateArray()
                    .Select(v => v.GetString() ?? ""));
        }
        return result;
    }

    private static void MergeHeaders(Dictionary<string, string> target, Dictionary<string, string> source)
    {
        foreach (var (key, value) in source)
        {
            if (key.StartsWith(':') || SkipHeaders.Contains(key) || string.IsNullOrEmpty(value)) continue;
            target[key] = value;
        }
    }

    public async Task StopAsync()
    {
        if (_stopped) return;
        _stopped = true;
        Volatile.Write(ref _status, "正在关闭浏览器…");
        _cts.Cancel();
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                    .ConfigureAwait(false);
        }
        catch { /* 忽略 */ }

        try { await _pumpTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); } catch { }
        if (_interactionTask is not null)
            try { await _interactionTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch { }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
        }
        catch { /* 忽略 */ }

        _socket.Dispose();
        _process.Dispose();
        if (_deleteProfile) TryDeleteDirectory(_profileDir);
        Volatile.Write(ref _status, "已结束");
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch { /* 浏览器可能尚在释放文件，留给下次启动清理 */ }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
