using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using m3u8downloader.Models;

namespace m3u8downloader.Services;

public enum DownloadState
{
    Pending,
    Running,
    Paused,
    Merging,
    Completed,
    Failed,
    Canceled
}

public sealed class DownloadOptions
{
    public required string Url { get; init; }
    public required string SaveDirectory { get; init; }
    public required string FileName { get; init; }
    public int Concurrency { get; init; } = 8;
    public bool AutoRemux { get; init; } = true;
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public ProxyOptions? Proxy { get; init; }
    /// <summary>不为 null 时，静态解析失败会启动真实浏览器渲染页面并监听视频请求。</summary>
    public BrowserSniffOptions? BrowserSniff { get; init; }
}

/// <summary>
/// 单个下载任务：解析清单 → 并发下载/解密/净化分片 → 合并 → 可选 ffmpeg 转 MP4。
/// 点播支持断点续传；直播流自动识别并持续录制，可暂停续录、随时停止并保存。
/// </summary>
public sealed class DownloadJob : IDisposable
{
    private readonly DownloadOptions _options;
    private readonly HttpClient _http;
    private readonly TaskPaths _paths;
    private readonly string _tempDir;
    private readonly string _visiblePath;
    private readonly SemaphoreSlim _appendGate = new(1, 1);
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private int _state = (int)DownloadState.Pending;
    private string _message = "等待开始";
    private int _completed;
    private int _total;
    private long _bytes;
    private int _failed;
    private bool _isLive;
    private int _finishRequested;
    private bool _disposed;
    private string _displayFileName;
    private string _liveRecordingPath = "";
    private int _appended;
    private long _bytesWritten;
    private bool _initAppended;
    private bool _visibleDisabled;

