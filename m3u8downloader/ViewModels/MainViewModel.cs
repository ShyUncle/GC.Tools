using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using m3u8downloader.Services;

namespace m3u8downloader.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36";

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _refreshTicks;
    private CancellationTokenSource? _probeCts;
    private SniffSession? _probeSession;

    [ObservableProperty] public partial string UrlText { get; set; } = "";
    [ObservableProperty] public partial string RefererText { get; set; } = "";
    [ObservableProperty] public partial string UserAgentText { get; set; } = DefaultUserAgent;
    [ObservableProperty] public partial string CustomHeadersText { get; set; } = "";
    [ObservableProperty] public partial string SaveDirectory { get; set; } = GetDefaultDirectory();
    [ObservableProperty] public partial string FileName { get; set; } = "";
    [ObservableProperty] public partial string HintText { get; set; } = "";
    [ObservableProperty] public partial bool HasHint { get; set; }
    [ObservableProperty] public partial double Concurrency { get; set; } = 8;
    [ObservableProperty] public partial bool AutoRemux { get; set; } = true;
    [ObservableProperty] public partial string CacheSizeText { get; set; } = "缓存占用：0 B";

    // 浏览器渲染解析：解决 JS 动态生成播放地址 / 需要登录态的页面
    [ObservableProperty] public partial bool BrowserSniffEnabled { get; set; } = true;
    [ObservableProperty] public partial bool BrowserHeaded { get; set; }
    [ObservableProperty] public partial double BrowserWaitSeconds { get; set; } = 12;
    [ObservableProperty] public partial string BrowserPath { get; set; } = "";
    [ObservableProperty] public partial bool IsProbing { get; set; }
    [ObservableProperty] public partial string ProbeStatus { get; set; } = "";
    [ObservableProperty] public partial bool HasProbeResults { get; set; }

    public ObservableCollection<ProbeResultViewModel> ProbeResults { get; } = [];
    public string ProbeButtonText => IsProbing ? "■  结束探测" : "🔍  探测页面";

    partial void OnIsProbingChanged(bool value) => OnPropertyChanged(nameof(ProbeButtonText));

    // 代理：0=直连，1=HTTP/HTTPS，2=SOCKS5
    [ObservableProperty] public partial int ProxyModeIndex { get; set; }
    [ObservableProperty] public partial string ProxyHost { get; set; } = "127.0.0.1";
    [ObservableProperty] public partial string ProxyPort { get; set; } = "7890";
    [ObservableProperty] public partial string ProxyUsername { get; set; } = "";
    [ObservableProperty] public partial string ProxyPassword { get; set; } = "";

    public ObservableCollection<DownloadItemViewModel> Tasks { get; } = [];
    public bool HasTasks => Tasks.Count > 0;
    public bool IsEmpty => Tasks.Count == 0;
    public bool IsProxyConfigured => ProxyModeIndex > 0;
    public string TaskCountText => $"共 {Tasks.Count} 个任务";

    partial void OnProxyModeIndexChanged(int value) => OnPropertyChanged(nameof(IsProxyConfigured));

    public MainViewModel()
    {
        Tasks.CollectionChanged += OnTasksChanged;
        _timer.Tick += (_, _) => RefreshAll();
        _timer.Start();
        // 启动时清理缓存：只删程序自建目录，绝不动成品文件
        try
        {
            // 旧版本散落在 %TEMP% 的分片新版不再使用，直接清掉
            TaskPaths.CleanupStale(TimeSpan.Zero, roots: [TaskPaths.LegacyCacheRoot]);
            // 新版缓存只清 24h 前的孤立目录，避免影响异常退出后仍可续传的任务
            TaskPaths.CleanupStale(TimeSpan.FromHours(24), roots: [TaskPaths.CacheRoot]);
        }
        catch { /* 清理失败不影响使用 */ }
        UpdateCacheSize();
    }

    private void OnTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasTasks));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(TaskCountText));
    }

    private void RefreshAll()
    {
        foreach (var item in Tasks) item.Refresh();
        if (++_refreshTicks % 20 == 0) UpdateCacheSize(); // 每 10 秒统计一次
    }

    private void UpdateCacheSize()
    {
        try { CacheSizeText = "缓存占用：" + FormatBytes(TaskPaths.CacheSize()); }
        catch { CacheSizeText = "缓存占用：未知"; }
    }

    [RelayCommand]
    private void CleanupCache()
    {
        try
        {
            // 排除正在下载的任务，其余孤立缓存一次性清掉
            var active = Tasks.Select(t => t.Job.TempDirectory).ToArray();
            var (directories, bytes) = TaskPaths.CleanupStale(TimeSpan.Zero, active);
            HintText = directories > 0
                ? $"已清理 {directories} 个缓存目录，释放 {FormatBytes(bytes)}（成品文件未受影响）"
                : "没有需要清理的缓存";
            HasHint = true;
            UpdateCacheSize();
        }
        catch (Exception ex)
        {
            HintText = "清理失败：" + ex.Message;
            HasHint = true;
        }
    }

    private static string FormatBytes(long bytes)
    {
        var value = (double)bytes;
        if (value < 1024) return $"{value:0} B";
        if (value < 1024 * 1024) return $"{value / 1024:0.0} KB";
        if (value < 1024L * 1024 * 1024) return $"{value / (1024 * 1024):0.0} MB";
        return $"{value / (1024L * 1024 * 1024):0.00} GB";
    }

    [RelayCommand]
    private void AddTask()
    {
        // 支持一次粘贴多行地址，批量创建任务
        var lines = UrlText.Split(['\r', '\n'],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var valid = new List<Uri>();
        var invalidCount = 0;
        foreach (var line in lines)
        {
            if (Uri.TryCreate(line, UriKind.Absolute, out var parsed) &&
                (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
                valid.Add(parsed);
            else
                invalidCount++;
        }

        if (valid.Count == 0)
        {
            HintText = "请输入有效的 http/https 视频地址（每行一个，可批量添加）";
            HasHint = true;
            return;
        }

        var saveDirectory = SaveDirectory.Trim();
        if (saveDirectory.Length == 0)
        {
            HintText = "请填写保存目录";
            HasHint = true;
            return;
        }
        try
        {
            Directory.CreateDirectory(saveDirectory);
            SaveDirectory = saveDirectory;
        }
        catch (Exception ex)
        {
            HintText = "保存目录不可用：" + ex.Message;
            HasHint = true;
            return;
        }

        ProxyOptions? proxy;
        try
        {
            proxy = BuildProxy();
        }
        catch (Exception ex)
        {
            HintText = "代理配置无效：" + ex.Message;
            HasHint = true;
            return;
        }

        HasHint = invalidCount > 0;
        HintText = invalidCount > 0 ? $"已跳过 {invalidCount} 个无效地址" : "";

        var headers = BuildHeaders();

        foreach (var (uri, index) in valid.Select((u, i) => (u, i)))
        {
            var name = string.IsNullOrWhiteSpace(FileName) || valid.Count > 1
                ? DeriveFileName(uri, AutoRemux)
                : SanitizeFileName(FileName.Trim());
            if (!Path.HasExtension(name)) name += AutoRemux ? ".mp4" : ".ts";
            // 同一批链接自动派生文件名相同时追加序号，避免覆盖
            if (valid.Count > 1)
            {
                var stem = Path.GetFileNameWithoutExtension(name);
                var extension = Path.GetExtension(name);
                name = $"{stem}_{index + 1}{extension}";
            }
            name = UniqueFileName(saveDirectory, name);

            var job = new DownloadJob(new DownloadOptions
            {
                Url = uri.ToString(),
                SaveDirectory = saveDirectory,
                FileName = name,
                Concurrency = (int)Math.Clamp(Concurrency, 1, 32),
                AutoRemux = AutoRemux,
                Headers = headers,
                Proxy = proxy,
                BrowserSniff = BrowserSniffEnabled ? BuildBrowserSniffOptions(proxy, headers) : null
            });

            Tasks.Add(new DownloadItemViewModel(job, RemoveItem));
            job.Start();
        }

        FileName = "";
        UrlText = "";
        UpdateCacheSize();
    }

    private Dictionary<string, string> BuildHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["User-Agent"] = string.IsNullOrWhiteSpace(UserAgentText) ? DefaultUserAgent : UserAgentText.Trim(),
            ["Accept"] = "*/*",
            ["Accept-Language"] = "zh-CN,zh;q=0.9,en;q=0.8"
        };
        if (!string.IsNullOrWhiteSpace(RefererText))
            headers["Referer"] = RefererText.Trim();
        foreach (var line in CustomHeadersText.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                var key = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (key.Length > 0 && value.Length > 0) headers[key] = value;
            }
        }
        return headers;
    }

    private BrowserSniffOptions BuildBrowserSniffOptions(
        ProxyOptions? proxy, Dictionary<string, string>? headers = null) => new()
    {
        Headless = !BrowserHeaded,
        WaitSeconds = (int)Math.Clamp(BrowserWaitSeconds, 3, 120),
        BrowserPath = string.IsNullOrWhiteSpace(BrowserPath) ? null : BrowserPath.Trim(),
        PersistentProfile = true,
        Headers = headers ?? BuildHeaders(),
        Proxy = proxy
    };

    private static HttpClient BuildHttpClient(Dictionary<string, string> headers, ProxyOptions? proxy)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = proxy is not null
        };
        if (proxy is { } p)
        {
            var webProxy = new WebProxy(p.Address);
            if (!string.IsNullOrEmpty(p.Username))
                webProxy.Credentials = new NetworkCredential(p.Username, p.Password ?? "");
            handler.Proxy = webProxy;
        }
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        foreach (var (key, value) in headers)
            http.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
        return http;
    }

    /// <summary>
    /// 「探测页面」：先静态增强解析，失败则启动真实浏览器渲染并监听全部网络请求，
    /// 把 JS 动态生成的 m3u8 与浏览器实际携带的请求头一起列出来供选择。
    /// </summary>
    [RelayCommand]
    private async Task ProbePageAsync()
    {
        if (IsProbing) { await StopProbeAsync(); return; }

        var raw = UrlText.Split(['\r', '\n'],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            HintText = "请先在地址框填入一个视频页面地址，再点「探测页面」";
            HasHint = true;
            return;
        }

        IsProbing = true;
        ProbeResults.Clear();
        HasProbeResults = false;
        ProbeStatus = "正在静态解析页面…";
        HasHint = false;
        HintText = "";
        _probeCts = new CancellationTokenSource();
        var ct = _probeCts.Token;

        try
        {
            var headers = BuildHeaders();
            var proxy = BuildProxy();
            using var http = BuildHttpClient(headers, proxy);
            var html = await http.GetStringAsync(uri, ct).ConfigureAwait(true);

            if (!html.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                foreach (var item in await PageMediaDiscovery
                             .ExtractFromPageAsync(http, uri, html, ct).ConfigureAwait(true))
                    AddProbeResult(item);

            if (!HasProbeResults)
            {
                ProbeStatus = BrowserHeaded
                    ? "已打开浏览器窗口：请登录并播放视频，捕获到地址后点「结束探测」"
                    : "页面地址由 JS 动态生成，正在用浏览器渲染并监听视频请求…";
                var options = BuildBrowserSniffOptions(proxy, headers);
                _probeSession = await SniffSession.StartAsync(uri.ToString(), options, ct).ConfigureAwait(true);
                _probeSession.CandidateAdded += OnCandidateCaptured;
                await _probeSession.WaitAsync(ct).ConfigureAwait(true);
                foreach (var item in _probeSession.Candidates) AddProbeResult(item);
            }

            ProbeStatus = HasProbeResults
                ? $"捕获到 {ProbeResults.Count} 个播放地址，点「使用」回填"
                : "未捕获到播放地址。可勾选「有头模式」后在浏览器里手动登录 / 播放一次再试";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ProbeStatus = $"探测已结束，共 {ProbeResults.Count} 个候选";
        }
        catch (Exception ex)
        {
            ProbeStatus = "探测失败：" + ex.Message;
            HintText = ProbeStatus;
            HasHint = true;
        }
        finally
        {
            if (_probeSession is { } session)
            {
                session.CandidateAdded -= OnCandidateCaptured;
                try { await session.StopAsync().ConfigureAwait(true); } catch { /* 忽略 */ }
                _probeSession = null;
            }
            _probeCts?.Dispose();
            _probeCts = null;
            IsProbing = false;
        }
    }

    private async Task StopProbeAsync()
    {
        try { _probeCts?.Cancel(); } catch { /* 已释放 */ }
        if (_probeSession is { } session)
            try { await session.StopAsync().ConfigureAwait(true); } catch { /* 忽略 */ }
    }

    private void OnCandidateCaptured(MediaCandidate candidate) =>
        Dispatcher.UIThread.Post(() => AddProbeResult(candidate));

    private void AddProbeResult(MediaCandidate candidate)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AddProbeResult(candidate));
            return;
        }
        if (ProbeResults.Any(r => string.Equals(r.Url, candidate.Url, StringComparison.OrdinalIgnoreCase)))
            return;
        ProbeResults.Add(new ProbeResultViewModel(candidate, UseProbeResult));
        HasProbeResults = true;
        if (IsProbing) ProbeStatus = $"已捕获 {ProbeResults.Count} 个播放地址…";
    }

    /// <summary>把候选地址和浏览器实际携带的鉴权头回填到输入区。</summary>
    private void UseProbeResult(MediaCandidate candidate)
    {
        UrlText = candidate.Url;
        if (candidate.Headers.TryGetValue("Referer", out var referer) && !string.IsNullOrWhiteSpace(referer))
            RefererText = referer;
        if (candidate.Headers.TryGetValue("User-Agent", out var ua) && !string.IsNullOrWhiteSpace(ua))
            UserAgentText = ua;

        var lines = CustomHeadersText
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        foreach (var (key, value) in candidate.Headers)
        {
            var keep = key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                       key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                       key.Equals("Origin", StringComparison.OrdinalIgnoreCase) ||
                       key.Equals("Token", StringComparison.OrdinalIgnoreCase) ||
                       key.StartsWith("X-", StringComparison.OrdinalIgnoreCase);
            if (!keep || string.IsNullOrWhiteSpace(value)) continue;
            lines.RemoveAll(l => l.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
            lines.Add($"{key}: {value}");
        }
        CustomHeadersText = string.Join("\n", lines);
        HintText = "已回填播放地址与浏览器请求头（Cookie / Referer 等），点「添加下载任务」即可";
        HasHint = true;
    }

    [RelayCommand]
    private void ClearBrowserData()
    {
        try
        {
            if (IsProbing)
            {
                HintText = "请先结束探测，再清除浏览器数据";
                HasHint = true;
                return;
            }
            var directory = SniffSession.ProfileDirectory;
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
                HintText = "浏览器登录数据已清除（下次探测需重新登录）";
            }
            else HintText = "没有浏览器数据需要清除";
        }
        catch (Exception ex) { HintText = "清除失败：" + ex.Message; }
        HasHint = true;
    }

    [RelayCommand]
    private void ClearCompleted()
    {
        foreach (var item in Tasks.Where(t => t.State is DownloadState.Completed or DownloadState.Canceled).ToArray())
            RemoveItem(item);
    }

    private void RemoveItem(DownloadItemViewModel item)
    {
        Tasks.Remove(item);
        item.CleanupCache(); // 只清缓存，任务文件夹里的成品保留
        item.Dispose();
        UpdateCacheSize();
    }

    private ProxyOptions? BuildProxy()
    {
        if (ProxyModeIndex == 0) return null;

        var host = ProxyHost.Trim();
        foreach (var scheme in new[] { "http://", "https://", "socks5://", "socks4://" })
            if (host.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                host = host[scheme.Length..];
        host = host.TrimEnd('/');

        if (host.Length == 0) throw new InvalidOperationException("请填写代理地址");
        if (!int.TryParse(ProxyPort.Trim(), out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("端口须为 1–65535");

        var schemeName = ProxyModeIndex == 2 ? "socks5" : "http";
        if (!Uri.TryCreate($"{schemeName}://{host}:{port}", UriKind.Absolute, out var address))
            throw new InvalidOperationException("代理地址格式不正确");

        return new ProxyOptions
        {
            Address = address,
            Username = string.IsNullOrWhiteSpace(ProxyUsername) ? null : ProxyUsername.Trim(),
            Password = string.IsNullOrWhiteSpace(ProxyPassword) ? null : ProxyPassword
        };
    }

    private static string GetDefaultDirectory()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        return Directory.Exists(downloads)
            ? downloads
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string DeriveFileName(Uri uri, bool remux)
    {
        var segment = Uri.UnescapeDataString(uri.AbsolutePath.Split('/').LastOrDefault() ?? "");
        if (segment.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)) segment = segment[..^5];
        var name = SanitizeFileName(segment);
        if (string.IsNullOrWhiteSpace(name))
            name = "video_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return name + (remux ? ".mp4" : ".ts");
    }

    private static string UniqueFileName(string directory, string name)
    {
        // 下载中会在同目录生成同名 .ts 实时文件，也要一并避让
        bool Occupied(string candidate) =>
            File.Exists(Path.Combine(directory, candidate)) ||
            File.Exists(Path.Combine(directory, Path.GetFileNameWithoutExtension(candidate) + ".ts"));

        if (!Occupied(name)) return name;
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var i = 1; ; i++)
        {
            var candidate = $"{stem}({i}){extension}";
            if (!Occupied(candidate)) return candidate;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
    }
}
