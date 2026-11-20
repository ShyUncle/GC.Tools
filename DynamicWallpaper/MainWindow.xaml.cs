using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicWallpaper.Config;
using DynamicWallpaper.Library;
using DynamicWallpaper.Models;
using DynamicWallpaper.Online;
using DynamicWallpaper.Tray;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using ContextMenu = System.Windows.Controls.ContextMenu;
using RadioButton = System.Windows.Controls.RadioButton;

namespace DynamicWallpaper;

public partial class MainWindow : Window
{
    private enum PageKind { Dynamic, Static, Mine, Settings }

    private const string MineFavoriteFilter = "fav";
    private const string MineUploadFilter = "uploads";

    private readonly AppConfig _config;
    private readonly WallpaperLibrary _library;
    private readonly WallpaperManager _manager;
    private readonly OnlineWallpaperService _online;
    private readonly TrayController _tray = new();
    private readonly ObservableCollection<WallpaperItemViewModel> _items = new();

    private PageKind _page = PageKind.Static;
    private string _mineFilter = MineFavoriteFilter;
    private bool _uiReady;
    private bool _loadingOnline;
    private bool _onlineFailed;
    private bool _isExiting;
    private bool _hasShownTrayHint;

    // 动态/静态页各自的浏览分类（分页滚动加载，缓存互相独立）
    private string _dynamicCategory = Online.OnlineWallpaperService.Categories[0].Name;
    private string _staticCategory = Online.OnlineWallpaperService.Categories[0].Name;

    public MainWindow()
    {
        InitializeComponent();

        _config = ConfigService.Load();
        _library = new WallpaperLibrary(_config);
        _manager = new WallpaperManager(_library, _config);
        _online = new OnlineWallpaperService(_config);

        CardsList.ItemsSource = _items;
        SearchBox.Text = string.Empty;
        IntervalBox.Text = Math.Max(5, _config.RotationIntervalSeconds).ToString();

        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 在线缓存不做启动自动清理：缓存即离线数据，保留可避免每次打开都重新下载；
        // 需要释放空间时在「设置 → 存储 → 预览缓存」手动清理。

        MergeOnline(); // 注册在线分页缓存（含收藏状态同步），替代单独的 _library.Load()
        RefreshCategoryChips();
        RefreshCards();

        // 壁纸的启动恢复由 WallpaperManager.Show() 统一处理：
        // 有上次壁纸就自动恢复，没有则不接管桌面、保持系统原壁纸可见。
        _manager.Show();

        _tray.Setup(
            OnShowMain,
            OnNextAction,
            () =>
            {
                // 托盘停止轮换：与设置页开关状态保持一致并持久化
                _config.AutoStartRotation = false;
                _manager.StopRotation();
                ConfigService.Save(_config);
            },
            () =>
            {
                _config.AutoStartRotation = true;
                _manager.StartRotation();
                ConfigService.Save(_config);
            },
            OnExit);

        _uiReady = true;
        SyncSettingsUi();

        // 轮换范围从配置恢复（一级范围 + 二级分类），并同步到管理器
        _manager.RotationScope = _config.RotationScope;
        _manager.RotationCategory = _config.RotationCategory;
        SetupRotationControls();

        // 启动不发起网络请求：分页缓存（pages.json）命中即秒开；
        // 当前分类无缓存时进入动态/静态页才会触发第 1 页加载。

        if (_config.AutoStartRotation)
            StartRotation();
    }

    // ---------- 页面切换 ----------

