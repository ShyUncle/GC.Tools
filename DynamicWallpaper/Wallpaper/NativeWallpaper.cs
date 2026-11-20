using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DynamicWallpaper.Wallpaper;

/// <summary>
/// 设置桌面壁纸。
/// 实测（2026-11-19 本机）：IDesktopWallpaper COM 类未注册（REGDB_E_CLASSNOTREG，精简版系统常见），
/// 因此使用无需 COM 注册的 SystemParametersInfo(SPI_SETDESKWALLPAPER)——user32 直接导出，
/// 写入 HKCU\Control Panel\Desktop 并广播 WM_SETTINGCHANGE，桌面立即生效。
/// 壁纸显示样式由注册表 WallpaperStyle（10=填充）与 TileWallpaper（0=不平铺）控制。
/// 退出程序时不恢复原壁纸：最后设置的壁纸保留为桌面壁纸。
/// </summary>
internal static class NativeWallpaper
{
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x0001;
    private const uint SPIF_SENDCHANGE = 0x0002;

    /// <summary>把图片设为系统桌面壁纸（填充模式，立即生效）。</summary>
    public static bool SetImageWallpaper(string imagePath)
    {
        try
        {
            // 确保显示样式为「填充」（10）且不平铺。
            using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
            {
                key?.SetValue("WallpaperStyle", "10");
                key?.SetValue("TileWallpaper", "0");
            }

            return SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, imagePath,
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam,
        string? pvParam, uint fWinIni);
}
