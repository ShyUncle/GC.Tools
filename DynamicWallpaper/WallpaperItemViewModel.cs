using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicWallpaper.Config;
using DynamicWallpaper.Library;
using DynamicWallpaper.Models;

namespace DynamicWallpaper;

/// <summary>缩略图卡片的视图模型：负责加载缩略图与 UI 状态（选中/收藏）通知。</summary>
public class WallpaperItemViewModel : INotifyPropertyChanged
{
    private readonly WallpaperLibrary _library;
    private bool _isSelected;
    private bool _isFavorite;
    private ImageSource? _thumbnail;

    public WallpaperItemViewModel(WallpaperItem item, WallpaperLibrary library, AppConfig config)
    {
        _library = library;
        Item = item;
        _isFavorite = item.IsFavorite;

        // 在线壁纸：优先用 CDN 缩略图（远程未下载的条目也能展示），本地无缓存时后台异步下载
        if (item.IsOnline && !string.IsNullOrWhiteSpace(item.ThumbnailUrl))
        {
            var cached = RemoteThumbCache.TryGet(item.ThumbnailUrl);
            if (cached != null)
            {
                _thumbnail = DecodeFile(cached);
            }
            else
            {
                _ = LoadRemoteThumbnailAsync(item.ThumbnailUrl);
            }
        }
        else
        {
            _thumbnail = LoadLocalThumbnail(config, item);
        }
    }

    public WallpaperItem Item { get; }

    public string DisplayName => Item.Title is { Length: > 0 } title
        ? title
        : Path.GetFileName(Item.Path);

    public bool IsVideo => Item.Type == WallpaperType.Video;

    public bool IsImported => _library.IsImported(Item.Path);

    /// <summary>视频角标仅在视频类型时显示。</summary>
    public System.Windows.Visibility VideoBadgeVisibility =>
        IsVideo ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public ImageSource? Thumbnail => _thumbnail;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            Raise();
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite == value)
                return;
            _isFavorite = value;
            Raise();
        }
    }

    /// <summary>
    /// 切换收藏状态（同步到壁纸库并持久化到 metadata.json）。
    /// 动态/静态页的列表项与库内对象是不同实例，需回读真实状态并回写到来源项。
    /// 在线壁纸的收藏键是来源 URL，未下载的远程条目同样可收藏。
    /// </summary>
    public void ToggleFavorite()
    {
        var key = WallpaperLibrary.KeyOf(Item);
        _library.ToggleFavorite(key);

        var libItem = _library.Items.FirstOrDefault(x =>
            string.Equals(WallpaperLibrary.KeyOf(x), key, StringComparison.OrdinalIgnoreCase));
        IsFavorite = libItem?.IsFavorite ?? !Item.IsFavorite;
        Item.IsFavorite = IsFavorite; // 回写来源项（在线页缓存条目），刷新后星标状态保持
    }

    /// <summary>后台下载远程缩略图（CDN 小图）并解码展示；失败时保持占位样式。</summary>
    private async Task LoadRemoteThumbnailAsync(string url)
    {
        var file = await RemoteThumbCache.EnsureAsync(url).ConfigureAwait(true);
        if (file == null)
            return;

        var bitmap = DecodeFile(file);
        if (bitmap == null)
            return;

        _thumbnail = bitmap;
        Raise(nameof(Thumbnail));
    }

    /// <summary>本地壁纸缩略图：优先缓存文件，视频提取首帧；解码宽度限制在 320px 控制内存。</summary>
    private static ImageSource? LoadLocalThumbnail(AppConfig config, WallpaperItem item)
    {
        try
        {
            var file = item.Type == WallpaperType.Video
                ? ThumbnailCache.EnsureVideoThumbnailFile(config, item.Path, 320, 240)
                : ThumbnailCache.TryGetThumbnail(config, item.Path, out var cached) ? cached : item.Path;

            if (string.IsNullOrEmpty(file) || !File.Exists(file))
                return null;

            return DecodeFile(file);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? DecodeFile(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 320;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze(); // 跨 UI 线程安全访问
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
