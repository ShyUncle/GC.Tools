using System.IO;
using System.Linq;
using System.Windows.Threading;
using DynamicWallpaper.Config;
using DynamicWallpaper.Library;
using DynamicWallpaper.Models;
using DynamicWallpaper.Wallpaper;
using Timer = System.Windows.Threading.DispatcherTimer;


namespace DynamicWallpaper;

public class WallpaperManager : IDisposable
{
    private readonly WallpaperLibrary _library;
    private readonly AppConfig _config;
    private readonly WallpaperForm _wallpaperForm;
    private Timer? _rotationTimer;
    private WallpaperItem? _current;
    private bool _windowShown;

    /// <summary>壁纸窗口的桌面挂载诊断信息。</summary>
    public string AttachStatus => _wallpaperForm.AttachStatus;

    /// <summary>当前使用中的壁纸（启动恢复/手动应用/轮换后更新）。</summary>
    public WallpaperItem? CurrentItem => _current;

    /// <summary>轮换范围一级分类：dynamic/static/favorites/uploads（其他值=全部壁纸）。</summary>
    public string RotationScope { get; set; } = "all";

    /// <summary>轮换范围二级分类（仅动态/静态壁纸生效；null=全部）。</summary>
    public string? RotationCategory { get; set; }

    /// <summary>自动轮换是否正在进行中（用于设置页开关状态同步）。</summary>
    public bool IsRotating => _rotationTimer != null;

    public WallpaperManager(WallpaperLibrary library, AppConfig config)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _wallpaperForm = new WallpaperForm();
    }

    /// <summary>
    /// 程序启动时调用：优先恢复上次壁纸；没有任何可用壁纸时不显示壁纸窗口，
    /// 让系统原有壁纸保持可见（不接管桌面）。
    /// </summary>
    /// <returns>实际应用的壁纸；无壁纸时返回 null。</returns>
    public WallpaperItem? Show()
    {
        var item = ResolveStartupWallpaper();
        if (item == null)
            return null;

        Apply(item);
        return item;
    }

    /// <summary>
    /// 解析启动壁纸：只认配置中记忆的路径。
    /// 未设置过壁纸或记忆文件已不存在时返回 null —— 表示不修改桌面，保持系统原壁纸可见，
    /// 绝不回退到库中其他壁纸。
    /// </summary>
    private WallpaperItem? ResolveStartupWallpaper()
    {
        var last = _config.LastWallpaperPath;
        if (string.IsNullOrWhiteSpace(last) || !File.Exists(last))
            return null;

        // 记忆路径有效即恢复（即使该项已不在当前壁纸库列表中）；类型按扩展名推断
        return new WallpaperItem
        {
            Path = last,
            Type = IsVideoFile(last) ? WallpaperType.Video : WallpaperType.Image
        };
    }

    private static bool IsVideoFile(string path) =>
        Path.GetExtension(path).Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".webm", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".wmv", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 应用壁纸并记住当前选择：
    /// 图片走系统原生 SystemParametersInfo API（不创建任何窗口，图标/右键/Win+D 全部原生正常）；
    /// 视频走 WebView2 壁纸窗口（懒启动并嵌入桌面）。
    /// </summary>
    public void Apply(WallpaperItem? item)
    {
        if (item == null)
            return;

        _current = item;

        if (item.Type == WallpaperType.Video)
        {
            EnsureWindowShown();
            _wallpaperForm.SetVideoWallpaper(item.Path);
        }
        else
        {
            HideWallpaperWindow();

            if (NativeWallpaper.SetImageWallpaper(item.Path))
                _wallpaperForm.AttachStatus = "图片壁纸：已设为系统桌面壁纸（原生显示）";
            else
                _wallpaperForm.AttachStatus = "图片壁纸：系统 API 设置失败";
        }

        // 记忆当前壁纸，供下次启动自动恢复。
        _config.LastWallpaperPath = item.Path;
        ConfigService.Save(_config);
    }

    /// <summary>确保壁纸窗口已显示并嵌入桌面；图片壁纸不需要窗口。</summary>
    private void EnsureWindowShown()
    {
        if (_windowShown)
        {
            if (!_wallpaperForm.IsVisible)
                _wallpaperForm.Show();
            _wallpaperForm.AttachToDesktop();
            return;
        }

        _wallpaperForm.Show();
        _wallpaperForm.AttachToDesktop();
        _windowShown = true;
    }

    /// <summary>图片壁纸不需要覆盖层窗口，隐藏并脱离桌面，让原生壁纸直接可见。</summary>
    private void HideWallpaperWindow()
    {
        if (!_windowShown)
            return;

        _wallpaperForm.DetachFromDesktop();
        if (_wallpaperForm.IsVisible)
            _wallpaperForm.Hide();
    }

    public void StartRotation()
    {
        StopRotation();

        if (_config.RotationIntervalSeconds <= 0)
            return;

        _rotationTimer = new Timer();
        _rotationTimer.Interval = TimeSpan.FromSeconds(_config.RotationIntervalSeconds);
        _rotationTimer.Tick += OnRotationTick;
        _rotationTimer.Start();
    }

    public void StopRotation()
    {
        if (_rotationTimer != null)
        {
            _rotationTimer.Stop();
            _rotationTimer = null;
        }
    }

    /// <summary>
    /// 切换到轮换范围内的下一张壁纸（按设置页配置的一级/二级范围过滤）。
    /// 范围内没有可用壁纸时不做任何切换。
    /// </summary>
    public void Next()
    {
        var items = GetRotationItems();
        if (items.Count == 0)
            return;

        WallpaperItem next;
        if (_current == null)
        {
            next = items[0];
        }
        else
        {
            var index = items.FindIndex(x =>
                string.Equals(x.Path, _current.Path, StringComparison.OrdinalIgnoreCase));
            next = index < 0 ? items[0] : items[(index + 1) % items.Count];
        }

        Apply(next);
    }

    /// <summary>按轮换范围（一级分类 + 二级分类）过滤可用壁纸。</summary>
    private List<WallpaperItem> GetRotationItems()
    {
        IEnumerable<WallpaperItem> query = _library.Items.Where(i => File.Exists(i.Path));

        query = RotationScope switch
        {
            "dynamic" => query.Where(i => i.IsOnline && i.Type == WallpaperType.Video),
            "static" => query.Where(i => i.IsOnline && i.Type == WallpaperType.Image),
            "favorites" => query.Where(i => i.IsFavorite),
            "uploads" => query.Where(i => !i.IsOnline),
            _ => query
        };

        if (RotationScope is "dynamic" or "static" && !string.IsNullOrWhiteSpace(RotationCategory))
            query = query.Where(i => i.Category == RotationCategory);

        return query.ToList();
    }

    private void OnRotationTick(object? sender, EventArgs e)
    {
        Next();
    }

    public void Dispose()
    {
        StopRotation();
        // 退出时不恢复原壁纸：最后设置的壁纸保留为桌面壁纸（下次启动也会按记忆自动恢复同一张）。
        if (_windowShown)
            _wallpaperForm.DetachFromDesktop();
        _wallpaperForm.Close();
    }
}