    public DownloadJob(DownloadOptions options)
    {
        _options = options;
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            UseProxy = options.Proxy is not null
        };
        if (options.Proxy is { } proxy)
        {
            var webProxy = new WebProxy(proxy.Address);
            if (!string.IsNullOrEmpty(proxy.Username))
                webProxy.Credentials = new NetworkCredential(proxy.Username, proxy.Password ?? "");
            handler.Proxy = webProxy;
        }

        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        foreach (var header in options.Headers)
            _http.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);

        // 每个任务独占一层文件夹；缓存外置到程序数据目录，随任务生命周期清理
        _paths = TaskPaths.Resolve(options.SaveDirectory, options.FileName, options.Url);
        _tempDir = _paths.CacheDirectory;
        _visiblePath = _paths.VisiblePath;
        OutputPath = _paths.OutputPath;
        _displayFileName = options.FileName;
    }

    public DownloadState State
    {
        get => (DownloadState)Volatile.Read(ref _state);
        private set => Volatile.Write(ref _state, (int)value);
    }

    public string Message => Volatile.Read(ref _message);
    public int CompletedSegments => Volatile.Read(ref _completed);
    public int TotalSegments => Volatile.Read(ref _total);
    public long TotalBytes => Interlocked.Read(ref _bytes);
    public string FileName => _options.FileName;
    public string Url => _options.Url;
    public string SaveDirectory => _options.SaveDirectory;
    public string OutputPath { get; private set; }
    public bool IsLive => Volatile.Read(ref _isLive);

    /// <summary>界面显示的文件名：完成后取实际输出名（未装 ffmpeg 时会由 .mp4 降级为 .ts）。</summary>
    public string DisplayFileName => Volatile.Read(ref _displayFileName);

    /// <summary>任务专属输出文件夹，供「位置」按钮打开。</summary>
    public string TaskDirectory => _paths.TaskDirectory;

    /// <summary>分片缓存目录，供界面「缓存」按钮打开。</summary>
    public string TempDirectory => _tempDir;

    /// <summary>移除任务时清掉缓存，但保留任务文件夹里的成品。</summary>
    public void CleanupCache() => _paths.DeleteCache();

    /// <summary>下载/录制过程中已经落盘、用户可在保存目录看到的临时视频文件。</summary>
    public string VisiblePath =>
        IsLive && _liveRecordingPath.Length > 0 ? _liveRecordingPath : _visiblePath;

    public bool HasVisibleFile => File.Exists(VisiblePath) && new FileInfo(VisiblePath).Length > 0;

    public void Start()
    {
        lock (_gate)
        {
            if (_runTask is { IsCompleted: false }) return;
            if (State is DownloadState.Completed or DownloadState.Canceled) return;

            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            Interlocked.Exchange(ref _failed, 0);
            SetState(DownloadState.Running, Volatile.Read(ref _completed) > 0 ? "继续下载…" : "准备下载…");
            var token = _cts.Token;
            _runTask = Task.Run(() => RunAsync(token));
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (State is DownloadState.Running or DownloadState.Merging)
            {
                try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            }
        }
    }

    public void Stop()
    {
        Pause();
        State = DownloadState.Canceled;
    }

    /// <summary>直播录制：结束轮询、把已录内容封装保存，任务转为完成状态。</summary>
    public void FinishRecording()
    {
        if (!IsLive || State is DownloadState.Completed or DownloadState.Canceled) return;
        Volatile.Write(ref _finishRequested, 1);
        SetMessage("正在结束录制并保存…");
        if (State == DownloadState.Paused) Start();
    }

    private void SetState(DownloadState state, string message)
    {
        State = state;
        Volatile.Write(ref _message, message);
    }

    private void SetMessage(string message) => Volatile.Write(ref _message, message);

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var (playlist, mediaUrl) = await ResolvePlaylistAsync(ct).ConfigureAwait(false);
            if (playlist.IsLive)
            {
                Volatile.Write(ref _isLive, true);
                await RecordLiveAsync(mediaUrl, playlist, ct).ConfigureAwait(false);
            }
            else
            {
                await DownloadVodAsync(playlist, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            SetState(DownloadState.Paused, IsLive
                ? "录制已暂停，点击「继续」接着录"
                : "已暂停，点击「继续」从断点续传");
        }
        catch (Exception ex)
        {
            SetState(DownloadState.Failed, "失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 解析入口：静态增强解析（HTML + 外部 JS）→ 失败时真实浏览器渲染监听 →
    /// 逐层解析嵌套主清单（最多 6 层）→ 返回媒体清单。
    /// </summary>
    private async Task<(Playlist Playlist, Uri Url)> ResolvePlaylistAsync(CancellationToken ct)
    {
        SetState(DownloadState.Running, "正在获取播放列表…");
        Directory.CreateDirectory(_tempDir);

        var pageUrl = new Uri(_options.Url);
        var text = await FetchTextAsync(pageUrl, ct).ConfigureAwait(false);
        Uri? resolvedUrl = null;

        if (!IsPlaylistText(text))
        {
            SetMessage("该地址不是 m3u8，正在解析网页中的播放地址…");
            var candidates = await PageMediaDiscovery
                .ExtractFromPageAsync(_http, pageUrl, text, ct).ConfigureAwait(false);

            // 逐个试探静态候选，避免把广告 / 失效地址当成播放列表
            foreach (var candidate in candidates.Where(c => c.Score > 0).Take(6))
            {
                if (!Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri)) continue;
                try
                {
                    var probe = await FetchTextAsync(uri, ct).ConfigureAwait(false);
                    if (!IsPlaylistText(probe)) continue;
                    resolvedUrl = uri;
                    text = probe;
                    break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch { /* 试下一个候选 */ }
            }

            if (resolvedUrl is null && _options.BrowserSniff is { } sniff)
            {
                sniff.Log = msg => SetMessage(msg);
                SetMessage("页面地址由 JS 动态生成，正在用浏览器渲染并监听视频请求…");
                await using var session = await SniffSession
                    .StartAsync(pageUrl.ToString(), sniff, ct).ConfigureAwait(false);
                await session.WaitAsync(ct).ConfigureAwait(false);

                foreach (var candidate in session.Candidates.Where(c => c.Score > 0).Take(6))
                {
                    if (!Uri.TryCreate(candidate.Url, UriKind.Absolute, out var uri)) continue;
                    // 浏览器里带签名 / Cookie 的请求头，原样复用到清单、密钥和分片请求
                    ApplyCapturedHeaders(candidate.Headers);
                    try
                    {
                        var probe = await FetchTextAsync(uri, ct).ConfigureAwait(false);
                        if (!IsPlaylistText(probe)) continue;
                        resolvedUrl = uri;
                        text = probe;
                        break;
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch { /* 试下一个候选 */ }
                }

                if (resolvedUrl is null)
                {
                    var hints = session.RecentRequests
                        .Where(r => r.Contains("api", StringComparison.OrdinalIgnoreCase) ||
                                    r.Contains("play", StringComparison.OrdinalIgnoreCase) ||
                                    r.Contains("video", StringComparison.OrdinalIgnoreCase))
                        .Take(3)
                        .Select(r => r.Length > 100 ? r[..100] + "…" : r)
                        .ToArray();
                    throw new InvalidOperationException(
                        "浏览器渲染后仍未捕获到可下载的 m3u8。请确认页面能正常播放、账号已登录；" +
                        "可勾选「有头模式」后点「探测页面」手动播放一次。" +
                        (hints.Length > 0 ? " 页面请求线索：" + string.Join("；", hints) : ""));
                }
            }

            if (resolvedUrl is null)
                throw new InvalidOperationException(
                    "未能从页面提取到 m3u8 地址。该页面可能由 JS 动态生成播放地址，" +
                    "请在高级设置开启「浏览器渲染解析」或点「探测页面」抓取；也可直接粘贴 m3u8 直链。");
        }

        if (!IsPlaylistText(text))
            throw new InvalidOperationException("获取到的内容不是有效的 m3u8 播放列表");

        var url = resolvedUrl ?? pageUrl;
        var playlist = M3u8Parser.Parse(url, text);
        for (var depth = 0; playlist.IsMaster && depth < 6; depth++)
        {
            if (playlist.Variants.Count == 0)
                throw new InvalidOperationException("主清单中没有可用的清晰度");
            var best = playlist.Variants.MaxBy(v => v.Bandwidth)!;
            SetMessage($"主清单（第 {depth + 1} 层），自动选择清晰度：" +
                       (string.IsNullOrEmpty(best.Resolution) ? "默认" : best.Resolution));
            url = best.Uri;
            text = await FetchTextAsync(url, ct).ConfigureAwait(false);
            if (!text.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("嵌套地址返回的不是有效的 m3u8 清单");
            playlist = M3u8Parser.Parse(url, text);
        }

        if (playlist.IsMaster)
            throw new InvalidOperationException("主清单嵌套层级过深（超过 6 层）");
        return (playlist, url);
    }

    // ---------------- 点播 ----------------

    private async Task DownloadVodAsync(Playlist playlist, CancellationToken ct)
    {
        var segments = playlist.Segments;
        if (segments.Count == 0)
            throw new InvalidOperationException("媒体清单中没有可下载的视频分片");

        Volatile.Write(ref _total, segments.Count);
        var fingerprint = PlaylistFingerprint(playlist);
        await PrepareVisibleOutputAsync(playlist, fingerprint, ct).ConfigureAwait(false);

        // 已写入输出文件的分片不再触碰；其余分片若缓存仍在则直接复用
        Volatile.Write(ref _completed, _appended);
        for (var i = _appended; i < segments.Count; i++)
        {
            var cached = SegmentPath(i);
            if (File.Exists(cached) && new FileInfo(cached).Length > 0)
                Interlocked.Increment(ref _completed);
        }
        SetMessage(_appended > 0
            ? $"从第 {_appended + 1} 个分片继续，共 {segments.Count} 个…"
            : $"共 {segments.Count} 个分片，开始下载…");

        var keys = await FetchKeysAsync(segments, ct).ConfigureAwait(false);
        using var semaphore = new SemaphoreSlim(Math.Max(1, _options.Concurrency));
        var jobs = new List<Task>();
        for (var i = _appended; i < segments.Count; i++)
            jobs.Add(DownloadVodSegmentAsync(i, segments[i], keys, semaphore, playlist, fingerprint, ct));

        await Task.WhenAll(jobs).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        var failedCount = Volatile.Read(ref _failed);
        if (failedCount > 0)
            throw new InvalidOperationException($"{failedCount} 个分片下载失败，点击「继续」可重试缺失分片");

        SetState(DownloadState.Merging, "正在收尾并封装文件…");
        await MergeAsync(playlist, fingerprint, ct).ConfigureAwait(false);
        SetState(DownloadState.Completed, "已完成：" + OutputPath);
    }

    private async Task DownloadVodSegmentAsync(
        int index, Segment segment, ConcurrentDictionary<string, byte[]> keys,
        SemaphoreSlim semaphore, Playlist playlist, string fingerprint, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            var dest = SegmentPath(index);
            if (!(File.Exists(dest) && new FileInfo(dest).Length > 0))
            {
                if (!await EnsureSegmentFileAsync(dest, segment, keys, ct).ConfigureAwait(false))
                {
                    Interlocked.Increment(ref _failed);
                    return;
                }
                Interlocked.Increment(ref _completed);
            }
            await AppendReadySegmentsAsync(playlist, fingerprint, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            semaphore.Release();
        }
    }

    // ---------------- 下载过程中可见的实时输出文件 ----------------

    private string SegmentPath(int index) => Path.Combine(_tempDir, $"{index:D6}.seg");

    private static string PlaylistFingerprint(Playlist playlist)
    {
        var first = playlist.Segments.Count > 0 ? playlist.Segments[0].Uri.ToString() : "";
        var raw = $"{playlist.Segments.Count}|{first}|{playlist.InitUri?.ToString() ?? ""}";
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(raw)))[..12];
    }

    private readonly record struct VodState(int NextIndex, long BytesWritten, bool InitWritten);

    private VodState? ReadVodState(string fingerprint)
    {
        try
        {
            if (!File.Exists(_paths.VodStatePath)) return null;
            var parts = File.ReadAllText(_paths.VodStatePath).Split('|');
            if (parts.Length < 4 || parts[0] != fingerprint) return null;
            return new VodState(
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                long.Parse(parts[2], CultureInfo.InvariantCulture),
                parts[3] == "1");
        }
        catch { return null; }
    }

    private void WriteVodState(string fingerprint)
    {
        try
        {
            var temp = _paths.VodStatePath + ".tmp";
            File.WriteAllText(temp, FormattableString.Invariant(
                $"{fingerprint}|{_appended}|{_bytesWritten}|{(_initAppended ? 1 : 0)}"));
            File.Move(temp, _paths.VodStatePath, true);
        }
        catch { /* 状态写失败时下次启动会保守重下，不会写错文件 */ }
    }

    private void ClearSegmentCache()
    {
        try
        {
            foreach (var pattern in new[] { "*.seg", "*.part" })
                foreach (var file in Directory.EnumerateFiles(_tempDir, pattern)) TryDelete(file);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 以状态文件为断点基准：校验清单指纹后把输出文件截断到已确认字节位置，从该分片继续。
    /// 状态缺失 / 清单变化 / 文件被删时保守地从头重下，保证绝不错位。
    /// </summary>
    private async Task PrepareVisibleOutputAsync(Playlist playlist, string fingerprint, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.TaskDirectory);
        Directory.CreateDirectory(_tempDir);
        _appended = 0;
        _initAppended = false;
        _bytesWritten = 0;
        _visibleDisabled = false;

        var state = ReadVodState(fingerprint);
        if (state is { } resume && File.Exists(_visiblePath))
        {
            var length = new FileInfo(_visiblePath).Length;
            if (length >= resume.BytesWritten)
            {
                if (length > resume.BytesWritten)
                {
                    // 崩溃时可能多写了未来得及确认的数据，截断掉即可
                    await using var trim = new FileStream(_visiblePath, FileMode.Open,
                        FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true);
                    trim.SetLength(resume.BytesWritten);
                }
                _appended = Math.Clamp(resume.NextIndex, 0, playlist.Segments.Count);
                _initAppended = resume.InitWritten;
                _bytesWritten = resume.BytesWritten;
                return;
            }
        }

        // 从头重建：清掉可能与新清单不匹配的旧分片
        TryDelete(_paths.VodStatePath);
        ClearSegmentCache();
        try
        {
            // 只创建空文件并立即释放句柄，保证用户随时能打开 / 播放 / 复制
            await using (var stream = new FileStream(_visiblePath, FileMode.Create,
                             FileAccess.Write, FileShare.Read, 1 << 16, useAsync: true)) { }
        }
        catch (IOException)
        {
            // 文件被占用等情况：放弃实时文件，合并阶段会自动从分片重建
            _visibleDisabled = true;
            Debug.WriteLine("[visible] 无法创建实时输出文件，回退到普通合并");
            return;
        }

        await AppendReadySegmentsAsync(playlist, fingerprint, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 把已就绪的连续分片按序追加进可见文件：数据落盘后原子更新状态，随即删掉这些分片，
    /// 缓存里只保留尚未写入的乱序分片。写句柄每批释放，避免占用导致播放器打不开文件。
    /// </summary>
    private async Task AppendReadySegmentsAsync(Playlist playlist, string fingerprint, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || _visibleDisabled) return;
        await _appendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pending = new List<(string Path, long Length)>();
            var next = _appended;
            while (next < playlist.Segments.Count)
            {
                var file = SegmentPath(next);
                if (!File.Exists(file)) break;
                var length = new FileInfo(file).Length;
                if (length == 0) break;
                pending.Add((file, length));
                next++;
            }
            var needInit = !_initAppended && _appended == 0 && playlist.InitUri is not null;
            if (pending.Count == 0 && !needInit) return;

            await using (var stream = new FileStream(_visiblePath, FileMode.Append,
                             FileAccess.Write, FileShare.Read, 1 << 20, useAsync: true))
            {
                if (needInit)
                {
                    var init = await LoadInitAsync(playlist.InitUri!, ct).ConfigureAwait(false);
                    await stream.WriteAsync(init, ct).ConfigureAwait(false);
                    _initAppended = true;
                    _bytesWritten += init.Length;
                }
                foreach (var (file, length) in pending)
                {
                    await using var source = new FileStream(file, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 1 << 20, useAsync: true);
                    await source.CopyToAsync(stream, ct).ConfigureAwait(false);
                    _bytesWritten += length;
                    _appended++;
                }
            }

            WriteVodState(fingerprint);
            foreach (var (file, _) in pending) TryDelete(file);
        }
        catch (Exception ex)
        {
            // 实时文件写入失败时保留分片，合并阶段会自动从分片重建
            Debug.WriteLine("[append] " + ex.Message);
        }
        finally { _appendGate.Release(); }
    }

    // ---------------- 直播 ----------------

    private async Task RecordLiveAsync(Uri mediaUrl, Playlist firstPlaylist, CancellationToken ct)
    {
        SetState(DownloadState.Running, "检测到直播流，开始持续录制…");
        Directory.CreateDirectory(_paths.TaskDirectory);

        var output = _paths.OutputPath;
        var ffmpeg = FindFfmpeg();
        var wantsMp4 = Path.GetExtension(output).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        var canRemux = _options.AutoRemux && wantsMp4 && ffmpeg is not null;
        var recordingPath = canRemux
            ? output + ".recording.ts"
            : wantsMp4 ? Path.ChangeExtension(output, ".ts")! : output;
        _liveRecordingPath = recordingPath;

        var stateFile = _paths.LiveStatePath;
        var initDoneFile = Path.Combine(_tempDir, "init.done");
        long lastSequence = -1;
        var recordedCount = 0;
        if (File.Exists(stateFile))
        {
            var parts = (await File.ReadAllTextAsync(stateFile, ct).ConfigureAwait(false)).Split(':');
            if (parts.Length == 2 && long.TryParse(parts[0], out var savedSeq) &&
                int.TryParse(parts[1], out var savedCount))
            {
                lastSequence = savedSeq;
                recordedCount = savedCount;
                Volatile.Write(ref _completed, recordedCount);
                SetMessage($"继续录制直播，已保存 {recordedCount} 段…");
            }
        }

        var playlist = firstPlaylist;
        var wave = 0;
        var streamEnded = false;

        while (!ct.IsCancellationRequested && Volatile.Read(ref _finishRequested) == 0)
        {
            if (wave > 0)
            {
                var text = await FetchTextAsync(mediaUrl, ct).ConfigureAwait(false);
                playlist = M3u8Parser.Parse(mediaUrl, text);
            }
            wave++;

            // fMP4 直播：初始化段只写一次
            if (playlist.InitUri is not null && !File.Exists(initDoneFile))
            {
                var initBytes = await LoadInitAsync(playlist.InitUri, ct).ConfigureAwait(false);
                await using (var stream = new FileStream(recordingPath,
                                 File.Exists(recordingPath) ? FileMode.Append : FileMode.Create,
                                 FileAccess.Write, FileShare.Read, 1 << 20, useAsync: true))
                    await stream.WriteAsync(initBytes, ct).ConfigureAwait(false);
                await File.WriteAllTextAsync(initDoneFile, "1", ct).ConfigureAwait(false);
            }

            var pending = playlist.Segments
                .Where(s => s.SequenceNumber > lastSequence)
                .OrderBy(s => s.SequenceNumber)
                .ToArray();

            if (pending.Length > 0)
            {
                var keys = await FetchKeysAsync(pending, ct).ConfigureAwait(false);
                using var semaphore = new SemaphoreSlim(Math.Max(1, _options.Concurrency));
                await Task.WhenAll(pending.Select(s => Task.Run(async () =>
                {
                    await semaphore.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        await EnsureSegmentFileAsync(LiveSegmentPath(s.SequenceNumber), s, keys, ct)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                    finally { semaphore.Release(); }
                }, ct))).ConfigureAwait(false);

                await using (var target = new FileStream(recordingPath, FileMode.Append,
                                 FileAccess.Write, FileShare.Read, 1 << 20, useAsync: true))
                {
                    foreach (var segment in pending)
                    {
                        var file = LiveSegmentPath(segment.SequenceNumber);
                        if (!File.Exists(file)) break; // 保持顺序，缺片留到下一轮
                        await using (var source = new FileStream(file, FileMode.Open, FileAccess.Read,
                                         FileShare.Read, 1 << 20, useAsync: true))
                        {
                            await source.CopyToAsync(target, ct).ConfigureAwait(false);
                        }
                        lastSequence = segment.SequenceNumber;
                        recordedCount++;
                        Volatile.Write(ref _completed, recordedCount);
                        TryDelete(file); // 删除已写入的分片，控制磁盘占用
                    }
                }

                await File.WriteAllTextAsync(stateFile,
                    FormattableString.Invariant($"{lastSequence}:{recordedCount}"), ct).ConfigureAwait(false);
                SetMessage($"直播录制中 · 已保存 {recordedCount} 段 · 点击「停止并保存」结束");
            }

            if (!playlist.IsLive)
            {
                streamEnded = true;
                break;
            }
            if (Volatile.Read(ref _finishRequested) != 0) break;

            await LiveWaitAsync(Math.Clamp(playlist.TargetDuration / 2.0, 0.5, 5.0), ct)
                .ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();

        SetState(DownloadState.Merging, "正在结束录制并保存文件…");
        if (canRemux && await RemuxAsync(ffmpeg!, recordingPath, output, ct).ConfigureAwait(false))
        {
            TryDelete(recordingPath);
            OutputPath = output;
        }
        else
        {
            OutputPath = recordingPath;
        }

        Volatile.Write(ref _displayFileName, Path.GetFileName(OutputPath));
        SetState(DownloadState.Completed,
            (streamEnded ? "直播已结束，录制完成：" : "录制已停止，文件已保存：") + OutputPath);
        _paths.DeleteCache();
    }

    private async Task LiveWaitAsync(double seconds, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _finishRequested) != 0) return;
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
    }

    private string LiveSegmentPath(long sequence) =>
        Path.Combine(_tempDir, $"live_{sequence:D12}.seg");

    // ---------------- 分片下载 / 解密 / 净化 ----------------

    private async Task<ConcurrentDictionary<string, byte[]>> FetchKeysAsync(
        IEnumerable<Segment> segments, CancellationToken ct)
    {
        var keys = new ConcurrentDictionary<string, byte[]>();
        var list = segments as IReadOnlyCollection<Segment> ?? segments.ToArray();

        var drm = list.FirstOrDefault(s => s.Key is { IsUnsupportedDrm: true });
        if (drm is not null)
            SetMessage($"注意：{drm.Key!.Method} 属于 DRM，无法本地解密，将保留原始分片");

        var keyUris = list
            .Where(s => s.Key is { IsAesCbc: true, KeyUri: not null })
            .Select(s => (s.Key!.KeyUri!, s.Key!))
            .DistinctBy(pair => pair.Item1.ToString())
            .ToArray();

        foreach (var (keyUri, info) in keyUris)
        {
            SetMessage("正在获取解密密钥…");
            var raw = await FetchKeyBytesAsync(keyUri, ct).ConfigureAwait(false);
            var normalized = SegmentCodec.NormalizeKey(raw, info);
            keys[keyUri.ToString()] = normalized;
            SetMessage($"已获取密钥：AES-{normalized.Length * 8}-CBC");
        }

        return keys;
    }

    /// <summary>下载 + 解密 + 伪装格式还原，成功后原子改名写入 dest；返回是否成功。</summary>
    private async Task<bool> EnsureSegmentFileAsync(
        string dest, Segment segment, ConcurrentDictionary<string, byte[]> keys, CancellationToken ct)
    {
        if (File.Exists(dest) && new FileInfo(dest).Length > 0) return true;

        var part = dest + ".part";
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3 && !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await DownloadToFileAsync(segment.Uri, segment.ByteRange, part, ct).ConfigureAwait(false);
                var raw = await File.ReadAllBytesAsync(part, ct).ConfigureAwait(false);

                byte[] output;
                if (segment.Key is { IsAesCbc: true } key)
                {
                    var keyUri = key.KeyUri ?? throw new InvalidOperationException("EXT-X-KEY 缺少密钥地址");
                    if (!keys.TryGetValue(keyUri.ToString(), out var keyBytes))
                        throw new InvalidOperationException("未能获取解密密钥：" + keyUri);
                    var iv = SegmentCodec.NormalizeIv(key.Iv, segment.SequenceNumber);
                    var (cipher, _) = SegmentCodec.SanitizeCipher(raw);
                    var plain = SegmentCodec.Decrypt(keyBytes, iv, cipher);
                    var (clean, _) = SegmentCodec.Sanitize(plain);
                    output = clean;
                }
                else
                {
                    var (clean, _) = SegmentCodec.Sanitize(raw);
                    output = clean;
                }

                await File.WriteAllBytesAsync(part, output, ct).ConfigureAwait(false);
                File.Move(part, dest, true);
                return true;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < 3) await Task.Delay(500 * attempt, ct).ConfigureAwait(false);
            }
        }

        TryDelete(part);
        if (lastError is not null && !ct.IsCancellationRequested)
            SetMessage($"分片 {segment.SequenceNumber} 下载失败：{lastError.Message}");
        return false;
    }

    private async Task<byte[]> LoadInitAsync(Uri initUri, CancellationToken ct)
    {
        var file = Path.Combine(_tempDir, "init.seg");
        if (!File.Exists(file))
        {
            var part = file + ".part";
            await DownloadToFileAsync(initUri, null, part, ct).ConfigureAwait(false);
            var (clean, _) = SegmentCodec.Sanitize(await File.ReadAllBytesAsync(part, ct));
            await File.WriteAllBytesAsync(file, clean, ct).ConfigureAwait(false);
            TryDelete(part);
        }
        return await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
    }

    private async Task DownloadToFileAsync(
        Uri uri, (long Length, long Offset)? range, string destination, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (range is { } r)
            request.Headers.TryAddWithoutValidation("Range", $"bytes={r.Offset}-{r.Offset + r.Length - 1}");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = new FileStream(destination, FileMode.Create, FileAccess.Write,
            FileShare.None, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            Interlocked.Add(ref _bytes, read);
        }
    }

    private async Task<string> FetchTextAsync(Uri uri, CancellationToken ct)
    {
        var bytes = await FetchBytesAsync(uri, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
    }

    private async Task<byte[]> FetchBytesAsync(Uri uri, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    private async Task<byte[]> FetchKeyBytesAsync(Uri keyUri, CancellationToken ct)
    {
        // 支持内联密钥：URI="data:application/octet-stream;base64,...."
        if (keyUri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
        {
            var value = Uri.UnescapeDataString(keyUri.ToString());
            var comma = value.IndexOf(',');
            if (comma < 0) throw new InvalidOperationException("内联密钥格式无效");
            var body = value[(comma + 1)..];
            return value[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(body)
                : Encoding.UTF8.GetBytes(body);
        }
        return await FetchBytesAsync(keyUri, ct).ConfigureAwait(false);
    }

    // ---------------- 合并 / 转封装 ----------------

    private async Task MergeAsync(Playlist playlist, string fingerprint, CancellationToken ct)
    {
        Directory.CreateDirectory(_paths.TaskDirectory);
        SetMessage("分片已实时写入，正在收尾…");

        // 补齐可能因文件被短暂占用而未写入的尾部分片；这些分片不会在写入成功前被删除
        await AppendReadySegmentsAsync(playlist, fingerprint, ct).ConfigureAwait(false);
        if (_appended < playlist.Segments.Count || !File.Exists(_visiblePath))
        {
            await using (var target = new FileStream(_visiblePath, FileMode.Append,
                             FileAccess.Write, FileShare.Read, 1 << 20, useAsync: true))
            {
                // fMP4 必须先写入初始化段（ISO BMFF 的 ftyp/moov）
                if (!_initAppended && _appended == 0 && playlist.InitUri is not null)
                {
                    var initBytes = await LoadInitAsync(playlist.InitUri, ct).ConfigureAwait(false);
                    await target.WriteAsync(initBytes, ct).ConfigureAwait(false);
                    _initAppended = true;
                    _bytesWritten += initBytes.Length;
                }

                for (var i = _appended; i < playlist.Segments.Count; i++)
                {
                    var segmentFile = SegmentPath(i);
                    if (!File.Exists(segmentFile))
                        throw new IOException($"分片文件缺失（第 {i + 1} 段），点击「继续」可重试");
                    var length = new FileInfo(segmentFile).Length;
                    await using var source = new FileStream(segmentFile, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 1 << 20, useAsync: true);
                    await source.CopyToAsync(target, ct).ConfigureAwait(false);
                    _bytesWritten += length;
                    _appended++;
                }
            }
            WriteVodState(fingerprint);
        }

        var output = _paths.OutputPath;
        var ffmpeg = FindFfmpeg();
        var wantsMp4 = Path.GetExtension(output).Equals(".mp4", StringComparison.OrdinalIgnoreCase);

        if (_options.AutoRemux && wantsMp4 && ffmpeg is not null)
        {
            SetMessage("正在用 ffmpeg 无损转封装为 MP4…");
            if (await RemuxAsync(ffmpeg, _visiblePath, output, ct).ConfigureAwait(false))
            {
                TryDelete(_visiblePath);
            }
            else
            {
                var ts = Path.ChangeExtension(output, ".ts")!;
                if (!PathsEqual(_visiblePath, ts)) File.Move(_visiblePath, ts, true);
                output = ts;
                SetMessage("ffmpeg 转换失败，已保存为 TS 格式");
            }
        }
        else
        {
            if (_options.AutoRemux && wantsMp4) SetMessage("未检测到 ffmpeg，已保存为 TS 格式");
            var finalPath = wantsMp4 ? Path.ChangeExtension(output, ".ts")! : output;
            if (!PathsEqual(_visiblePath, finalPath)) File.Move(_visiblePath, finalPath, true);
            output = finalPath;
        }

        OutputPath = output;
        Volatile.Write(ref _displayFileName, Path.GetFileName(output));
        _paths.DeleteCache();
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static async Task<bool> RemuxAsync(string ffmpeg, string input, string output, CancellationToken ct)
    {
        var info = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "-y", "-loglevel", "error", "-i", input, "-c", "copy", "-movflags", "+faststart", output })
            info.ArgumentList.Add(arg);

        using var process = Process.Start(info);
        if (process is null) return false;
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0) Debug.WriteLine("[ffmpeg] " + error);
        return process.ExitCode == 0;
    }

    private static string? FindFfmpeg()
    {
        var executable = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
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

    private static bool IsPlaylistText(string text) =>
        !string.IsNullOrEmpty(text) && text.Contains("#EXTM3U", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 把浏览器实际发出的请求头复用到下载用的 HttpClient。
    /// 只保留与鉴权 / 防盗链相关的头，避免复用 Host、Content-Length 等逐请求字段。
    /// </summary>
    private void ApplyCapturedHeaders(IReadOnlyDictionary<string, string> headers)
    {
        foreach (var (key, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(value) || key.StartsWith(':')) continue;
            var keep =
                key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Referer", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Origin", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Token", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Accept", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Accept-Language", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("X-", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("Sec-", StringComparison.OrdinalIgnoreCase);
            if (!keep) continue;
            try
            {
                _http.DefaultRequestHeaders.Remove(key);
                _http.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
            }
            catch { /* 非法头名忽略 */ }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
        _http.Dispose();
        _cts?.Dispose();
    }
}
