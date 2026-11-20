using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using DynamicWallpaper.Config;

namespace DynamicWallpaper.Library;

/// <summary>
/// 远程缩略图缓存：按 URL 哈希存盘（AppData\DynamicWallpaper\ThumbCache），
/// 在线壁纸卡片直接展示 Pexels CDN 小图，无需下载完整文件。
/// </summary>
public static class RemoteThumbCache
{
    private static readonly HttpClient Http = CreateClient();

    public static string Folder { get; } =
        Path.Combine(ConfigService.GetAppDataFolder(), "ThumbCache");

    /// <summary>缩略图已在磁盘缓存中时返回本地路径，否则返回 null（异步下载用 EnsureAsync）。</summary>
    public static string? TryGet(string url)
    {
        var file = GetPath(url);
        return File.Exists(file) ? file : null;
    }

    /// <summary>确保缩略图已下载并返回本地路径；失败返回 null（卡片显示占位样式）。</summary>
    public static async Task<string?> EnsureAsync(string url)
    {
        try
        {
            var file = GetPath(url);
            if (File.Exists(file))
                return file;

            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = File.Create(file);
            await response.Content.CopyToAsync(stream);
            return file;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>清空缩略图缓存目录（随「清理缓存」一起执行），返回（文件数，字节数）。</summary>
    public static (int Count, long Bytes) ClearAll()
    {
        if (!Directory.Exists(Folder))
            return (0, 0);

        long bytes = 0;
        var count = 0;

        foreach (var file in Directory.GetFiles(Folder))
        {
            try
            {
                bytes += new FileInfo(file).Length;
                File.Delete(file);
                count++;
            }
            catch
            {
                // 单个文件失败时跳过
            }
        }

        return (count, bytes);
    }

    private static string GetPath(string url)
    {
        Directory.CreateDirectory(Folder);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        return Path.Combine(Folder, hash + ".jpg");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DynamicWallpaper/1.0");
        return client;
    }
}
