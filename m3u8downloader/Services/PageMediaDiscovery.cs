using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace m3u8downloader.Services;

/// <summary>从网页中发现的播放列表候选。</summary>
public sealed record MediaCandidate(string Url, string Source)
{
    public string? MimeType { get; set; }
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Score => MediaUrlRules.Score(Url, MimeType);
    public DateTime CapturedAt { get; init; } = DateTime.Now;
}

/// <summary>媒体 URL 的判定与打分，静态提取和浏览器抓取共用同一套规则。</summary>
public static class MediaUrlRules
{
    private static readonly Regex AdvertRegex = new(
        @"(?:^|[/\-_.?&=])(?:ad|ads|advert|advertise|banner|preroll|postroll|midroll|logo|tracker|beacon|pixel|analytics|stat)(?:[/\-_.?&=]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PositiveRegex = new(
        @"(?:m3u8|master|playlist|hls|vod|video|play|live|stream)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>是否是可作为下载入口的播放列表地址（含无扩展名但响应类型为 mpegurl 的情况）。</summary>
    public static bool IsPlaylist(string url, string? mimeType = null, string? resourceType = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        // 浏览器内部地址、data URI、扩展页不处理
        if (url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase)) return false;

        if (!string.IsNullOrEmpty(mimeType) &&
            (mimeType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) ||
             mimeType.Contains("dash+xml", StringComparison.OrdinalIgnoreCase) ||
             mimeType.Contains("x-mpeg", StringComparison.OrdinalIgnoreCase)))
            return true;

        // 去掉查询串后看路径；有的站点把 .m3u8 藏在参数里
        string path;
        try { path = new Uri(url).AbsolutePath; }
        catch { return false; }

        if (path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(".m3u8/", StringComparison.OrdinalIgnoreCase))
            return true;

