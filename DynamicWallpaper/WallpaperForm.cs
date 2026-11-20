using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WpfGrid = System.Windows.Controls.Grid;

namespace DynamicWallpaper;

/// <summary>
/// 视频壁纸宿主窗口（纯 WPF Window + WebView2）。
/// Win11 raised-desktop 下，挂到 Progman 的 layered 子窗口必须由「自带 DWM 合成器」的
/// 引擎渲染内容（微软官方：DX blt presents）。WPF/WinForms 自绘内容走 redirection surface，
/// 在该桌面合成树下不被合成；WebView2（Chromium DirectComposition）经验证可行
/// （参见 WebDesk 在 Win11 25H2 build 26200 上的实现）。
/// 注意：图片壁纸不走本窗口，直接用系统原生 SystemParametersInfo API 设置。
/// </summary>
public partial class WallpaperForm : Window
{
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;
    private const uint SMTO_NORMAL = 0x0000;

    private const int WM_SIZE = 0x0005;
    private const int SIZE_MINIMIZED = 0x0001;
    private const int WM_WINDOWPOSCHANGING = 0x0046;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

    private const int GWL_EXSTYLE = -20;
    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;
    private const int WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x00000002;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter,
        string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg,
        IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    private static long GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex).ToInt64()
            : GetWindowLong32(hWnd, nIndex);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static long SetWindowLongPtr(IntPtr hWnd, int nIndex, long dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, new IntPtr(dwNewLong)).ToInt64()
            : SetWindowLong32(hWnd, nIndex, (int)dwNewLong);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey,
        byte bAlpha, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr HWnd;
        public IntPtr HWndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private readonly WpfGrid _grid;
    private readonly WebView2 _webView;

    private readonly DispatcherTimer _attachTimer;
    private readonly DispatcherTimer _zOrderWatchdog;
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _wallpaperWorkerW = IntPtr.Zero;
    private IntPtr _zOrderAfter = HWND_BOTTOM;
    private bool _attached;
    private int _attachAttempts;
    private bool _suppressRestore;

    private bool _webReady;
    private string? _pendingNavigation;

    /// <summary>主屏物理像素边界（左上角原点），与 WinForms Screen.PrimaryScreen.Bounds 等价。</summary>
    private readonly record struct NativeRect(int X, int Y, int Width, int Height);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>获取主屏物理像素尺寸（必须用物理像素：壁纸窗口通过 Win32 SetWindowPos 铺满屏幕）。</summary>
    private static NativeRect GetPrimaryScreenBounds()
    {
        var width = GetSystemMetrics(SM_CXSCREEN);
        var height = GetSystemMetrics(SM_CYSCREEN);
        return new NativeRect(0, 0,
            width > 0 ? width : 1920,
            height > 0 ? height : 1080);
    }

    public WallpaperForm()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = false;
        Background = System.Windows.Media.Brushes.Black;

        var bounds = GetPrimaryScreenBounds();
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        _grid = new WpfGrid { Background = System.Windows.Media.Brushes.Black };

        // WebView2：内容由 Chromium 通过 DirectComposition 合成，
        // 是 Win11 raised-desktop 下验证可行的渲染路径。
        _webView = new WebView2
        {
            CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(
                    Config.ConfigService.GetAppDataFolder(), "WebView2")
            }
        };
        _grid.Children.Add(_webView);
        Content = _grid;

        // WebView2 惰性初始化：只在真正播放视频壁纸时才拉起 Chromium 浏览器进程，
        // 纯图片壁纸用户全程不产生 WebView2 内存开销（约省 100~150MB）。

        _attachTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _attachTimer.Tick += OnAttachTimerTick;

        _zOrderWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        _zOrderWatchdog.Tick += (_, _) => ReassertZOrder();

        Closed += (_, _) =>
        {
            _attachTimer.Stop();
            _zOrderWatchdog.Stop();
            DetachFromDesktop();
            StopContent();
        };
    }

    private bool _initStarted;

    /// <summary>确保 WebView2 运行时已初始化（幂等：只启动一次）。</summary>
    private void EnsureWebViewInitialized()
    {
        if (_initStarted)
            return;

        _initStarted = true;
        _ = InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async();

            var settings = _webView.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;

            _webReady = true;
            if (_pendingNavigation != null)
            {
                var pending = _pendingNavigation;
                _pendingNavigation = null;
                NavigateTo(pending);
            }
        }
        catch
        {
            
            AttachStatus = "WebView2 初始化失败：请确认系统已安装 Microsoft WebView2 运行时";
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // Show() 完全结束、内容已渲染后再 SetParent，避免可见性状态错乱。
        AttachToDesktop();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // 任何试图改变 z-order 的操作都强制钉在图标层之后。
        if (_attached && !_suppressRestore && msg == WM_WINDOWPOSCHANGING)
        {
            var pos = Marshal.PtrToStructure<WindowPos>(lParam);
            if (pos.HWnd == hwnd && pos.HWndInsertAfter != _zOrderAfter)
            {
                pos.HWndInsertAfter = _zOrderAfter;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }

        // Win+D / 显示桌面不应把壁纸窗口当普通窗口最小化。
        if (_attached && !_suppressRestore &&
            msg == WM_SIZE && wParam == (IntPtr)SIZE_MINIMIZED)
        {
            RestoreDesktopWindow();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void RestoreDesktopWindow()
    {
        _suppressRestore = true;
        try
        {
            if (_hwnd != IntPtr.Zero)
            {
                ShowWindow(_hwnd, SW_RESTORE);
                ShowWindow(_hwnd, SW_SHOW);
            }
        }
        finally
        {
            _suppressRestore = false;
        }
    }

    /// <summary>周期性重新声明 z-order，兜住 shell 重排桌面层级的情况。</summary>
    private void ReassertZOrder()
    {
        if (!_attached || _hwnd == IntPtr.Zero || !IsWindow(_hwnd) || _suppressRestore)
            return;

        if (_wallpaperWorkerW == IntPtr.Zero || !IsWindow(_wallpaperWorkerW) ||
            GetParent(_hwnd) != _wallpaperWorkerW)
            return;

        _suppressRestore = true;
        try
        {
            SetWindowPos(_hwnd, _zOrderAfter, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }
        finally
        {
            _suppressRestore = false;
        }
    }

    public bool AttachToDesktop()
    {
        if (_attached)
            return true;
        if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd))
            return false;

        if (AttachCore())
            return true;

        if (_attachAttempts == 0)
        {
            _attachAttempts = 1;
            _attachTimer.Start();
        }

        return false;
    }

    private void OnAttachTimerTick(object? sender, EventArgs e)
    {
        if (AttachCore() || ++_attachAttempts >= 25)
        {
            _attachTimer.Stop();
            _attachAttempts = 0;
        }
    }

    private sealed record HostCandidate(IntPtr Parent, IntPtr InsertAfter, string Description,
        bool UseLayered = false);

    private bool AttachCore()
    {
        var hosts = FindWallpaperHosts();
        if (hosts.Count == 0)
        {
            ShowAsStandalone("未找到桌面壁纸宿主，已显示普通全屏窗口");
            return false;
        }

        foreach (var host in hosts)
        {
            if (host.Parent == IntPtr.Zero || !IsWindow(host.Parent))
                continue;

            if (!host.UseLayered && !CoversPrimaryScreen(host.Parent))
                continue;

            // Win11 raised-desktop 关键：在 SetParent 之前设置 WS_EX_LAYERED + alpha=255。
            if (host.UseLayered)
            {
                var style = GetWindowLongPtr(_hwnd, GWL_STYLE);
                SetWindowLongPtr(_hwnd, GWL_STYLE, style | WS_CHILD);

                var exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
                SetWindowLongPtr(_hwnd, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);
                SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
            }

            SetParent(_hwnd, host.Parent);

            var bounds = GetPrimaryScreenBounds();
            SetWindowPos(_hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                SWP_NOZORDER | SWP_NOACTIVATE);

            _zOrderAfter = host.InsertAfter != IntPtr.Zero ? host.InsertAfter : HWND_BOTTOM;
            SetWindowPos(_hwnd, _zOrderAfter, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);

            ShowWindow(_hwnd, SW_SHOW);

            SetWindowPos(_hwnd, _zOrderAfter, 0, 0, 0, 0,
                SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);

            _wallpaperWorkerW = host.Parent;
            _attached = true;
            _zOrderWatchdog.Start();
            AttachStatus = "已嵌入桌面：" + host.Description
                + "（父窗口 " + DescribeWindow(host.Parent)
                + "，z序=" + (_zOrderAfter == HWND_BOTTOM ? "底" : "图标后")
                + "，WebView2合成）";
            return true;
        }

        ShowAsStandalone("桌面宿主均不可用，已显示普通全屏窗口");
        return false;
    }

    private static bool CoversPrimaryScreen(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
            return false;

        var screen = GetPrimaryScreenBounds();
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        return width >= screen.Width * 0.9 && height >= screen.Height * 0.9;
    }

    private static string DescribeWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "无";

        var sb = new StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        GetWindowRect(hwnd, out var r);
        return $"{sb} {r.Right - r.Left}x{r.Bottom - r.Top}";
    }

    private void ShowAsStandalone(string status)
    {
        SetParent(_hwnd, IntPtr.Zero);
        var b = GetPrimaryScreenBounds();
        SetWindowPos(_hwnd, IntPtr.Zero, b.X, b.Y, b.Width, b.Height,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
        ShowWindow(_hwnd, SW_SHOW);
        _wallpaperWorkerW = IntPtr.Zero;
        _attached = false;
        _zOrderWatchdog.Stop();
        AttachStatus = status;
    }

    public string AttachStatus { get; set; } = "尚未挂载";

    public void DetachFromDesktop()
    {
        _attachTimer.Stop();
        _zOrderWatchdog.Stop();
        _attachAttempts = 0;

        if (_hwnd != IntPtr.Zero && IsWindow(_hwnd))
            SetParent(_hwnd, IntPtr.Zero);

        _attached = false;
        _wallpaperWorkerW = IntPtr.Zero;
        AttachStatus = "已从桌面分离";
    }

    private static List<HostCandidate> FindWallpaperHosts()
    {
        var hosts = new List<HostCandidate>();
        var progman = FindWindow("Progman", null);

        if (progman != IntPtr.Zero)
        {
            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero,
                SMTO_NORMAL, 1000, out _);
            SendMessageTimeout(progman, 0x052C, (IntPtr)0xD, (IntPtr)0x1,
                SMTO_NORMAL, 1000, out _);
        }

        IntPtr defView = IntPtr.Zero;
        IntPtr defViewOwner = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            var view = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero)
            {
                defView = view;
                defViewOwner = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        bool raisedDesktop = progman != IntPtr.Zero &&
            (GetWindowLongPtr(progman, GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP) != 0;

        // Win11 raised-desktop：layered 子窗口挂 Progman、z序紧跟 DefView 之后。
        if (raisedDesktop && defViewOwner == progman && defView != IntPtr.Zero)
        {
            hosts.Add(new HostCandidate(progman, defView, "Progman(RaisedDesktop/分层)", UseLayered: true));
            return hosts;
        }

        // 经典模式：挂到图标层之后的顶层 WorkerW。
        if (defViewOwner != IntPtr.Zero)
        {
            var sibling = FindWindowEx(IntPtr.Zero, defViewOwner, "WorkerW", null);
            if (sibling != IntPtr.Zero && !HasDefView(sibling))
                hosts.Add(new HostCandidate(sibling, IntPtr.Zero, "顶层 WorkerW(图标层之后)"));
        }

        EnumWindows((hwnd, _) =>
        {
            if (!IsClass(hwnd, "WorkerW") || HasDefView(hwnd))
                return true;
            hosts.Add(new HostCandidate(hwnd, IntPtr.Zero, "顶层 WorkerW(兜底)"));
            return false;
        }, IntPtr.Zero);

        return hosts;
    }

    private static bool HasDefView(IntPtr hwnd)
    {
        return FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero;
    }

    private static bool IsClass(IntPtr hwnd, string className)
    {
        var sb = new StringBuilder(64);
        GetClassName(hwnd, sb, sb.Capacity);
        return string.Equals(sb.ToString(), className, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>在壁纸窗口中播放视频（HTML5 video，静音循环铺满）。</summary>
    public void SetVideoWallpaper(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        EnsureWebViewInitialized();
        ShowContent(BuildVideoHtml(filePath));
    }

    /// <summary>停止当前内容显示（导航到空白页，终止视频播放）。</summary>
    public void StopContent()
    {
        if (_webReady)
            NavigateTo("about:blank");
        else
            _pendingNavigation = null;
    }

    private static string BuildVideoHtml(string filePath)
    {
        var uri = new Uri(filePath).AbsoluteUri;
        return $$"""
            <!DOCTYPE html><html><head><meta charset="utf-8"><style>
            html,body{margin:0;padding:0;width:100%;height:100%;background:#000;overflow:hidden}
            video{width:100%;height:100%;object-fit:cover;display:block}
            </style></head><body>
            <video id="v" src="{{uri}}" autoplay loop muted playsinline></video>
            <script>
            var v=document.getElementById('v');
            if (v.play) v.play().catch(function(){});
            v.addEventListener('error',function(){document.body.style.background='#200'});
            </script>
            </body></html>
            """;
    }

    private void ShowContent(string html)
    {
        try
        {
            var file = Path.Combine(
                Config.ConfigService.GetAppDataFolder(), "wallpaper.html");
            File.WriteAllText(file, html);
            NavigateTo(file);
        }
        catch
        {
            // 写临时 HTML 失败时忽略本次内容更新
        }
    }

    private void NavigateTo(string target)
    {
        if (!_webReady)
        {
            _pendingNavigation = target;
            return;
        }

        try
        {
            if (target == "about:blank")
                _webView.CoreWebView2.Navigate("about:blank");
            else
                _webView.CoreWebView2.Navigate(new Uri(target).AbsoluteUri);
        }
        catch
        {
            // WebView2 尚未就绪或导航失败，忽略
        }
    }
}