    private void SwitchPage(PageKind page)
    {
        _page = page;
        var browse = page != PageKind.Settings;
        var isMine = page == PageKind.Mine;
        var isDynamicStatic = page is PageKind.Dynamic or PageKind.Static;

        BrowsePanel.Visibility = browse ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = browse ? Visibility.Collapsed : Visibility.Visible;

        // 分类筛选条仅动态/静态页显示；「我的」页操作收进左侧边栏，设置页隐藏
        ChipsBar.Visibility = isDynamicStatic ? Visibility.Visible : Visibility.Collapsed;

        // 「我的」页分类改为左侧竖排（含标题/数量/操作按钮）；列宽需容纳卡片边距(14+8)+内容
        MineNavPanel.Visibility = isMine ? Visibility.Visible : Visibility.Collapsed;
        MineNavColumn.Width = isMine ? new GridLength(194) : new GridLength(0);

        if (!browse)
        {
            SyncSettingsUi();
            return;
        }

        RefreshCategoryChips();
        RefreshCards();

        // 进入动态/静态页时确保当前分类已有内容（命中分页缓存则不联网）
        if (isDynamicStatic)
            _ = EnsureOnlinePageAsync();
    }

    private void OnNavDynamic(object sender, RoutedEventArgs e)
    {
        if (_uiReady) SwitchPage(PageKind.Dynamic);
    }

    private void OnNavStatic(object sender, RoutedEventArgs e)
    {
        if (_uiReady) SwitchPage(PageKind.Static);
    }

    private void OnNavMine(object sender, RoutedEventArgs e)
    {
        if (_uiReady) SwitchPage(PageKind.Mine);
    }

    private void OnNavSettings(object sender, RoutedEventArgs e)
    {
        if (_uiReady) SwitchPage(PageKind.Settings);
    }

    // ---------- 在线壁纸分页加载 ----------

    /// <summary>当前在线页对应的壁纸类型（动态=视频 / 静态=图片）。</summary>
    private WallpaperType CurrentOnlineType =>
        _page == PageKind.Dynamic ? WallpaperType.Video : WallpaperType.Image;

    /// <summary>当前浏览分类（动态/静态页各自记忆）。</summary>
    private string CurrentOnlineCategory =>
        _page == PageKind.Dynamic ? _dynamicCategory : _staticCategory;

    /// <summary>确保当前分类已有内容：无缓存且未加载失败时拉取第 1 页（命中缓存则不联网）。</summary>
    private async Task EnsureOnlinePageAsync()
    {
        if (_page is not (PageKind.Dynamic or PageKind.Static))
            return;

        var type = CurrentOnlineType;
        var category = CurrentOnlineCategory;

        if (_online.GetCachedItems(type, category).Count > 0 || _online.IsExhausted(type, category))
            return;

        await LoadNextOnlinePageAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 加载下一页（Pexels 分类分页）：成功后合并进壁纸库并刷新卡片。
    /// 已到最后一页 / 正在加载 / 上次失败待手动重试时直接返回。
    /// </summary>
    private async Task LoadNextOnlinePageAsync()
    {
        if (_loadingOnline || _page is not (PageKind.Dynamic or PageKind.Static))
            return;

        var type = CurrentOnlineType;
        var category = CurrentOnlineCategory;

        if (_online.IsExhausted(type, category) || _onlineFailed)
            return;

        var nextPage = _online.GetCachedPageCount(type, category) + 1;
        _loadingOnline = true;
        _onlineFailed = false;
        ShowToast($"正在加载「{category}」{(_page == PageKind.Dynamic ? "动态" : "静态")}壁纸（第 {nextPage} 页）…",
            "\uE823", autoHideMs: 0);

        try
        {
            await _online.FetchPageAsync(type, category, nextPage).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // 分页获取失败不致命：记录日志、提示重试（点「刷新」或继续滚动不自动重试）
            Logging.ErrorLogger.Log(ex, "在线壁纸分页获取");
            _onlineFailed = true;
            ShowToast("在线内容加载失败：" + ex.Message, "\uE783");
            return;
        }
        finally
        {
            _loadingOnline = false;
        }

        HideToast();
        MergeOnline();
        RefreshCards();
        MaybeLoadMore(); // 一页填不满视口时继续补页
    }

    /// <summary>滚动到底部附近时自动加载下一页。</summary>
    private void OnCardsScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_page is not (PageKind.Dynamic or PageKind.Static))
            return;

