using System.Globalization;
using System.Text.RegularExpressions;

namespace m3u8downloader.Models;

public sealed record EncryptionInfo(string Method, Uri? KeyUri, byte[]? Iv)
{
    private static readonly Regex KeySizeRegex = new(@"(128|192|256)", RegexOptions.Compiled);

    /// <summary>是否为可本地解密的 AES-CBC 系列（AES-128 / AES-192 / AES-256）。</summary>
    public bool IsAesCbc
    {
        get
        {
            var method = Method.ToUpperInvariant();
            return method.StartsWith("AES", StringComparison.Ordinal)
                   && !method.Contains("SAMPLE")   // SAMPLE-AES 属于 DRM，不做处理
                   && !method.Contains("CTR")      // 非 CBC 模式
                   && !method.Contains("GCM");
        }
    }

    /// <summary>_METHOD 中显式声明的密钥位数（AES-256-CBC → 256），未声明则为 null。</summary>
    public int? DeclaredKeySize
    {
        get
        {
            var match = KeySizeRegex.Match(Method);
            return match.Success && int.TryParse(match.Value, out var bits) ? bits : null;
        }
    }

    public bool IsUnsupportedDrm => Method.Contains("SAMPLE", StringComparison.OrdinalIgnoreCase)
                                    || Method.Contains("DRM", StringComparison.OrdinalIgnoreCase);
}

public sealed record Segment(
    Uri Uri,
    double Duration,
    EncryptionInfo? Key,
    (long Length, long Offset)? ByteRange,
    long SequenceNumber);

public sealed record VariantStream(Uri Uri, int Bandwidth, string Resolution, string? Name);

public sealed class Playlist
{
    public bool IsMaster { get; init; }
    /// <summary>媒体清单中没有 #EXT-X-ENDLIST 即视为直播流。</summary>
    public bool IsLive { get; init; }
    public double TargetDuration { get; set; } = 2;
    public List<Segment> Segments { get; } = [];
    public List<VariantStream> Variants { get; } = [];
    public long MediaSequence { get; set; }
    public Uri? InitUri { get; set; }
}

/// <summary>
/// HLS 播放列表解析器：支持主/媒体（多码率）清单、嵌套地址与相对路径、
/// EXT-X-KEY 加密标签、EXT-X-MAP（fMP4 初始化段）、EXT-X-BYTERANGE、
/// EXT-X-MEDIA-SEQUENCE、直播流识别。
/// </summary>
public static class M3u8Parser
{
    private static readonly Regex AttributeRegex = new(
        @"([A-Za-z0-9_-]+)\s*=\s*(?:""([^""]*)""|([^,]*))",
        RegexOptions.Compiled);

    public static Playlist Parse(Uri baseUrl, string content)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');
        var isMaster = lines.Any(l =>
            l.TrimStart().StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase));
        var playlist = new Playlist
        {
            IsMaster = isMaster,
            IsLive = !isMaster && !content.Contains("#EXT-X-ENDLIST", StringComparison.OrdinalIgnoreCase)
        };

        EncryptionInfo? currentKey = null;
        double duration = 0;
        (long, long)? pendingRange = null;
        long rangeEnd = 0;
        Dictionary<string, string>? pendingVariant = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(line[22..].Trim(), out var seq)) playlist.MediaSequence = seq;
            }
            else if (line.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(line[22..].Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var target) && target > 0)
                    playlist.TargetDuration = target;
            }
            else if (line.StartsWith("#EXT-X-KEY:", StringComparison.OrdinalIgnoreCase))
            {
                var attrs = ParseAttributes(line[11..]);
                var method = attrs.GetValueOrDefault("METHOD", "NONE").Trim().Trim('"').ToUpperInvariant();
                if (method is "NONE" or "")
                {
                    currentKey = null;
                }
                else
                {
                    Uri? keyUri = attrs.TryGetValue("URI", out var u) && !string.IsNullOrWhiteSpace(u)
                        ? new Uri(baseUrl, u)
                        : null;
                    byte[]? iv = null;
                    if (attrs.TryGetValue("IV", out var ivText))
                    {
                        try { iv = ParseHex(ivText); } catch { /* 非法 IV 时回退到序号推导 */ }
                    }
                    currentKey = new EncryptionInfo(method, keyUri, iv);
                }
            }
            else if (line.StartsWith("#EXT-X-MAP:", StringComparison.OrdinalIgnoreCase))
            {
                var attrs = ParseAttributes(line[11..]);
                if (attrs.TryGetValue("URI", out var u)) playlist.InitUri = new Uri(baseUrl, u);
            }
            else if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                var value = line[8..];
                var comma = value.IndexOf(',');
                var number = comma >= 0 ? value[..comma] : value;
                if (double.TryParse(number.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    duration = d;
            }
            else if (line.StartsWith("#EXT-X-BYTERANGE:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line[17..].Split('@');
                long.TryParse(parts[0], out var length);
                var offset = parts.Length > 1 && long.TryParse(parts[1], out var o) ? o : rangeEnd;
                pendingRange = (length, offset);
                rangeEnd = offset + length;
            }
            else if (line.StartsWith("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase))
            {
                var colon = line.IndexOf(':');
                pendingVariant = colon >= 0 ? ParseAttributes(line[(colon + 1)..]) : [];
            }
            else if (line.StartsWith('#'))
            {
                // 其余标签（DISCONTINUITY、PROGRAM-DATE-TIME 等）不影响下载流程
            }
            else if (isMaster && pendingVariant is not null)
            {
                int.TryParse(pendingVariant.GetValueOrDefault("BANDWIDTH", "0"), out var bandwidth);
                playlist.Variants.Add(new VariantStream(
                    new Uri(baseUrl, line),
                    bandwidth,
                    pendingVariant.GetValueOrDefault("RESOLUTION", ""),
                    pendingVariant.GetValueOrDefault("NAME")));
                pendingVariant = null;
            }
            else
            {
                // 忽略图片等伪装后缀：只要是清单里列出的 URI 行就当作分片处理
                playlist.Segments.Add(new Segment(
                    new Uri(baseUrl, line),
                    duration,
                    currentKey,
                    pendingRange,
                    playlist.MediaSequence + playlist.Segments.Count));
                duration = 0;
                pendingRange = null;
            }
        }

        return playlist;
    }

    private static Dictionary<string, string> ParseAttributes(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeRegex.Matches(text))
        {
            var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value.Trim();
            result[match.Groups[1].Value] = value;
        }
        return result;
    }

    private static byte[] ParseHex(string hex)
    {
        var h = hex.Trim();
        if (h.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) h = h[2..];
        if (h.Length % 2 == 1) h = "0" + h;
        return Convert.FromHexString(h);
    }
}
