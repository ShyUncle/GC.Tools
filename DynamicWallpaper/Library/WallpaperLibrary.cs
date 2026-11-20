using System.IO;
using System.Text.Json;
using DynamicWallpaper.Config;
using DynamicWallpaper.Models;

namespace DynamicWallpaper.Library;

    public class WallpaperLibrary
    {
        /// <summary>导入到自有壁纸库的文件所使用的分类名。</summary>
        public const string LibraryCategory = "我的壁纸";

        private readonly AppConfig _config;
        private readonly List<WallpaperItem> _items = new();

        // 「我的壁纸」列表（用户手动导入的壁纸）：直接以 JSON 存储，不扫描图库文件夹。
        // 在线壁纸转正只保存文件、不进入此列表，因此不会出现在「我的壁纸」栏。
        private List<WallpaperItem> _uploads = new();

        /// <summary>「我的壁纸」列表持久化文件（AppData\DynamicWallpaper\uploads.json）。</summary>
        private static string UploadsFile =>
            Path.Combine(ConfigService.GetAppDataFolder(), "uploads.json");

        // 收藏与分类等用户数据按路径保存，避免 Load()/刷新时被重置。
        private readonly Dictionary<string, (bool Favorite, string Category)> _metadata =
            new(StringComparer.OrdinalIgnoreCase);

    // 外部来源（在线壁纸）：每次 Load() 时合并进列表，收藏状态通过 metadata 持久化。
    private IReadOnlyList<WallpaperItem>? _externalSource;

    /// <summary>收藏/分类元数据持久化文件（AppData\DynamicWallpaper\metadata.json）。</summary>
    private static string MetadataFile =>
        Path.Combine(ConfigService.GetAppDataFolder(), "metadata.json");

    /// <summary>收藏/分类元数据的键：本地壁纸用文件路径，在线壁纸用来源 URL（未下载的远程项也可收藏）。</summary>
    public static string KeyOf(WallpaperItem item)
        => item.IsOnline && !string.IsNullOrWhiteSpace(item.SourceUrl)
            ? item.SourceUrl!
            : item.Path;

    public IReadOnlyList<WallpaperItem> Items => _items.AsReadOnly();

    /// <summary>应用自有壁纸库的绝对路径。</summary>
    public string LibraryFolder => ConfigService.GetLibraryFolder(_config);

    public WallpaperLibrary(AppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        LoadMetadata();
    }

    /// <summary>注册外部来源（在线壁纸），Load() 时合并进壁纸列表。</summary>
    public void SetExternalSource(IEnumerable<WallpaperItem>? items)
        => _externalSource = items?.ToList();

    public void Load()
    {
        // 「我的壁纸」列表来自 uploads.json（JSON 存储，不扫描图库文件夹）。
        LoadUploads();
        RebuildItems();
    }

    /// <summary>根据 JSON 列表与外部在线来源重建内存中的壁纸列表。</summary>
    private void RebuildItems()
    {
        _items.Clear();
        EnsureLibraryFolder();

        // 用户导入的「我的壁纸」：只保留文件仍存在的条目（存储地址变更/文件被删时自然隐藏）
        foreach (var upload in _uploads)
        {
            if (upload == null || string.IsNullOrWhiteSpace(upload.Path))
                continue;
            if (!File.Exists(upload.Path))
                continue;
            if (_items.Any(x => string.Equals(x.Path, upload.Path, StringComparison.OrdinalIgnoreCase)))
                continue;

            var item = new WallpaperItem
            {
                Path = upload.Path,
                Type = upload.Type,
                Category = string.IsNullOrWhiteSpace(upload.Category) ? LibraryCategory : upload.Category,
                Title = upload.Title,
                IsFavorite = upload.IsFavorite
            };

            if (_metadata.TryGetValue(item.Path, out var meta))
            {
                item.IsFavorite = meta.Favorite;
                item.Category = meta.Category;
            }

            _items.Add(item);
        }

        // 合并外部来源（在线壁纸）：无论完整文件是否已下载都保留（远程项靠缩略图展示，
        // 设为壁纸时才按需下载）；按收藏键去重。
        if (_externalSource != null)
        {
            foreach (var external in _externalSource)
            {
                if (external == null || !external.IsOnline)
                    continue;
                if (string.IsNullOrWhiteSpace(KeyOf(external)))
                    continue;
                if (_items.Any(x => string.Equals(KeyOf(x), KeyOf(external), StringComparison.OrdinalIgnoreCase)))
                    continue;

                var merged = new WallpaperItem
                {
                    Path = external.Path,
                    Type = external.Type,
                    Category = external.Category,
                    Title = external.Title,
                    IsOnline = true,
                    SourceUrl = external.SourceUrl,
                    ThumbnailUrl = external.ThumbnailUrl
                };

                // 收藏状态：优先按 URL 键读取；老版本数据按缓存路径记录，命中时迁移到 URL 键
                var favoriteKey = KeyOf(merged);
                if (_metadata.TryGetValue(favoriteKey, out var externalMeta) ||
                    (!string.IsNullOrWhiteSpace(merged.Path) &&
                     _metadata.TryGetValue(merged.Path, out externalMeta)))
                {
                    merged.IsFavorite = externalMeta.Favorite;
                    if (!string.Equals(favoriteKey, merged.Path, StringComparison.OrdinalIgnoreCase))
                        _metadata[favoriteKey] = (externalMeta.Favorite, externalMeta.Category);
                    SaveMetadata();
                }

                _items.Add(merged);
            }
        }

        // 清理已不存在的元数据键，避免无限增长（在线项按 URL 键保留）。
        var validKeys = _items.Select(KeyOf)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in _metadata.Keys.Where(k => !validKeys.Contains(k)).ToList())
            _metadata.Remove(key);
    }

    /// <summary>
    /// 从 uploads.json 读取「我的壁纸」列表。
    /// 首次运行（文件不存在）时做一次迁移：把图库文件夹里已有的媒体文件扫描入库后写回 JSON，
    /// 此后完全以 JSON 列表为准，不再扫描文件夹。
    /// </summary>
    private void LoadUploads()
    {
        if (File.Exists(UploadsFile))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<List<WallpaperItem>>(File.ReadAllText(UploadsFile));
                if (stored != null)
                {
                    _uploads = stored;
                    return;
                }
            }
            catch
            {
                // 列表损坏时按空处理，走迁移逻辑重建
            }
        }

        // 一次性迁移：扫描旧版图库文件夹生成初始列表（此后不再扫描）。
        // 排除在线壁纸转正产生的副本（promoted.json 有记录）——它们不属于「我的壁纸」。
        _uploads = new List<WallpaperItem>();
        try
        {
            var promotedPaths = LoadPromotedLibraryPaths();
            EnsureLibraryFolder();
            if (Directory.Exists(LibraryFolder))
            {
                foreach (var file in GetMediaFiles(LibraryFolder))
                {
                    if (promotedPaths.Contains(Path.GetFullPath(file)))
                        continue;

                    var item = new WallpaperItem
                    {
                        Path = file,
                        Type = IsVideo(file) ? WallpaperType.Video : WallpaperType.Image,
                        Category = LibraryCategory
                    };

                    if (_metadata.TryGetValue(file, out var meta))
                        item.IsFavorite = meta.Favorite;

                    _uploads.Add(item);
                }
            }
            SaveUploads();
        }
        catch
        {
            // 迁移失败时按空列表处理，用户重新导入即可
        }
    }

    /// <summary>读取 promoted.json 中已转正文件的库内路径集合（迁移时排除转正副本）。</summary>
    private static HashSet<string> LoadPromotedLibraryPaths()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var file = Path.Combine(ConfigService.GetAppDataFolder(), "OnlineCache", "promoted.json");
            if (!File.Exists(file))
                return result;

            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
            if (map != null)
            {
                foreach (var path in map.Values)
                {
                    if (!string.IsNullOrWhiteSpace(path))
                        result.Add(Path.GetFullPath(path));
                }
            }
        }
        catch
        {
            // 记录损坏时按空集合处理（最多多迁移几个转正副本）
        }

        return result;
    }

    private void SaveUploads()
    {
        try
        {
            File.WriteAllText(UploadsFile,
                JsonSerializer.Serialize(_uploads, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不阻断程序运行
        }
    }

    /// <summary>
    /// 将用户选择的本地媒体文件复制导入应用自有壁纸库，并写入 uploads.json 列表。
    /// 返回实际导入的文件数量。
    /// </summary>
    public int ImportFiles(IEnumerable<string> sourceFiles)
    {
        EnsureLibraryFolder();
        var imported = 0;

        foreach (var source in sourceFiles)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                continue;
            if (!IsSupportedMedia(source))
                continue;

            try
            {
                var fileName = Path.GetFileName(source);
                var destination = GetUniqueDestination(Path.Combine(LibraryFolder, fileName));
                File.Copy(source, destination, overwrite: false);

                _uploads.Add(new WallpaperItem
                {
                    Path = destination,
                    Type = IsVideo(destination) ? WallpaperType.Video : WallpaperType.Image,
                    Category = LibraryCategory
                });
                imported++;
            }
            catch
            {
                // 单个文件失败不影响其余导入
            }
        }

        if (imported > 0)
        {
            SaveUploads();
            Load();
        }

        return imported;
    }

    /// <summary>
    /// 从自有壁纸库删除一个文件（同时从 uploads.json 列表移除）。
    /// </summary>
    public bool RemoveImportedItem(string path)
    {
        if (!IsInsideLibrary(path))
            return false;

        try
        {
            if (File.Exists(path))
                File.Delete(path);
            _metadata.Remove(path);

            var removed = _uploads.RemoveAll(x =>
                string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
                SaveUploads();

            Load();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>切换收藏状态（key 为 KeyOf 计算的收藏键：本地=路径，在线=来源 URL）。</summary>
    public void ToggleFavorite(string key)
    {
        var item = _items.FirstOrDefault(x =>
            string.Equals(KeyOf(x), key, StringComparison.OrdinalIgnoreCase));
        if (item == null)
            return;

        item.IsFavorite = !item.IsFavorite;
        _metadata[key] = (item.IsFavorite, item.Category);
        SaveMetadata();
    }

    public List<WallpaperItem> Search(string keyword, string? category = null, bool? favorite = null)
    {
        var query = _items.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(x =>
                Path.GetFileName(x.Path).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (x.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(x => x.Category == category);
        }

        if (favorite.HasValue)
        {
            query = query.Where(x => x.IsFavorite == favorite.Value);
        }

        return query.ToList();
    }

    public List<string> GetCategories()
    {
        return _items.Select(x => x.Category).Distinct().Order().ToList();
    }

    public WallpaperItem? GetNext(WallpaperItem? current)
    {
        if (_items.Count == 0)
            return null;

        if (current == null)
            return _items[0];

        var index = _items.FindIndex(x => string.Equals(x.Path, current.Path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return _items[0];

        return _items[(index + 1) % _items.Count];
    }

    private void EnsureLibraryFolder()
    {
        Directory.CreateDirectory(LibraryFolder);
    }

    /// <summary>启动时从 metadata.json 读取收藏/分类元数据。</summary>
    private void LoadMetadata()
    {
        try
        {
            if (!File.Exists(MetadataFile))
                return;

            var entries = JsonSerializer.Deserialize<List<MetadataEntry>>(
                File.ReadAllText(MetadataFile));

            if (entries == null)
                return;

            foreach (var entry in entries)
            {
                if (!string.IsNullOrWhiteSpace(entry.Path))
                    _metadata[entry.Path] = (entry.Favorite, entry.Category ?? "默认");
            }
        }
        catch
        {
            // 元数据损坏时按空处理，不影响壁纸库本身
        }
    }

    /// <summary>把收藏/分类元数据写入 metadata.json（用户操作后即时持久化）。</summary>
    private void SaveMetadata()
    {
        try
        {
            var entries = _metadata.Select(kv => new MetadataEntry(
                kv.Key, kv.Value.Favorite, kv.Value.Category)).ToList();

            File.WriteAllText(MetadataFile,
                JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 保存失败不阻断程序运行
        }
    }

    private sealed record MetadataEntry(string Path, bool Favorite, string? Category);

    /// <summary>判断某个文件是否位于应用自有壁纸库内。</summary>
    public bool IsImported(string path)
    {
        return IsInsideLibrary(path);
    }

    private bool IsInsideLibrary(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(LibraryFolder) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniqueDestination(string destination)
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

    private static IEnumerable<string> GetMediaFiles(string folder)
    {
        return Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(IsSupportedMedia);
    }

    private static bool IsSupportedMedia(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp"
                    or ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi";
    }

    private static bool IsVideo(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi";
    }
}
