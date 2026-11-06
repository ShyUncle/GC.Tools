namespace m3u8downloader.Services;

/// <summary>代理配置。Address 使用 http:// 或 socks5:// 方案，.NET 6+ 原生支持 SOCKS5。</summary>
public sealed class ProxyOptions
{
    public required Uri Address { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
}