        // 形如 /hls/xxx?format=m3u8 或 ?type=m3u8
        if (url.Contains("m3u8", StringComparison.OrdinalIgnoreCase) &&
            (resourceType is "XHR" or "Fetch" or "Media" || url.Contains("/hls/", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    public static int Score(string url, string? mimeType = null)
    {
        var score = 100;
        var lower = url.ToLowerInvariant();
        if (lower.Contains(".m3u8")) score += 40;
        if (lower.Contains("master") || lower.Contains("playlist") || lower.Contains("index")) score += 12;
        if (PositiveRegex.IsMatch(lower)) score += 8;
        if (lower.Contains("token=") || lower.Contains("auth") || lower.Contains("sign=")) score += 8;
        if (lower.Contains("/hls/") || lower.Contains("/vod/")) score += 6;
        if (!string.IsNullOrEmpty(mimeType) && mimeType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase))
            score += 15;
        if (lower.EndsWith(".mpd")) score -= 30; // 当前引擎仅支持 HLS
        if (AdvertRegex.IsMatch(lower)) score -= 120;
        if (lower.Contains("preview") || lower.Contains("trailer")) score -= 25;
        return score;
    }
}

/// <summary>
/// 增强的静态页面解析：处理 JS 转义 / HTML 实体 / URL 编码 / 协议相对地址 / base64，
/// 并扫描页面引用的外部脚本。对真正由 JS 动态生成的地址仍需 BrowserSniffer 渲染抓取。
/// </summary>
public static class PageMediaDiscovery
{
    private static readonly Regex ScriptSrcRegex = new(
        @"<script\b[^>]*?\bsrc\s*=\s*(?:""(?<u>[^""]+)""|'(?<u>[^']+)'|(?<u>[^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 绝对地址 / 协议相对地址，允许末尾带 query
    private static readonly Regex AbsoluteRegex = new(
        @"(?:https?:)?//[^\s""'<>\\`\]\)}]*?\.m3u8[^\s""'<>\\`\]\)}]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 引号包裹的任意形式（含相对路径、拼接残留）
    private static readonly Regex QuotedRegex = new(
        @"[""'`](?<u>[^""'`\s]{1,2000}?\.m3u8[^""'`\s]*)[""'`]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 无引号的相对路径
    private static readonly Regex BareRegex = new(
        @"(?<![A-Za-z0-9_/.\-])(?<u>(?:\./|\.\./|/)?[\w.\-]+(?:/[\w.\-]+)*\.m3u8(?:[?&#][^\s""'<>\\]*)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 整体被 URL 编码的地址
    private static readonly Regex EncodedRegex = new(
        @"https?%3a%2f%2f[^\s""'<>\\]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex UnicodeEscapeRegex = new(
        @"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);

    private static readonly Regex Base64Regex = new(
        @"(?<![A-Za-z0-9+/])(?<b>[A-Za-z0-9+/]{24,}={0,2})(?![A-Za-z0-9+/])",
        RegexOptions.Compiled);

    private const int MaxScripts = 12;
    private const int MaxScriptBytes = 3 * 1024 * 1024;

    /// <summary>把常见的转义还原成可直接解析的文本。</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        var s = text.Replace("\\/", "/").Replace("\\\\/", "/");
        s = UnicodeEscapeRegex.Replace(s, m =>
            ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
        try { s = WebUtility.HtmlDecode(s); } catch { /* 忽略非法实体 */ }
        return s;
    }

    /// <summary>从一段文本中提取候选，baseUrl 用于补全相对地址。</summary>
    public static IEnumerable<MediaCandidate> Extract(Uri baseUrl, string text, string source = "页面")
    {
        var normalized = Normalize(text);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<MediaCandidate>();
        void Collect(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return;
            var value = raw.Trim().TrimEnd(',', ';', ')', ']', '}');
            var quote = value.IndexOfAny(['"', '\'', '`']);
            if (quote > 0) value = value[..quote];
            if (value.Length < 5) return;
            if (value.StartsWith("//", StringComparison.Ordinal)) value = baseUrl.Scheme + ":" + value;
            if (!Uri.TryCreate(baseUrl, value, out var absolute)) return;
            if (absolute.Scheme is not ("http" or "https")) return;
            var url = absolute.ToString();
            if (!MediaUrlRules.IsPlaylist(url)) return;
            if (seen.Add(url)) results.Add(new MediaCandidate(url, source));
        }

        foreach (Match m in AbsoluteRegex.Matches(normalized)) Collect(m.Value);
        foreach (Match m in QuotedRegex.Matches(normalized)) Collect(m.Groups["u"].Value);
        foreach (Match m in BareRegex.Matches(normalized)) Collect(m.Groups["u"].Value);

        foreach (Match m in EncodedRegex.Matches(normalized))
        {
            try { Collect(Uri.UnescapeDataString(m.Value)); } catch { /* 忽略非法编码 */ }
        }

        // 部分站点用 base64 包住播放地址（atob / data 配置）
        var checkedBase64 = 0;
        foreach (Match m in Base64Regex.Matches(normalized))
        {
            if (++checkedBase64 > 200) break;
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups["b"].Value));
                if (!decoded.Contains("m3u8", StringComparison.OrdinalIgnoreCase)) continue;
                var inner = Normalize(decoded);
                foreach (Match hit in AbsoluteRegex.Matches(inner)) Collect(hit.Value);
                foreach (Match hit in QuotedRegex.Matches(inner)) Collect(hit.Groups["u"].Value);
            }
            catch { /* 不是有效 base64 文本 */ }
        }

        return results.OrderByDescending(c => c.Score).ThenBy(c => c.Url.Length).ToList();
    }

    /// <summary>页面中的外部脚本地址（限制数量，避免抓一堆统计 SDK）。</summary>
    public static IReadOnlyList<Uri> FindScripts(Uri baseUrl, string html)
    {
        var list = new List<Uri>();
        foreach (Match m in ScriptSrcRegex.Matches(html))
        {
            var value = Normalize(m.Groups["u"].Value).Trim();
            if (value.Length == 0 || value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            if (Uri.TryCreate(baseUrl, value, out var script) &&
                script.Scheme is "http" or "https" &&
                !list.Any(u => u.ToString() == script.ToString()))
                list.Add(script);
            if (list.Count >= MaxScripts) break;
        }
        return list;
    }

    /// <summary>
    /// 完整静态解析：页面 HTML + 外部 JS。外部 JS 抓取失败不影响主结果。
    /// </summary>
    public static async Task<List<MediaCandidate>> ExtractFromPageAsync(
        HttpClient http, Uri pageUrl, string html, CancellationToken ct)
    {
        var candidates = new List<MediaCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Merge(IEnumerable<MediaCandidate> items)
        {
            foreach (var item in items)
                if (seen.Add(item.Url)) candidates.Add(item);
        }

        Merge(Extract(pageUrl, html, "页面 HTML"));

        var scripts = FindScripts(pageUrl, html);
        foreach (var script in scripts)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                using var response = await http.GetAsync(script, HttpCompletionOption.ResponseHeadersRead, ct)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                var length = response.Content.Headers.ContentLength;
                if (length > MaxScriptBytes) continue;
                var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (text.Length > MaxScriptBytes) continue;
                Merge(Extract(script, text, "外部 JS · " + Path.GetFileName(script.AbsolutePath)));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch { /* 单个脚本失败不影响整体 */ }
        }

        return candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Url.Length).ToList();
    }
}
