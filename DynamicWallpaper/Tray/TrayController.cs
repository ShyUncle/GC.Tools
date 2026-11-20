namespace DynamicWallpaper.Tray;

using System.IO;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// 系统托盘控制器：托盘图标与右键菜单均为 Win32 原生实现（Shell_NotifyIcon + TrackPopupMenu），
/// 不依赖 WinForms / WPF 可视树，弹出的可靠性最高。
/// 双击=打开主界面，右键=弹出菜单（整体位置略高于光标），支持气泡提示。
/// </summary>
public class TrayController : IDisposable
{
    // Shell_NotifyIcon
    private const uint NIM_ADD = 0x0;
    private const uint NIM_MODIFY = 0x1;
    private const uint NIM_DELETE = 0x2;

    private const uint NIF_MESSAGE = 0x1;
    private const uint NIF_ICON = 0x2;
    private const uint NIF_TIP = 0x4;
    private const uint NIF_INFO = 0x10;
    private const int NIIF_INFO = 0x1;

    // 托盘消息（WM_APP 区段，避免与框架消息冲突）
    private const int WM_APP_TRAY = 0x8001;

    /// <summary>
    /// Explorer 重启时系统广播 TaskbarCreated：所有托盘图标被移除，
    /// 程序必须收到此消息后重新添加图标，否则进程仍在但托盘消失。
    /// 该消息 ID 由 RegisterWindowMessage 注册，每次登录会话不同，须运行时获取。
    /// </summary>
    private static readonly uint WmTaskbarCreated = RegisterWindowMessageW("TaskbarCreated");

    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;

    private const int IDI_APPLICATION = 32512;

    // TrackPopupMenu 标志：返回所选命令 ID、不发送通知消息、右键也可选择
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_NONOTIFY = 0x0080;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint WM_NULL = 0x0000;

    // 右键菜单命令 ID
    private const int MenuShowMain = 1;
    private const int MenuNext = 2;
    private const int MenuPause = 3;
    private const int MenuResume = 4;
    private const int MenuExit = 5;

    private const string TrayTip = "动态壁纸";

    /// <summary>菜单整体相对鼠标位置的上移偏移（DIP，按 DPI 换算为物理像素）。</summary>
    private const double MenuVerticalOffsetDip = 10;

    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _icon = IntPtr.Zero;
    private IntPtr _menu = IntPtr.Zero;
    private bool _iconVisible;
    private bool _windowCreated;

    private Action? _onShowMain;
    private Action? _onNext;
    private Action? _onPauseRotation;
    private Action? _onResumeRotation;
    private Action? _onExit;

    // 托盘消息窗口单实例，WndProc 通过静态引用分发到当前控制器
    private static TrayController? _current;

    // 静态引用防止委托被 GC 回收（原生代码持有其函数指针）
    private static readonly WndProcDelegate WndProcThunk = WndProc;
    private const string WindowClassName = "DynamicWallpaperTrayWnd";

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    public TrayController()
    {
        _icon = LoadAppIcon();
        _current = this;
        _windowCreated = CreateTrayWindow();

        if (_windowCreated)
            _menu = CreatePopupMenu();
    }

    public void Setup(
        Action onShowMain,
        Action onNext,
        Action onPauseRotation,
        Action onResumeRotation,
        Action onExit)
    {
        _onShowMain = onShowMain;
        _onNext = onNext;
        _onPauseRotation = onPauseRotation;
        _onResumeRotation = onResumeRotation;
        _onExit = onExit;

        if (_iconVisible)
            RemoveTrayIcon();

        AddTrayIcon();
    }

    /// <summary>显示托盘气泡提示（用于告知用户程序已最小化到托盘）。</summary>
    public void ShowBalloon(string title, string text)
    {
        if (!_windowCreated || !_iconVisible)
            return;

        var data = CreateBaseData();
        data.uFlags = NIF_INFO;
        data.szInfo = text ?? string.Empty;
        data.szInfoTitle = title ?? string.Empty;
        data.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_iconVisible)
            RemoveTrayIcon();