        if (e.ExtentHeight <= e.ViewportHeight ||
            e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 400)
        {
            _ = LoadNextOnlinePageAsync();
        }
    }

    private ScrollViewer? _cardsScroll;

    /// <summary>从 ListView 可视树中找到内部 ScrollViewer（缓存结果）。</summary>
    private ScrollViewer? GetCardsScroll()
    {
        if (_cardsScroll != null)
            return _cardsScroll;

        return _cardsScroll = FindScrollViewer(CardsList);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? parent)
    {
        if (parent == null)
            return null;

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer sv)
                return sv;
            if (FindScrollViewer(child) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>列表填不满视口（或接近底部）时补加载下一页。</summary>
    private void MaybeLoadMore()
    {
        if (_page is not (PageKind.Dynamic or PageKind.Static))
            return;

        var scroll = GetCardsScroll();
        if (scroll == null)
            return;

        if (scroll.ScrollableHeight <= 0 ||
            scroll.VerticalOffset + scroll.ViewportHeight >= scroll.ScrollableHeight - 400)
        {
            _ = LoadNextOnlinePageAsync();
        }
    }

    /// <summary>把在线分页缓存注册到壁纸库（收藏持久化、轮换列表均可覆盖在线壁纸），
    /// 并把库中的收藏状态回写到在线列表项（动态/静态页星标显示）。</summary>
    private void MergeOnline()
    {
        _library.SetExternalSource(_online.GetAllCachedItems());
        _library.Load();
        SyncOnlineFavorites();
    }

    /// <summary>在线服务返回的列表项与库内对象是不同实例，收藏状态需按收藏键同步。</summary>
    private void SyncOnlineFavorites()
    {
        var favorites = _library.Items
            .Where(x => x.IsFavorite)
            .Select(WallpaperLibrary.KeyOf)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _online.GetAllCachedItems())
            item.IsFavorite = favorites.Contains(WallpaperLibrary.KeyOf(item));
    }

    // ---------- 列表与筛选 ----------

    private void RefreshCategoryChips()
    {
        CategoryChips.Children.Clear();

        // 「我的」页分类在左侧竖排（MineNavPanel），顶部不生成胶囊。
        if (_page == PageKind.Mine)
            return;

        // 动态/静态页：Pexels 浏览分类（精选 = curated 接口）
        if (_page is PageKind.Dynamic or PageKind.Static)
        {
            var current = CurrentOnlineCategory;
            foreach (var category in OnlineWallpaperService.Categories)
                AddChip(category.Name, category.Name, isChecked: category.Name == current);
        }
    }

    private void AddChip(string label, string? tag, bool isChecked = false)
    {
        var chip = new RadioButton
        {
            Content = label,
            GroupName = "CategoryChips",
            Tag = tag,
            IsChecked = isChecked,
            Style = (Style)FindResource("FilterChip")
        };
        chip.Checked += OnCategoryChipChecked;
        CategoryChips.Children.Add(chip);
    }

    private void RefreshCards()
    {
        _items.Clear();

        var keyword = SearchBox.Text.Trim();
        var page = _page;

        IEnumerable<WallpaperItem> query = page switch
        {
            PageKind.Dynamic => FilterOnline(_online.GetCachedItems(WallpaperType.Video, _dynamicCategory), keyword),
            PageKind.Static => FilterOnline(_online.GetCachedItems(WallpaperType.Image, _staticCategory), keyword),
            PageKind.Mine => FilterMine(keyword),
            _ => Enumerable.Empty<WallpaperItem>()
        };

        foreach (var item in query)
            _items.Add(new WallpaperItemViewModel(item, _library, _config));

        EmptyText.Text = page switch
        {
            PageKind.Dynamic => OnlineEmptyText("动态"),
            PageKind.Static => OnlineEmptyText("静态"),
            PageKind.Mine => _mineFilter == MineFavoriteFilter
                ? "还没有收藏的壁纸 —— 将鼠标移到卡片上，点击右上角 ★ 收藏喜欢的壁纸"
                : "还没有上传的壁纸 —— 点击左侧「导入图片 / 导入视频」添加",
            _ => "壁纸库为空"
        };

        if (_page == PageKind.Mine)
            UpdateMineCounts();
    }

    /// <summary>刷新「我的」页左侧导航的数量徽标。</summary>
    private void UpdateMineCounts()
    {
        var items = _library.Items;
        MineFavCountText.Text = items.Count(x => x.IsFavorite).ToString();
        MineUploadCountText.Text = items.Count(x => !x.IsOnline).ToString();
    }

    /// <summary>在线内容按关键词筛选（分类由浏览分类决定，无需再过滤）。</summary>
    private IEnumerable<WallpaperItem> FilterOnline(IEnumerable<WallpaperItem> items, string keyword)
        => FilterByKeyword(items, keyword);

    private IEnumerable<WallpaperItem> FilterMine(string keyword)
    {
        var all = _library.Search(keyword, null, null);
        return _mineFilter == MineFavoriteFilter
            ? all.Where(x => x.IsFavorite)
            : all.Where(x => !x.IsOnline);
    }

    private static IEnumerable<WallpaperItem> FilterByKeyword(IEnumerable<WallpaperItem> items, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return items;

        return items.Where(x =>
            (x.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
            Path.GetFileName(x.Path).Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private string OnlineEmptyText(string kind)
    {
        if (_items.Count > 0)
            return string.Empty;

        if (_loadingOnline)
            return $"正在加载「{CurrentOnlineCategory}」的{kind}壁纸…";
        if (_onlineFailed)
            return $"在线{kind}壁纸加载失败，请检查网络连接后点击「刷新」重试";
        if (_page is PageKind.Dynamic or PageKind.Static &&
            _online.IsExhausted(CurrentOnlineType, CurrentOnlineCategory))
            return $"「{CurrentOnlineCategory}」分类的内容已全部加载";
        return $"暂无{kind}壁纸 —— 向下滚动自动加载更多";
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            RefreshCards();
    }

    private void OnCategoryChipChecked(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        if (_page is PageKind.Dynamic or PageKind.Static &&
            sender is RadioButton { Tag: string tag })
        {
            if (_page == PageKind.Dynamic)
                _dynamicCategory = tag;
            else
                _staticCategory = tag;
        }

        RefreshCards();
        _ = EnsureOnlinePageAsync();
    }

    private void OnMineNavChecked(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        if (sender is RadioButton { Tag: string tag } &&
            (tag == MineFavoriteFilter || tag == MineUploadFilter))
        {
            _mineFilter = tag;
        }

        if (_page == PageKind.Mine)
            RefreshCards();
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        _onlineFailed = false; // 手动刷新允许重试失败的分页
        MergeOnline();
        RefreshCategoryChips();
        RefreshCards();
        _ = EnsureOnlinePageAsync();
    }

    // ---------- 导入 ----------

    private void OnImportImages(object sender, RoutedEventArgs e) => ImportFiles(images: true);

    private void OnImportVideo(object sender, RoutedEventArgs e) => ImportFiles(images: false);

    private void ImportFiles(bool images)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            CheckFileExists = true,
            Title = images ? "选择图片导入壁纸库" : "选择视频导入壁纸库",
            Filter = images
                ? "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|所有文件|*.*"
                : "视频文件|*.mp4;*.webm;*.mkv;*.mov;*.avi|所有文件|*.*"
        };

        if (dialog.ShowDialog(this) != true || dialog.FileNames.Length == 0)
            return;

        var imported = _library.ImportFiles(dialog.FileNames);

        RefreshCategoryChips();
        RefreshCards();
        ConfigService.Save(_config);

        MessageBox.Show(this,
            imported > 0 ? $"已导入 {imported} 个文件到自有壁纸库。" : "没有可导入的文件，或这些文件已在壁纸库中。",
            "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------- 卡片交互 ----------

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (!_uiReady)
            return;

        // 点击卡片打开大图/视频预览，预览内也可设为壁纸
        if (sender is FrameworkElement element && element.DataContext is WallpaperItemViewModel vm)
            OpenPreview(vm.Item);
    }

    /// <summary>
    /// 打开预览：预览窗立即弹出；未下载的在线壁纸在预览窗内显示加载遮罩并后台下载，
    /// 下载完成后自动展示内容（不阻塞主窗口）。
    /// </summary>
    private void OpenPreview(WallpaperItem item)
    {
        try
        {
            var preview = new PreviewWindow(item, _online.EnsureDownloadedAsync) { Owner = this };
            preview.ShowDialog();

            // 下载结果已在服务内持久化到分页缓存，这里同步一次库内收藏状态
            MergeOnline();

            if (preview.ApplyRequested)
                _ = ApplyItemAsync(item);
        }
        catch (Exception ex)
        {
            ShowToast("打开预览失败：" + ex.Message, "\uE783");
        }
    }

    private void OnApplyFromCard(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is WallpaperItemViewModel vm)
            _ = ApplyItemAsync(vm.Item);
    }

    /// <summary>
    /// 应用壁纸：在线壁纸先按需下载完整文件，再「转正」到正式壁纸库（设为壁纸才会保存），
    /// 然后走通用应用管线（图片=系统原生 API / 视频=WebView2 窗口）。
    /// </summary>
    private async Task ApplyItemAsync(WallpaperItem item)
    {
        ShowToast("正在设置壁纸…", "\uE823", autoHideMs: 0);
        await Task.Delay(300);

        try
        {
            var target = item;

            if (item.IsOnline && !string.IsNullOrWhiteSpace(item.SourceUrl))
            {
                // 完整文件未下载时先下载（视频可达几十 MB，给独立提示）
                if (string.IsNullOrEmpty(item.Path) || !File.Exists(item.Path))
                {
                    ShowToast("正在下载壁纸文件…", "\uE823", autoHideMs: 0);
                    var downloaded = await _online.EnsureDownloadedAsync(item).ConfigureAwait(true);
                    if (!downloaded)
                        throw new InvalidOperationException("壁纸文件下载失败，请检查网络后重试");
                }

                ShowToast("正在保存到正式壁纸库…", "\uE823", autoHideMs: 0);
                await Task.Delay(200);

                var promoted = _online.PromoteToLibrary(item, _library.LibraryFolder);
                if (promoted == null)
                    throw new InvalidOperationException("文件保存到壁纸库失败（可能被其他程序占用）");

                // 转正后重新加载壁纸库（转正副本只保存文件，不进「我的壁纸」列表，
                // 该列表仅由用户手动导入构成），并把收藏状态同步过去（已在收藏中则不重复切换）
                _library.Load();
                var libItem = _library.Items.FirstOrDefault(x =>
                    string.Equals(x.Path, promoted.Path, StringComparison.OrdinalIgnoreCase));
                if (promoted.IsFavorite && libItem is { IsFavorite: false })
                    _library.ToggleFavorite(WallpaperLibrary.KeyOf(item));

                MergeOnline();
                target = promoted;
                RefreshCards();
            }

            _manager.Apply(target);

            var name = target.Title is { Length: > 0 } t ? t : Path.GetFileName(target.Path);
            ShowToast($"设置成功：{name}", "\uE73E");
        }
        catch (Exception ex)
        {
            ShowToast("设置失败：" + ex.Message, "\uE783");
        }
    }

    // ---------- Toast 提示 ----------

    private DispatcherTimer? _toastTimer;

    /// <summary>显示底部居中的轻提示；autoHideMs<=0 表示保持显示直到下一次调用。</summary>
    private void ShowToast(string message, string glyph = "\uE73E", int autoHideMs = 2500)
    {
        ToastText.Text = message;
        ToastIcon.Text = glyph;
        ToastHost.Visibility = Visibility.Visible;

        _toastTimer?.Stop();
        if (autoHideMs > 0)
        {
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(autoHideMs) };
            _toastTimer.Tick += OnToastTimerTick;
            _toastTimer.Start();
        }
    }

    private void OnToastTimerTick(object? sender, EventArgs e)
    {
        ((DispatcherTimer)sender!).Stop();
        ToastHost.Visibility = Visibility.Collapsed;
    }

    /// <summary>隐藏常驻的 Toast 提示（如加载完成/取消）。</summary>
    private void HideToast()
    {
        _toastTimer?.Stop();
        ToastHost.Visibility = Visibility.Collapsed;
    }

    private void OnFavClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is WallpaperItemViewModel vm)
        {
            vm.ToggleFavorite();

            // 「我的」页面里取消收藏后立即移出列表
            if (_page == PageKind.Mine && _mineFilter == MineFavoriteFilter)
                RefreshCards();
        }
    }

    /// <summary>
    /// 右键菜单只在「我的」页（我的壁纸/我的收藏）可用；
    /// 动态/静态等在线壁纸卡片不弹出任何右键菜单。
    /// </summary>
    private void OnCardContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (_page != PageKind.Mine)
            e.Handled = true;
    }

    private void OnDeleteImported(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            menuItem.Parent is not ContextMenu menu ||
            menu.PlacementTarget is not FrameworkElement target ||
            target.DataContext is not WallpaperItemViewModel vm ||
            !vm.IsImported)
            return;

        if (!_library.RemoveImportedItem(vm.Item.Path))
            return;

        RefreshCategoryChips();
        RefreshCards();
    }

    // ---------- 设置页 ----------

    private bool _syncingSettings;

    private void SyncSettingsUi()
    {
        _syncingSettings = true;
        try
        {
            AutoStartSwitch.IsChecked = AutoStart.IsEnabled();
            TraySwitch.IsChecked = _config.MinimizeToTray; 
            RotationRunSwitch.IsChecked = _manager.IsRotating;
            IntervalBox.Text = Math.Max(5, _config.RotationIntervalSeconds).ToString();
            LibraryPathText.Text = _library.LibraryFolder;
            CacheSizeText.Text = _online.GetCacheSizeText();
            PexelsKeyBox.Text = _config.PexelsApiKey ?? string.Empty;
        }
        finally
        {
            _syncingSettings = false;
        }
    }

    private void OnAutoStartToggled(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        AutoStart.SetEnabled(AutoStartSwitch.IsChecked == true);
    }

    private void OnTrayToggled(object sender, RoutedEventArgs e)
    {
        if (!_uiReady)
            return;

        _config.MinimizeToTray = TraySwitch.IsChecked == true;
        ConfigService.Save(_config);
    }

    /// <summary>
    /// 设置页「开启轮换」开关：唯一轮换入口。
    /// 打开 = 自动保存状态并立即开始轮换；关闭 = 暂停并保存状态。
    /// 下次启动按保存的状态自动恢复（首次安装默认 false，不自动轮换）。
    /// </summary>
    private void OnRotationRunToggled(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _syncingSettings)
            return;

        if (RotationRunSwitch.IsChecked == true)
        {
            StartRotation();
        }
        else
        {
            _config.AutoStartRotation = false;
            _manager.StopRotation();
            ConfigService.Save(_config);
        }
    }

    /// <summary>
    /// 设置页「Pexels API Key」保存：清空分页缓存，当前分类立即重新加载第 1 页。
    /// </summary>
    private void OnSavePexelsConfig(object sender, RoutedEventArgs e)
    {
        _config.PexelsApiKey = PexelsKeyBox.Text.Trim();
        ConfigService.Save(_config);
        _online.ResetPexels();
        _onlineFailed = false;

        MergeOnline();
        RefreshCards();

        _ = EnsureOnlinePageAsync();

        MessageBox.Show(this,
            string.IsNullOrWhiteSpace(_config.PexelsApiKey)
                ? "API Key 已清空，在线壁纸不可用；填入 Key 后保存即可恢复。"
                : "已保存 Pexels 配置，正在重新加载在线内容。",
            "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnChangeLibraryFolder(object sender, RoutedEventArgs e)
    {
        // WPF 内置的文件夹选择对话框（.NET 8+，替代 WinForms FolderBrowserDialog）
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择图库存储地址（导入的图片/视频将保存到这里）"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        _config.LibraryFolderPath = dialog.FolderName;
        ConfigService.Save(_config);

        _library.Load();
        RefreshCategoryChips();
        RefreshCards();
        LibraryPathText.Text = _library.LibraryFolder;

        MessageBox.Show(this,
            "图库存储地址已更新，新导入的文件将保存到新位置。",
            "设置完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnOpenLibraryFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = _library.LibraryFolder;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch
        {
            // 打开失败不阻断程序运行
        }
    }

    private void OnClearCache(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this,
            "将清理「在线壁纸预览缓存」目录中的临时下载文件。\n\n" +
            "正式图库（本地导入 / 已设为壁纸保存的文件）不会被清理，\n" +
            "当前正在使用的壁纸也会自动保留。\n\n确定清理吗？",
            "清理缓存", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK)
            return;

        // 保护当前正在使用的壁纸文件与上次壁纸记忆路径（可能仍是临时缓存中的在线壁纸）
        var (count, bytes) = _online.ClearCache(_manager.CurrentItem?.Path, _config.LastWallpaperPath);
        var (thumbCount, thumbBytes) = RemoteThumbCache.ClearAll();
        count += thumbCount;
        bytes += thumbBytes;

        CacheSizeText.Text = _online.GetCacheSizeText();

        ShowToast(count > 0
            ? $"已清理 {count} 个缓存文件，释放 {bytes / 1024.0 / 1024:0.0} MB"
            : "缓存已经是空的，无需清理", "\uE73E");
    }

    // ---------- 轮换（设置页配置，托盘保留「下一张」） ----------

    private void OnNextAction() => _manager.Next();

    private static int ParseInterval(string text, int fallback)
    {
        return int.TryParse(text.Trim(), out var value) && value >= 5
            ? Math.Min(value, 86400)
            : Math.Max(5, fallback);
    }

    private void StartRotation()
    {
        _config.RotationIntervalSeconds = ParseInterval(IntervalBox.Text, _config.RotationIntervalSeconds);
        IntervalBox.Text = _config.RotationIntervalSeconds.ToString();
        _config.AutoStartRotation = true;
        _manager.StartRotation();
        ConfigService.Save(_config);
    }

    private void OnIntervalLostFocus(object sender, RoutedEventArgs e) => ApplyIntervalChange();

    private void OnIntervalKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ApplyIntervalChange();
    }

    /// <summary>设置页修改轮换间隔：即时持久化；若轮换进行中则按新间隔重启计时器。</summary>
    private void ApplyIntervalChange()
    {
        if (!_uiReady)
            return;

        var value = ParseInterval(IntervalBox.Text, _config.RotationIntervalSeconds);
        IntervalBox.Text = value.ToString();
        _config.RotationIntervalSeconds = value;
        ConfigService.Save(_config);

        if (_config.AutoStartRotation)
            _manager.StartRotation();
    }

    // ---------- 轮换范围选择（一级范围下拉 + 二级分类下拉） ----------

    private bool _rotationUiReady;

    private void SetupRotationControls()
    {
        if (_rotationUiReady)
            return;

        void AddItem(System.Windows.Controls.ComboBox combo, string label, string tag)
            => combo.Items.Add(new ComboBoxItem { Content = label, Tag = tag });

        AddItem(RotationScopeCombo, "动态壁纸", "dynamic");
        AddItem(RotationScopeCombo, "静态壁纸", "static");
        AddItem(RotationScopeCombo, "我的收藏", "favorites");
        AddItem(RotationScopeCombo, "我的壁纸", "uploads");

        var scopeIndex = -1;
        for (var i = 0; i < RotationScopeCombo.Items.Count; i++)
        {
            if (RotationScopeCombo.Items[i] is ComboBoxItem { Tag: string t } &&
                string.Equals(t, _config.RotationScope, StringComparison.Ordinal))
            {
                scopeIndex = i;
                break;
            }
        }
        RotationScopeCombo.SelectedIndex = scopeIndex < 0 ? 0 : scopeIndex;

        _rotationUiReady = true;
        RefreshRotationCategoryCombo();

        // 初始选中项设置完成后再挂事件，避免初始化时误触发保存
        RotationScopeCombo.SelectionChanged += OnRotationScopeChanged;
        RotationCategoryCombo.SelectionChanged += OnRotationCategoryChanged;
    }

    /// <summary>二级分类下拉：动态/静态壁纸为「全部 + 7 个一级分类」；我的收藏/我的壁纸隐藏（无二级分类）。</summary>
    private void RefreshRotationCategoryCombo()
    {
        var isMedia = _config.RotationScope is "dynamic" or "static";
        RotationCategoryLabel.Visibility = isMedia ? Visibility.Visible : Visibility.Collapsed;
        RotationCategoryCombo.Visibility = isMedia ? Visibility.Visible : Visibility.Collapsed;

        RotationCategoryCombo.Items.Clear();
        if (!isMedia)
            return;

        void AddItem(string label, string tag)
            => RotationCategoryCombo.Items.Add(new ComboBoxItem { Content = label, Tag = tag });

        AddItem("全部", "");
        foreach (var category in OnlineWallpaperService.Categories)
            AddItem(category.Name, category.Name);

        var current = _config.RotationCategory ?? "";
        var index = 0;
        for (var i = 0; i < RotationCategoryCombo.Items.Count; i++)
        {
            if (RotationCategoryCombo.Items[i] is ComboBoxItem { Tag: string t } &&
                string.Equals(t, current, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }
        RotationCategoryCombo.SelectedIndex = index;
    }

    private void OnRotationScopeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || !_rotationUiReady ||
            RotationScopeCombo.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;

        _config.RotationScope = tag;
        _manager.RotationScope = tag;

        // 切换一级范围后重置二级分类为「全部」
        _config.RotationCategory = null;
        _manager.RotationCategory = null;
        ConfigService.Save(_config);

        RefreshRotationCategoryCombo();
    }

    private void OnRotationCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || !_rotationUiReady ||
            RotationCategoryCombo.SelectedItem is not ComboBoxItem { Tag: string tag })
            return;

        _config.RotationCategory = tag.Length == 0 ? null : tag;
        _manager.RotationCategory = _config.RotationCategory;
        ConfigService.Save(_config);
    }

    // ---------- 窗口控制（关闭 = 最小化到托盘） ----------

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _config.MinimizeToTray)
            Hide();

        // 最大化时取消圆角/边距/投影，铺满屏幕；还原时恢复椭圆化外观
        var maximized = WindowState == WindowState.Maximized;
        WindowRoot.Margin = maximized ? new Thickness(0) : new Thickness(10);
        WindowRoot.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(14);
        WindowRoot.BorderThickness = maximized ? new Thickness(0) : new Thickness(1.4);
        ChromeTopBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(12, 12, 0, 0);
    }

    private void OnShowMain()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    /// <summary>点 X = 最小化到托盘（程序与壁纸继续后台运行）；托盘「退出」才真正退出。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_isExiting)
        {
            e.Cancel = true;
            Hide();

            if (!_hasShownTrayHint)
            {
                _hasShownTrayHint = true;
                _tray.ShowBalloon("动态壁纸仍在运行",
                    "主窗体已最小化到系统托盘，壁纸继续生效；右键托盘图标可退出程序。");
            }

            return;
        }

        base.OnClosing(e);
    }

    private void OnExit()
    {
        if (_isExiting)
            return;

        _isExiting = true;

        try
        {
            _manager.Dispose();
        }
        finally
        {
            _tray.Dispose();
            ConfigService.Save(_config);
        }

        Application.Current.Shutdown();
    }
}
