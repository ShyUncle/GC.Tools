using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DynamicWallpaper.Config;
using DynamicWallpaper.Models;

namespace DynamicWallpaper.Online;

/// <summary>
/// 在线壁纸来源：主用 Pexels API，按「分类 + 分页」获取、滚动加载。
/// - 每个「类型+分类」按顺序缓存最近 MaxCachedPages 页元数据（pages.json，跨启动有效），
///   已缓存的页直接复用不再请求；超出 10 页时淘汰最旧页。
/// - 缩略图使用 Pexels CDN 小图（ThumbnailUrl），列表浏览不下载完整文件；
///   完整文件只在设为壁纸/预览时按需下载（EnsureDownloadedAsync），
///   文件按 URL 哈希缓存在 OnlineCache，下载过的内容天然复用、不重复下载。
/// - 回退：Pexels 未配置 Key 或请求失败时，静态（图片）自动注入必应每日精选，
///   保证无 Key / 断网场景下静态壁纸页仍有内容；动态（视频）无必应回退来源。
/// </summary>
public class OnlineWallpaperService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly AppConfig _config;

    public sealed record PexelsCategory(string Name, string Query);

    /// <summary>浏览分类：精选 = curated 接口，其余为「分类名 → 搜索词」映射。</summary>
    public static readonly IReadOnlyList<PexelsCategory> Categories = new[]
    {
        new PexelsCategory("精选", ""),
        new PexelsCategory("自然风光", "nature landscape"),
        new PexelsCategory("城市建筑", "city architecture"),
        new PexelsCategory("野生动物", "wildlife animals"),
        new PexelsCategory("海洋沙滩", "ocean beach sea"),
        new PexelsCategory("山川森林", "mountains forest"),
        new PexelsCategory("科技", "technology"),
        new PexelsCategory("宇宙星空", "galaxy space stars"),
        new PexelsCategory("抽象艺术", "abstract art")
    };

    /// <summary>每页条目数（单次 API 请求返回数量，上限 80）。</summary>
    public const int PageSize = 30;

    /// <summary>每个「类型+分类」最多缓存的页数，超出后淘汰最旧页。</summary>
    public const int MaxCachedPages = 10;

    /// <summary>在线内容临时缓存目录（AppData\DynamicWallpaper\OnlineCache）。</summary>
    public string CacheFolder { get; } =
        Path.Combine(ConfigService.GetAppDataFolder(), "OnlineCache");

    /// <summary>已转正（复制到正式壁纸库）的记录：URL → 正式库文件路径，避免重复产生副本。</summary>
    private const string PromotedFileName = "promoted.json";
    private readonly Dictionary<string, string> _promotedUrls =
        new(StringComparer.Ordinal);

    /// <summary>分页元数据缓存文件（OnlineCache\pages.json）。</summary>
    private const string PagesFileName = "pages.json";

    private sealed class PageEntry
    {
        public string Key { get; set; } = string.Empty;
        public bool Exhausted { get; set; }
        public List<WallpaperItem> Items { get; set; } = new();
    }

    private sealed class PageManifest
    {
        public int PageSize { get; set; } = OnlineWallpaperService.PageSize;
        public List<PageEntry> Entries { get; set; } = new();
    }

    private readonly Dictionary<string, PageEntry> _pages = new(StringComparer.Ordinal);

    public OnlineWallpaperService(AppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        Directory.CreateDirectory(CacheFolder);
        LoadPromotedUrls();
        LoadPages();
    }

    // ---------- 分类与分页查询 ----------

    private static string PageKey(WallpaperType type, string category) => $"{type}|{category}";

    private PageEntry GetEntry(string key)
    {
        if (!_pages.TryGetValue(key, out var entry))
        {
            entry = new PageEntry { Key = key };
            _pages[key] = entry;
        }

        return entry;
    }

    /// <summary>某「类型+分类」已缓存的全部条目（按获取顺序，新的页在后面）。</summary>
    public IReadOnlyList<WallpaperItem> GetCachedItems(WallpaperType type, string category)
        => GetEntry(PageKey(type, category)).Items;

    /// <summary>全部缓存条目（合并进壁纸库用：收藏展示、轮换池）。</summary>
    public IEnumerable<WallpaperItem> GetAllCachedItems()
        => _pages.Values.SelectMany(e => e.Items);

    /// <summary>已缓存的页数。</summary>
    public int GetCachedPageCount(WallpaperType type, string category)
        => (int)Math.Ceiling(GetEntry(PageKey(type, category)).Items.Count / (double)PageSize);

    /// <summary>该分类是否已没有更多页（API 返回不足一整页时标记）。</summary>
    public bool IsExhausted(WallpaperType type, string category)
        => GetEntry(PageKey(type, category)).Exhausted;

    /// <summary>Pexels 配置变更后调用：清空全部分页缓存，下次从第 1 页重新获取。</summary>
    public void ResetPexels()
    {
        _pages.Clear();
        try
        {
            File.Delete(Path.Combine(CacheFolder, PagesFileName));
        }
        catch
        {
            // 删除失败不影响流程（内存缓存已清空）
        }
    }

    /// <summary>
    /// 获取指定页：命中缓存直接返回（不发网络请求）；未缓存时按顺序向 Pexels 请求一页。
    /// 失败抛出异常由调用方提示；成功返回该页条目（同时已并入缓存并持久化）。
    /// </summary>
    public async Task<IReadOnlyList<WallpaperItem>> FetchPageAsync(WallpaperType type, string category, int page)
    {
        var entry = GetEntry(PageKey(type, category));
        var cachedPages = GetCachedPageCount(type, category);

        // 已缓存的页直接复用（pages.json 跨启动有效）
        if (page <= cachedPages)
            return SlicePage(entry, page);

        // 只允许顺序取页：跳页或取已淘汰的页时返回空
        if (entry.Exhausted || page > cachedPages + 1)
            return Array.Empty<WallpaperItem>();

        List<WallpaperItem> items;
        try
        {
            items = await FetchPexelsPageAsync(type, category, cachedPages + 1).ConfigureAwait(false);
        }
        catch (Exception) when (type == WallpaperType.Image)
        {
            // Pexels 不可用（未配置 Key 或请求失败）：图片回退到必应每日精选，保证页面有内容
            var bing = await FetchBingImagesAsync(entry, category).ConfigureAwait(false);
            if (bing.Count == 0)
                throw; // 必应也不可用时才向上抛出提示失败

            entry.Items.AddRange(bing);
            entry.Exhausted = true; // 必应内容为一次性注入的有限集合，不再继续翻页
            SavePages();
            return bing;
        }

        if (items.Count == 0)
        {
            entry.Exhausted = true;
            SavePages();
            return Array.Empty<WallpaperItem>();
        }

        entry.Items.AddRange(items);
        if (items.Count < PageSize)
            entry.Exhausted = true;

        TrimPages(entry);
        SavePages();

        return items;
    }

    private static List<WallpaperItem> SlicePage(PageEntry entry, int page)
    {
        var start = (page - 1) * PageSize;
        var count = Math.Min(PageSize, entry.Items.Count - start);
        return count <= 0 ? new List<WallpaperItem>() : entry.Items.GetRange(start, count);
    }

    /// <summary>缓存超出 MaxCachedPages 页时从头部淘汰最旧页。</summary>
    private static void TrimPages(PageEntry entry)
    {
        var max = MaxCachedPages * PageSize;
        if (entry.Items.Count > max)
            entry.Items.RemoveRange(0, entry.Items.Count - max);
    }

    // ---------- 按需下载 ----------

    /// <summary>
    /// 确保在线壁纸的完整文件已下载到本地缓存（已下载直接返回 true）。
    /// 成功后 item.Path 指向缓存文件，并持久化到 pages.json。
    /// </summary>
    public async Task<bool> EnsureDownloadedAsync(WallpaperItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path))
            return true;

        if (string.IsNullOrWhiteSpace(item.SourceUrl))
            return false;

        var local = TryGetCachedPath(item.SourceUrl) ?? await DownloadAsync(item.SourceUrl).ConfigureAwait(false);
        if (local == null)
            return false;

        item.Path = local; // 页缓存条目是共享引用，直接更新并持久化
        SavePages();
        return true;
    }

    // ---------- 必应回退（Pexels 不可用时保证静态页有内容） ----------

    /// <summary>本次会话抓取的必应图片池（按 urlbase 去重，一次抓取供所有分类复用）。</summary>
    private List<WallpaperItem>? _bingImages;
    private readonly SemaphoreSlim _bingLock = new(1, 1);

    /// <summary>
    /// 把必应每日精选注入当前分类的分页缓存：已注入过（按来源地址去重）的条目跳过，
    /// 返回本次新增条目；必应接口不可用时返回空列表。
    /// </summary>
    private async Task<List<WallpaperItem>> FetchBingImagesAsync(PageEntry entry, string category)
    {
        await _bingLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_bingImages == null)
                _bingImages = await FetchBingArchiveAsync().ConfigureAwait(false);

            var seen = entry.Items
                .Select(x => x.SourceUrl)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var added = new List<WallpaperItem>();
            foreach (var source in _bingImages)
            {
                if (!seen.Add(source.SourceUrl!))
                    continue;

                // 必应条目归入当前浏览分类（每个分类的缓存独立注入一份副本）
                added.Add(new WallpaperItem
                {
                    Path = string.Empty, // 设为壁纸/预览时按需下载
                    Type = WallpaperType.Image,
                    Category = category,
                    Title = source.Title,
                    IsOnline = true,
                    SourceUrl = source.SourceUrl,
                    ThumbnailUrl = source.ThumbnailUrl
                });
            }

            return added;
        }
        finally
        {
            _bingLock.Release();
        }
    }

    /// <summary>
    /// 抓取必应每日精选历史图片：接口单段 idx+n 上限 8，
    /// 错开 idx=0/8/16 三次请求拼接并按地址去重，约得到 18 天的精选内容。
    /// </summary>
    private static async Task<List<WallpaperItem>> FetchBingArchiveAsync()
    {
        var result = new List<WallpaperItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var idx in new[] { 0, 8, 16 })
        {
            try
            {
                var api = $"https://cn.bing.com/HPImageArchive.aspx?format=js&idx={idx}&n=8";
                var json = await Http.GetStringAsync(api).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("images", out var images))
                    continue;

                foreach (var img in images.EnumerateArray())
                {
                    try
                    {
                        var urlbase = img.TryGetProperty("urlbase", out var baseProp) ? baseProp.GetString() : null;
                        if (string.IsNullOrWhiteSpace(urlbase))
                            continue;
                        if (urlbase.StartsWith("//"))
                            urlbase = "https:" + urlbase;

                        // 同一张图在不同 idx 段可能重复出现，按 urlbase 去重
                        if (!seen.Add(urlbase))
                            continue;

                        // 缩略图（列表浏览）与完整文件（设为壁纸时按需下载）共用 urlbase 拼不同尺寸
                        var copyright = img.TryGetProperty("copyright", out var cpProp) ? cpProp.GetString() : null;
                        var title = copyright ?? "必应每日精选";
                        var cut = title.IndexOf(" (", StringComparison.Ordinal);
                        if (cut > 0)
                            title = title[..cut].Trim();

                        result.Add(new WallpaperItem
                        {
                            Type = WallpaperType.Image,
                            Title = title,
                            SourceUrl = urlbase + "_1920x1080.jpg",
                            ThumbnailUrl = urlbase + "_400x240.jpg"
                        });
                    }
                    catch
                    {
                        // 单张解析失败时跳过
                    }
                }
            }
            catch
            {
                // 单段请求失败时继续下一段
            }
        }

        return result;
    }

    // ---------- Pexels API ----------

    private async Task<List<WallpaperItem>> FetchPexelsPageAsync(WallpaperType type, string category, int page)
    {
        var result = new List<WallpaperItem>();
        var key = _config.PexelsApiKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("未配置 Pexels API Key（可在设置页填入后重试）");

        var categoryDef = Categories.FirstOrDefault(c => c.Name == category) ?? Categories[0];
        var url = BuildApiUrl(type, categoryDef.Query, page);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", key);

            using var response = await Http.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (type == WallpaperType.Image)
                ParsePhotos(doc, category, result);
            else
                ParseVideos(doc, category, result);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // 网络/API 失败上抛给调用方提示，缓存不受影响
            throw new InvalidOperationException("Pexels 请求失败：" + ex.Message, ex);
        }

        return result;
    }

    private static string BuildApiUrl(WallpaperType type, string query, int page)
    {
        var pp = PageSize.ToString();
        return type == WallpaperType.Image
            ? string.IsNullOrEmpty(query)
                ? $"https://api.pexels.com/v1/curated?per_page={pp}&page={page}"
                : $"https://api.pexels.com/v1/search?query={Uri.EscapeDataString(query)}&orientation=landscape&per_page={pp}&page={page}"
            : string.IsNullOrEmpty(query)
                ? $"https://api.pexels.com/videos/popular?per_page={pp}&page={page}" // 视频精选接口是 /popular（/curated 不存在，返回 404）
                : $"https://api.pexels.com/videos/search?query={Uri.EscapeDataString(query)}&orientation=landscape&per_page={pp}&page={page}";
    }

    private static void ParsePhotos(JsonDocument doc, string category, List<WallpaperItem> result)
    {
        if (!doc.RootElement.TryGetProperty("photos", out var photos))
            return;

        foreach (var photo in photos.EnumerateArray())
        {
            try
            {
                var pageUrl = photo.TryGetProperty("url", out var pageProp) ? pageProp.GetString() : null;
                var src = photo.GetProperty("src");
                var thumbnail = src.TryGetProperty("medium", out var mediumProp) ? mediumProp.GetString() : null;
                var original = src.TryGetProperty("original", out var origProp) ? origProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(original))
                    continue;

                // 原图分辨率过大，完整文件统一压到 2560px 宽（设壁纸时才下载）
                var downloadUrl = original + "?auto=compress&cs=tinysrgb&w=2560";
                var alt = photo.TryGetProperty("alt", out var altProp) ? altProp.GetString() : null;
                var title = PexelsTitle(pageUrl, alt, "Pexels 壁纸");

                result.Add(new WallpaperItem
                {
                    Path = string.Empty, // 未下载：设为壁纸/预览时按需下载
                    Type = WallpaperType.Image,
                    Category = category,
                    Title = title,
                    IsOnline = true,
                    SourceUrl = downloadUrl,
                    ThumbnailUrl = thumbnail
                });
            }
            catch
            {
                // 单张解析失败时跳过
            }
        }
    }

    private static void ParseVideos(JsonDocument doc, string category, List<WallpaperItem> result)
    {
        if (!doc.RootElement.TryGetProperty("videos", out var videos))
            return;

        foreach (var video in videos.EnumerateArray())
        {
            try
            {
                var pageUrl = video.TryGetProperty("url", out var pageProp) ? pageProp.GetString() : null;
                var link = PickVideoFile(video);
                if (string.IsNullOrWhiteSpace(link))
                    continue;

                var thumbnail = video.TryGetProperty("image", out var imageProp) ? imageProp.GetString() : null;
                var title = PexelsTitle(pageUrl, null, "Pexels 视频");

                result.Add(new WallpaperItem
                {
                    Path = string.Empty, // 未下载：设为壁纸/预览时按需下载
                    Type = WallpaperType.Video,
                    Category = category,
                    Title = title,
                    IsOnline = true,
                    SourceUrl = link,
                    ThumbnailUrl = thumbnail
                });
            }
            catch
            {
                // 单条解析失败时跳过
            }
        }
    }

    /// <summary>从 Pexels 视频的多档 video_files 中挑选适合做壁纸的 mp4：优先不超过 1920 宽的最大档。</summary>
    private static string? PickVideoFile(JsonElement video)
    {
        string? best = null;
        var bestWidth = -1;
        string? fallback = null;
        var fallbackWidth = int.MaxValue;

        foreach (var file in video.GetProperty("video_files").EnumerateArray())
        {
            var type = file.TryGetProperty("file_type", out var typeProp) ? typeProp.GetString() : null;
            if (!string.Equals(type, "video/mp4", StringComparison.OrdinalIgnoreCase))
                continue;

            var link = file.TryGetProperty("link", out var linkProp) ? linkProp.GetString() : null;
            var width = file.TryGetProperty("width", out var widthProp) && widthProp.ValueKind == JsonValueKind.Number
                ? widthProp.GetInt32()
                : 0;
            if (string.IsNullOrWhiteSpace(link) || width <= 0)
                continue;

            if (width <= 1920 && width > bestWidth)
            {
                best = link;
                bestWidth = width;
            }
            else if (width > 1920 && width < fallbackWidth)
            {
                fallback = link;
                fallbackWidth = width;
            }
        }

        return best ?? fallback;
    }

    /// <summary>
    /// 从 Pexels 页面地址解析标题：形如 /photo/xxx-yyy-123456/ 或 /video/xxx-yyy-1208094/，
    /// 去掉尾部数字 ID 并用空格连接；有 alt 文本时优先用 alt，否则用后备标题。
    /// </summary>
    private static string PexelsTitle(string? pageUrl, string? alt, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(alt))
            return alt.Trim();

        try
        {
            if (!string.IsNullOrWhiteSpace(pageUrl))
            {
                var slug = Path.GetFileNameWithoutExtension(new Uri(pageUrl).AbsolutePath);
                // 去掉尾部的数字 ID（如 vibrant-rgb-gaming-setup-33050962 → vibrant-rgb-gaming-setup）
                var cut = slug.Length;
                while (cut > 0 && (char.IsDigit(slug[cut - 1]) || slug[cut - 1] == '-'))
                    cut--;
                slug = slug[..cut].Replace('-', ' ').Trim();
                if (slug.Length > 0)
                    return char.ToUpperInvariant(slug[0]) + slug[1..];
            }
        }
        catch
        {
            // 地址格式异常时使用后备标题
        }

        return fallback;
    }

    // ---------- 分页缓存持久化 ----------

    private void LoadPages()
    {
        try
        {
            var file = Path.Combine(CacheFolder, PagesFileName);
            if (!File.Exists(file))
                return;

            var manifest = JsonSerializer.Deserialize<PageManifest>(File.ReadAllText(file));
            if (manifest == null)
                return;

            foreach (var entry in manifest.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                    continue;

                // 清单里可能超过上限（版本变更），加载时同样裁剪
                TrimPages(entry);
                _pages[entry.Key] = entry;
            }
        }
        catch
        {
            // 缓存损坏时按无缓存处理
        }
    }

    private void SavePages()
    {
        try
        {
            var manifest = new PageManifest
            {
                Entries = _pages.Values.ToList()
            };
            File.WriteAllText(Path.Combine(CacheFolder, PagesFileName),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不阻断程序运行（最多重新请求一次）
        }
    }

    /// <summary>已下载到本地缓存的文件数量（诊断用）。</summary>
    public int CachedFileCount => Directory.GetFiles(CacheFolder).Length;

    // ---------- 缓存转正与清理 ----------

    /// <summary>
    /// 把在线壁纸「转正」到正式壁纸库：文件复制到图库文件夹（按标题命名），临时缓存保留。
    /// 同一地址重复转正时直接复用已有副本。返回转正后的壁纸项；失败返回 null。
    /// 调用前须确保 item.Path 已通过 EnsureDownloadedAsync 下载。
    /// </summary>
    public WallpaperItem? PromoteToLibrary(WallpaperItem item, string libraryFolder)
    {
        if (string.IsNullOrWhiteSpace(item.SourceUrl) ||
            string.IsNullOrWhiteSpace(item.Path) || !File.Exists(item.Path))
            return null;

        try
        {
            Directory.CreateDirectory(libraryFolder);

            // 已转正过且副本仍在：复用正式库文件
            if (_promotedUrls.TryGetValue(item.SourceUrl, out var existing) && File.Exists(existing))
            {
                return new WallpaperItem
                {
                    Path = existing,
                    Type = item.Type,
                    Title = item.Title,
                    IsFavorite = item.IsFavorite,
                    IsOnline = false
                };
            }

            var ext = Path.GetExtension(item.Path);
            var name = SanitizeFileName(item.Title) + ext;
            var destination = GetUniquePath(Path.Combine(libraryFolder, name));

            File.Copy(item.Path, destination, overwrite: false);

            _promotedUrls[item.SourceUrl] = destination;
            SavePromotedUrls();

            return new WallpaperItem
            {
                Path = destination,
                Type = item.Type,
                Title = item.Title,
                IsFavorite = item.IsFavorite,
                IsOnline = false
            };
        }
        catch
        {
            // 复制失败（磁盘问题等）时保留原临时文件
            return null;
        }
    }

    /// <summary>缓存占用描述（用于设置页显示），如「5 个文件，共 23.4 MB」。</summary>
    public string GetCacheSizeText()
    {
        long total = 0;
        var count = 0;

        foreach (var file in EnumerateCacheFiles())
        {
            try
            {
                total += new FileInfo(file).Length;
                count++;
            }
            catch
            {
                // 文件被占用时跳过统计
            }
        }

        return count == 0 ? "缓存为空" : $"{count} 个文件，共 {FormatBytes(total)}";
    }

    /// <summary>
    /// 清理临时缓存文件。可传入需要保留的文件路径（当前正在使用的壁纸）。
    /// 返回（清理的文件数，释放的字节数）。
    /// </summary>
    public (int Count, long Bytes) ClearCache(params string?[] protectedPaths)
    {
        var protect = protectedPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        long freed = 0;
        var count = 0;

        foreach (var file in EnumerateCacheFiles())
        {
            if (protect.Contains(file))
                continue;

            try
            {
                var length = new FileInfo(file).Length;
                File.Delete(file);
                freed += length;
                count++;
            }
            catch
            {
                // 文件被占用（正在播放等）时跳过，不影响其余清理
            }
        }

        // 清理后已下载条目的 Path 失效：同步清空标记，重新浏览时按需重下
        foreach (var item in GetAllCachedItems())
        {
            if (!string.IsNullOrEmpty(item.Path) && !File.Exists(item.Path))
                item.Path = string.Empty;
        }
        SavePages();

        return (count, freed);
    }

    /// <summary>缓存目录内的可清理文件（排除元数据文件：转正记录、分页清单）。</summary>
    private IEnumerable<string> EnumerateCacheFiles()
    {
        if (!Directory.Exists(CacheFolder))
            return Enumerable.Empty<string>();

        var markers = new[]
        {
            Path.Combine(CacheFolder, PromotedFileName),
            Path.Combine(CacheFolder, PagesFileName)
        };

        return Directory.GetFiles(CacheFolder)
            .Where(f => !markers.Contains(f, StringComparer.OrdinalIgnoreCase));
    }

    private void LoadPromotedUrls()
    {
        try
        {
            var file = Path.Combine(CacheFolder, PromotedFileName);
            if (!File.Exists(file))
                return;

            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
            if (map != null)
                foreach (var kv in map)
                    _promotedUrls[kv.Key] = kv.Value;
        }
        catch
        {
            // 记录损坏时按空处理，最多多复制一份副本
        }
    }

    private void SavePromotedUrls()
    {
        try
        {
            File.WriteAllText(Path.Combine(CacheFolder, PromotedFileName),
                JsonSerializer.Serialize(_promotedUrls));
        }
        catch
        {
            // 保存失败不阻断程序运行
        }
    }

    private static string SanitizeFileName(string? title)
    {
        var name = (title ?? string.Empty).Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        if (name.Length > 60)
            name = name[..60];
        return string.IsNullOrWhiteSpace(name) ? "在线壁纸" : name;
    }

    private static string GetUniquePath(string destination)
    {
        if (!File.Exists(destination))
            return destination;

        var dir = Path.GetDirectoryName(destination)!;
        var name = Path.GetFileNameWithoutExtension(destination);
        var ext = Path.GetExtension(destination);

        for (var i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1 << 30 => $"{bytes / 1024.0 / 1024 / 1024:0.00} GB",
            >= 1 << 20 => $"{bytes / 1024.0 / 1024:0.0} MB",
            >= 1 << 10 => $"{bytes / 1024.0:0.0} KB",
            _ => $"{bytes} B"
        };
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(20);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DynamicWallpaper/1.0");
        return client;
    }

    private string? TryGetCachedPath(string url)
    {
        var file = Path.Combine(CacheFolder, CacheFileName(url));
        return File.Exists(file) ? file : null;
    }

    private static string CacheFileName(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        var ext = Path.GetExtension(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(ext) || ext.Length > 5)
            ext = ".bin";
        return hash + ext.ToLowerInvariant();
    }

    private async Task<string?> DownloadAsync(string url)
    {
        try
        {
            var file = Path.Combine(CacheFolder, CacheFileName(url));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // 大文件（Pexels HD 视频可达几十 MB）独立长超时，不受全局 20 秒限制
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = File.Create(file);
            await response.Content.CopyToAsync(stream, cts.Token).ConfigureAwait(false);
            return file;
        }
        catch
        {
            return null;
        }
    }
}
