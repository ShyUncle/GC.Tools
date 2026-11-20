using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicWallpaper.Config;

namespace DynamicWallpaper.Library;

/// <summary>
/// 缩略图缓存：按源路径哈希存盘，供 WPF BitmapImage 按文件路径加载。
/// 视频通过 Windows Shell API 提取首帧；提取失败由调用方（ViewModel）显示占位样式。
/// </summary>
public static class ThumbnailCache
{
    private const int SIIGBF_BIGGERSIZEOK = 0x01;

    public static string GetThumbnailPath(AppConfig config, string sourcePath)
    {
        var folder = Path.Combine(
            ConfigService.GetAppDataFolder(),
            config.ThumbnailCacheFolder);

        Directory.CreateDirectory(folder);

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sourcePath)));

        return Path.Combine(folder, hash + ".jpg");
    }

    public static bool TryGetThumbnail(AppConfig config, string sourcePath, out string thumbnailPath)
    {
        thumbnailPath = GetThumbnailPath(config, sourcePath);

        if (File.Exists(thumbnailPath))
        {
            var sourceTime = File.GetLastWriteTimeUtc(sourcePath);
            var thumbTime = File.GetLastWriteTimeUtc(thumbnailPath);

            if (thumbTime >= sourceTime)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 确保视频缩略图已生成并返回缓存文件路径；提取或写入失败返回 null。
    /// </summary>
    public static string? EnsureVideoThumbnailFile(AppConfig config, string sourcePath, int width, int height)
    {
        var cachePath = GetThumbnailPath(config, sourcePath);

        if (TryGetThumbnail(config, sourcePath, out _))
            return File.Exists(cachePath) ? cachePath : null;

        var frame = ExtractVideoThumbnail(sourcePath, width, height);
        if (frame == null)
            return null;

        try
        {
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var stream = File.Create(cachePath);
            encoder.Save(stream);
        }
        catch
        {
            // 缓存写入失败不影响调用方展示占位样式
        }

        return File.Exists(cachePath) ? cachePath : null;
    }

    /// <summary>通过 Windows Shell API 提取视频首帧（已 Freeze，可跨线程使用）。</summary>
    private static BitmapSource? ExtractVideoThumbnail(string sourcePath, int width, int height)
    {
        IntPtr hBitmap = IntPtr.Zero;

        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            var hr = SHCreateItemFromParsingName(sourcePath, IntPtr.Zero, ref iid,
                out IShellItemImageFactory? factory);

            if (hr != 0 || factory == null)
                return null;

            var size = new NativeSize { cx = width, cy = height };
            hr = factory.GetImage(size, SIIGBF_BIGGERSIZEOK, out hBitmap);

            if (hr != 0 || hBitmap == IntPtr.Zero)
                return null;

            // 从 HBITMAP 创建 WPF 位图（内容立即复制进托管缓冲），随后释放 GDI 句柄
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
                DeleteObject(hBitmap);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);
}