        if (_menu != IntPtr.Zero)
        {
            DestroyMenu(_menu);
            _menu = IntPtr.Zero;
        }

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }

        if (_current == this)
            _current = null;
    }

    // ---------- 托盘图标 ----------

    private NOTIFYICONDATA CreateBaseData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uCallbackMessage = WM_APP_TRAY,
            hIcon = _icon,
            szTip = TrayTip
        };
    }

    private void AddTrayIcon()
    {
        if (!_windowCreated || _icon == IntPtr.Zero)
            return;

        var data = CreateBaseData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        if (Shell_NotifyIconW(NIM_ADD, ref data))
            _iconVisible = true;
    }

    private void RemoveTrayIcon()
    {
        if (!_windowCreated)
            return;

        var data = CreateBaseData();
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _iconVisible = false;
    }

    /// <summary>
    /// Explorer 重启后重建托盘图标：立即尝试一次；若失败（Explorer 尚未就绪）
    /// 则后台每秒重试，最多 5 次（Shell_NotifyIcon 可跨线程调用，不占用 UI 线程）。
    /// </summary>
    private void ReaddTrayIconWithRetry()
    {
        if (!_windowCreated)
            return;

        _iconVisible = false;
        AddTrayIcon();
        if (_iconVisible)
            return;

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 5 && !_iconVisible; attempt++)
            {
                try
                {
                    await Task.Delay(1000).ConfigureAwait(false);
                    AddTrayIcon();
                }
                catch
                {
                    // 重试失败继续下一轮
                }
            }
        });
    }

    // ---------- Win32 原生右键菜单 ----------

    /// <summary>在光标位置弹出原生菜单，整体位置略高于光标（上移 MenuVerticalOffsetDip）。</summary>
    private void ShowContextMenu()
    {
        if (_menu == IntPtr.Zero)
            return;

        RebuildMenu();

        GetCursorPos(out var pt);
        var offset = (int)Math.Round(MenuVerticalOffsetDip * GetDpiScale());

        // 托盘菜单的经典要求：先把消息窗口设为前台，
        // 否则菜单弹出后可能因前台窗口不属于本线程而无法关闭/立即消失
        SetForegroundWindow(_hwnd);

        var selected = TrackPopupMenu(
            _menu, TPM_RIGHTBUTTON | TPM_NONOTIFY | TPM_RETURNCMD,
            pt.X, pt.Y - offset, 0, _hwnd, IntPtr.Zero);

        // 恢复前台状态（TrackPopupMenu 文档要求）
        PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

        switch (selected)
        {
            case MenuShowMain:
                _onShowMain?.Invoke();
                break;
            case MenuNext:
                _onNext?.Invoke();
                break;
            case MenuPause:
                _onPauseRotation?.Invoke();
                break;
            case MenuResume:
                _onResumeRotation?.Invoke();
                break;
            case MenuExit:
                _onExit?.Invoke();
                break;
        }
    }

    /// <summary>重建菜单内容（每次弹出前重建，保证「暂停/恢复轮换」等状态文案始终最新）。</summary>
    private void RebuildMenu()
    {
        // 必须用 MF_BYPOSITION(0x0400) 按位置删除；若误用默认的 MF_BYCOMMAND(0x0000)，
        // 位置 0 会被当作命令 ID 查找而删除失败，导致菜单项每次右键不断累加
        const uint mfByPosition = 0x0400;
        while (DeleteMenu(_menu, 0, mfByPosition))
        {
        }

        AppendMenuW(_menu, 0x0000 /* MF_STRING */, (UIntPtr)MenuShowMain, "打开主界面");
        AppendMenuW(_menu, 0x0000, (UIntPtr)MenuNext, "下一张");
        AppendMenuW(_menu, 0x0000, (UIntPtr)MenuPause, "暂停轮换");
        AppendMenuW(_menu, 0x0000, (UIntPtr)MenuResume, "恢复轮换");
        AppendMenuW(_menu, 0x0800 /* MF_SEPARATOR */, UIntPtr.Zero, null);
        AppendMenuW(_menu, 0x0000, (UIntPtr)MenuExit, "退出");
    }

    /// <summary>窗口 DPI 缩放比（用于把 DIP 偏移换算为物理像素）。</summary>
    private double GetDpiScale()
    {
        try
        {
            var dpi = GetDpiForWindow(_hwnd);
            if (dpi > 0)
                return dpi / 96.0;
        }
        catch
        {
            // 低版本系统无此 API，回退默认 100%
        }

        return 1.0;
    }

    private void HandleTrayMessage(int mouseMessage)
    {
        switch (mouseMessage)
        {
            case WM_LBUTTONDBLCLK:
                _onShowMain?.Invoke();
                break;
            case WM_RBUTTONUP:
                ShowContextMenu();
                break;
        }
    }

    /// <summary>托盘图标：从 exe 提取小图标（与应用图标一致），失败回退系统默认图标。</summary>
    private static IntPtr LoadAppIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe)
                && ExtractIconEx(exe, 0, out _, out var hSmall, 1) > 0
                && hSmall != IntPtr.Zero)
            {
                return hSmall;
            }
        }
        catch
        {
            // 提取失败时回退默认图标
        }

        return LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
    }

    // ---------- 消息窗口 ----------

    private bool CreateTrayWindow()
    {
        var wndClass = new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcThunk),
            hInstance = GetModuleHandleW(null),
            lpszClassName = WindowClassName
        };

        RegisterClassW(ref wndClass);

        _hwnd = CreateWindowExW(
            0, WindowClassName, WindowClassName,
            0, // WS_OVERLAPPED：隐藏的顶层窗口，仅用于接收托盘消息
            0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);

        return _hwnd != IntPtr.Zero;
    }

    private static IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (_current == null)
            return DefWindowProcW(hWnd, msg, wParam, lParam);

        if (msg == WM_APP_TRAY)
        {
            _current.HandleTrayMessage(lParam.ToInt32());
            return IntPtr.Zero;
        }

        // Explorer 重启后重新添加托盘图标（图标被系统移除，需 NIM_ADD 重建）
        if (msg == (int)WmTaskbarCreated && WmTaskbarCreated != 0)
        {
            _current.ReaddTrayIconWithRetry();
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    // ---------- Win32 声明 ----------

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(
        [MarshalAs(UnmanagedType.LPWStr)] string lpszFile,
        int nIconIndex,
        out IntPtr phiconLarge,
        out IntPtr phiconSmall,
        int nIcons);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint flags, UIntPtr idNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteMenu(IntPtr hMenu, uint position, uint flags);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(
        IntPtr hMenu, uint flags, int x, int y, int reserved, IntPtr hWnd, IntPtr prcRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);
}
