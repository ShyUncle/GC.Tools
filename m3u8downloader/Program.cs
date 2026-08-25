
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace M3U8Downloader
{
    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient(new HttpClientHandler()
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        });

        static async Task Main(string[] args)
        {
             
                Console.WriteLine("请输入url");

            string m3u8Url = "https://play.qiqiuyun.net/sdk_api/video/hls_stream/shd.m3u8?resNo=9f42018eca004c018dcfff2d2024ca65&token=eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJsZXZlbCI6InNoZCIsInByZXZpZXciOm51bGwsInBsYXlBdWRpbyI6MCwiaGVhZCI6bnVsbCwic2tpcCI6bnVsbCwibm8iOiI5ZjQyMDE4ZWNhMDA0YzAxOGRjZmZmMmQyMDI0Y2E2NSIsImp0aSI6ImFkODU5Y2Y0LTNhYzItNDIiLCJ0aW1lcyI6MSwiZXhwIjoxNzg3NjYxNzczLCJlbmNyeXB0IjoyLCJuYXRpdmUiOjAsInVpZCI6IjU0IiwidW5hbWUiOiJcdThjNmJcdTY2NGJcdTk2NDhcdTg1Y2YiLCJwaWQiOiI2WU1Hb2VLNEs4YTdNZzN0IiwiY2hhbm5lbFR5cGUiOiIiLCJjaGFubmVsIjoiIiwiaGxzQ2xlZkVuY3J5cHRWZXJzaW9uIjo5fQ.XDEWEacLBuCG2nZe2mytxRv4DuW0IuDjByjAXYYnGJU&ssl=1";// Console.ReadLine();
            string outputFileName =  "output.mp4";
            string referer =  null;

            if (!string.IsNullOrEmpty(referer))
            {
                httpClient.DefaultRequestHeaders.Referrer = new Uri(referer);
                httpClient.DefaultRequestHeaders.Add("Referer", "https://service-cdn.qiqiuyun.net/");
            }
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("token", "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.eyJwcmV2aWV3IjpudWxsLCJwbGF5QXVkaW8iOm51bGwsImhlYWQiOm51bGwsInNraXAiOm51bGwsIm5vIjoiOWY0MjAxOGVjYTAwNGMwMThkY2ZmZjJkMjAyNGNhNjUiLCJqdGkiOiJjNjdlYjk4ZC0xM2QwLTQ3IiwidGltZXMiOjEsImV4cCI6MTc4NzY0MDc3MSwiZW5jcnlwdCI6MiwibmF0aXZlIjowLCJ1aWQiOiI1NCIsInVuYW1lIjoiXHU4YzZiXHU2NjRiXHU5NjQ4XHU4NWNmIiwicGlkIjoiNllNR29lSzRLOGE3TWczdCIsImNoYW5uZWxUeXBlIjoiIiwiY2hhbm5lbCI6IiIsImhsc0NsZWZFbmNyeXB0VmVyc2lvbiI6OX0.KtL7o32nockOw-B0g5gCz3gSvyDObxvQT1weLK3CtDY");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("lang", "zh-CN");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("ssl", "1");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("resNo", "9f42018eca004c018dcfff2d2024ca65");
            try
            {
                Console.WriteLine($"[1/4] 正在解析 M3U8: {m3u8Url}");
                string m3u8Content = await httpClient.GetStringAsync(m3u8Url);

                // 处理 Master Playlist
                if (m3u8Content.Contains("#EXT-X-STREAM-INF"))
                {
                    Console.WriteLine("检测到多清晰度列表，自动选择最高带宽...");
                    m3u8Url = GetBestStreamUrl(m3u8Content, m3u8Url);
                    if (string.IsNullOrEmpty(m3u8Url)) throw new Exception("无法解析最佳流地址");
                    m3u8Content = await httpClient.GetStringAsync(m3u8Url);
                }

                var parseResult = ParseM3U8(m3u8Content, m3u8Url);
                List<string> tsUrls = parseResult.TsUrls;
                EncryptionInfo? encInfo = parseResult.EncryptionInfo;

                if (tsUrls.Count == 0) throw new Exception("未找到视频片段");

                Console.WriteLine($"[2/4] 找到 {tsUrls.Count} 个片段。");

                byte[]? keyBytes = null;
                if (encInfo.HasValue && !string.IsNullOrEmpty(encInfo.Value.KeyUri))
                {
                    Console.WriteLine("正在下载解密密钥...");
                    string keyUrl = ResolveUrl(encInfo.Value.KeyUri, m3u8Url);
                    keyBytes = await httpClient.GetByteArrayAsync(keyUrl);
                }

                Console.WriteLine("[3/4] 开始并发下载...");
                byte[][] tsSegments = new byte[tsUrls.Count][];
                var semaphore = new SemaphoreSlim(8); // 限制并发数

                var tasks = tsUrls.Select((url, index) => DownloadSegment(url, index, tsSegments, encInfo, keyBytes, semaphore)).ToList();
                await Task.WhenAll(tasks);

                Console.WriteLine("[4/4] 合并视频中...");
                MergeTsToMp4(tsSegments, outputFileName);
                Console.WriteLine($"完成！文件保存至: {Path.GetFullPath(outputFileName)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
            }
        }

        static string GetBestStreamUrl(string content, string baseUrl)
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string bestUrl = null;
            long maxBw = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("#EXT-X-STREAM-INF"))
                {
                    // 修复：使用 Groups.Value 获取第一个捕获组
                    var match = Regex.Match(lines[i], @"BANDWIDTH=(\d+)");
                    if (match.Success && long.TryParse(match.Groups[0].Value, out long bw))
                    {
                        if (bw > maxBw && i + 1 < lines.Length)
                        {
                            maxBw = bw;
                            bestUrl = ResolveUrl(lines[i + 1].Trim(), baseUrl);
                        }
                    }
                }
            }
            return bestUrl;
        }

        static (List<string> TsUrls, EncryptionInfo? EncryptionInfo) ParseM3U8(string content, string baseUrl)
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var tsUrls = new List<string>();
            EncryptionInfo? encInfo = null;

            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("#EXT-X-KEY"))
                {
                    // 修复：使用 Groups.Value 获取捕获组内容，避免 Groups.Value 错误
                    var methodMatch = Regex.Match(trimmed, @"METHOD=([^,]+)");
                    var uriMatch = Regex.Match(trimmed, @"URI=""([^""]+)""");
                    var ivMatch = Regex.Match(trimmed, @"IV=0x([0-9a-fA-F]+)");

                    if (methodMatch.Success && uriMatch.Success)
                    {
                        encInfo = new EncryptionInfo
                        {
                            Method = methodMatch.Groups[1].Value,
                            KeyUri = uriMatch.Groups[0].Value,
                            IV = ivMatch.Success ? ivMatch.Groups[0].Value : null
                        };
                    }
                }
                else if (!trimmed.StartsWith("#") && !string.IsNullOrEmpty(trimmed))
                {
                    tsUrls.Add(ResolveUrl(trimmed, baseUrl));
                }
            }
            return (tsUrls, encInfo);
        }

        static string ResolveUrl(string relativeOrAbsolute, string baseUrl)
        {
            if (relativeOrAbsolute.StartsWith("http")) return relativeOrAbsolute;
            try
            {
                return new Uri(new Uri(baseUrl), relativeOrAbsolute).ToString();
            }
            catch { return relativeOrAbsolute; }
        }

        static async Task DownloadSegment(string url, int index, byte[][] segments, EncryptionInfo? encInfo, byte[]? keyBytes, SemaphoreSlim semaphore)
        {
            await semaphore.WaitAsync();
            try
            {
                byte[] data = await httpClient.GetByteArrayAsync(url);

                if (encInfo.HasValue && encInfo.Value.Method == "AES-128" && keyBytes != null)
                {
                    data = DecryptAES128(data, keyBytes, encInfo.Value.IV, index);
                }
                segments[index] = data;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"片段 {index + 1} 失败: {ex.Message}");
                segments[index] = Array.Empty<byte>();
            }
            finally { semaphore.Release(); }
        }

        static byte[] DecryptAES128(byte[] encryptedData, byte[] key, string? ivHex, int segmentIndex)
        {
            byte[] iv = new byte[16];
            if (!string.IsNullOrEmpty(ivHex))
            {
                string hex = ivHex.Replace("0x", "").Replace("0X", "");
                byte[] parsedIv = Enumerable.Range(0, hex.Length)
                     .Where(x => x % 2 == 0)
                     .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                     .ToArray();
                Array.Copy(parsedIv, iv, Math.Min(parsedIv.Length, 16));
            }
            else
            {
                // HLS 标准：若未指定 IV，使用片段序号的大端序表示
                byte[] indexBytes = BitConverter.GetBytes(segmentIndex);
                if (BitConverter.IsLittleEndian) Array.Reverse(indexBytes);
                Array.Copy(indexBytes, 0, iv, 12, 4);
            }

            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var decryptor = aes.CreateDecryptor())
                {
                    try
                    {
                        return decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
                    }
                    catch
                    {
                        // 容错：尝试无填充模式
                        aes.Padding = PaddingMode.None;
                        using (var d2 = aes.CreateDecryptor())
                        {
                            return d2.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
                        }
                    }
                }
            }
        }

        static void MergeTsToMp4(byte[][] segments, string outputPath)
        {
            using (var fs = new FileStream(outputPath, FileMode.Create))
            {
                foreach (var seg in segments)
                {
                    if (seg != null && seg.Length > 0) fs.Write(seg, 0, seg.Length);
                }
            }
        }
    }

    struct EncryptionInfo
    {
        public string Method;
        public string KeyUri;
        public string? IV;
    }
}
