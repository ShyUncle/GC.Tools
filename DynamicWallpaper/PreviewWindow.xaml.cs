using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DynamicWallpaper.Config;
using DynamicWallpaper.Logging;
using DynamicWallpaper.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DynamicWallpaper;

/// <summary>
/// 壁纸预览窗口：图片显示大图；视频使用 WebView2（Chromium）播放——
/// 与壁纸播放同一引擎，解码能力一致，保证「能预览的必然能设为壁纸播放」。
/// 窗口内提供「设为壁纸」操作（通过 ApplyRequested 交回主窗口执行转正与应用）。
/// </summary>
public partial class PreviewWindow : Window
{
    public WallpaperItem Item { get; }

    /// <summary>用户在预览中点击了「设为壁纸」。</summary>
    public bool ApplyRequested { get; private set; }

    public PreviewWindow(WallpaperItem item, Func<WallpaperItem, Task<bool>>? downloadAsync = null)
    {
        InitializeComponent();
        Item = item ?? throw new ArgumentNullException(nameof(item));

        TitleText.Text = Item.Title is { Length: > 0 } title
            ? title
            : Path.GetFileName(Item.Path);

        // 尺寸约为主工作区 86%，居中显示
        var work = SystemParameters.WorkArea;
        Width = Math.Min(1280, work.Width * 0.86);
        Height = Math.Min(820, work.Height * 0.88);

        // 在线内容未下载时：预览窗立即显示，下载在窗内进行（加载遮罩），完成后自动展示
        if (downloadAsync != null && Item.IsOnline &&
            (string.IsNullOrEmpty(Item.Path) || !File.Exists(Item.Path)))
        {
            ApplyBtn.IsEnabled = false;
            LoadingOverlay.Visibility = Visibility.Visible;
            _ = PrepareContentAsync(downloadAsync);
        }
        else
        {
            LoadContent();
        }

        Closed += (_, _) => StopVideoPreview();
    }

    /// <summary>按类型加载内容：视频用 WebView2 播放，图片显示大图。</summary>
    private void LoadContent()
    {
        if (Item.Type == WallpaperType.Video)
        {
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewVideoView.Visibility = Visibility.Visible;
            _ = InitVideoPreviewAsync();
        }
        else
        {
            PreviewVideoView.Visibility = Visibility.Collapsed;
            PreviewImage.Source = LoadImage(Item.Path);
        }
    }

    /// <summary>后台下载完整文件，成功后加载内容；失败在遮罩上提示（仍可关闭窗口）。</summary>
    private async Task PrepareContentAsync(Func<WallpaperItem, Task<bool>> downloadAsync)
    {
        var ok = false;
        try
        {
            ok = await downloadAsync(Item).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "预览内容下载");
        }

        if (!ok)
        {
            LoadingGlyph.Text = "\uE783"; // 错误图标
            LoadingGlyph.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE7, 0x83, 0x6A));
            LoadingText.Text = "内容下载失败，请检查网络后重试";
            LoadingBar.Visibility = Visibility.Collapsed;
            return;
        }

        LoadingOverlay.Visibility = Visibility.Collapsed;
        ApplyBtn.IsEnabled = true;
        LoadContent();
    }

    private static BitmapImage? LoadImage(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            // 解码宽度限制在 1920：Pexels 原图可达 5K+，全尺寸解码单张就占 30~80MB 内存
            bitmap.DecodePixelWidth = 1920;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    // ---------- 视频预览（WebView2） ----------

    /// <summary>初始化 WebView2 并加载视频播放页（静音、循环、等比铺满）。</summary>
    private async Task InitVideoPreviewAsync()
    {
        try
        {
            // 与壁纸窗口共用同一 UserDataFolder（共享浏览器进程，启动更快）
            PreviewVideoView.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(ConfigService.GetAppDataFolder(), "WebView2")
            };
            await PreviewVideoView.EnsureCoreWebView2Async();

            var settings = PreviewVideoView.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsZoomControlEnabled = false;

            // 页面里 video 加载失败时通过 postMessage 通知宿主，显示回退文案
            PreviewVideoView.CoreWebView2.WebMessageReceived += OnVideoErrorReceived;

            var file = Path.Combine(ConfigService.GetAppDataFolder(), "preview.html");
            File.WriteAllText(file, BuildVideoHtml(Item.Path));
            PreviewVideoView.CoreWebView2.Navigate(new Uri(file).AbsoluteUri);
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "预览视频 WebView2 初始化");
            ShowVideoFail();
        }
    }

    private static string BuildVideoHtml(string filePath)
    {
        var uri = new Uri(filePath).AbsoluteUri;
        return $$"""
            <!DOCTYPE html><html><head><meta charset="utf-8"><style>
            html,body{margin:0;padding:0;width:100%;height:100%;background:#000;overflow:hidden}
            video{width:100%;height:100%;object-fit:contain;display:block}
            </style></head><body>
            <video id="v" src="{{uri}}" autoplay loop muted playsinline></video>
            <script>
            var v=document.getElementById('v');
            if (v.play) v.play().catch(function(){});
            v.addEventListener('error',function(){
              window.chrome.webview.postMessage('video-error');
            });
            </script>
            </body></html>
            """;
    }

    private void OnVideoErrorReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.TryGetWebMessageAsString() == "video-error")
            ShowVideoFail();
    }

    private void ShowVideoFail()
    {
        PreviewVideoView.Visibility = Visibility.Collapsed;
        VideoFailText.Visibility = Visibility.Visible;
    }

    /// <summary>关闭预览时停止播放并释放 WebView2（终止 Chromium 进程，回收约 100~150MB 内存；
    /// 若壁纸窗口仍在共享同一浏览器进程，则该进程由壁纸窗口继续持有，不受影响）。</summary>
    private void StopVideoPreview()
    {
        try
        {
            if (PreviewVideoView.CoreWebView2 != null)
                PreviewVideoView.CoreWebView2.Navigate("about:blank");
        }
        catch
        {
            // 窗口关闭阶段 WebView2 可能已释放
        }

        try
        {
            PreviewVideoView.Dispose();
        }
        catch
        {
            // 已释放时忽略
        }
    }

    // ---------- 通用交互 ----------

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnApply(object sender, RoutedEventArgs e)
    {
        ApplyRequested = true;
        Close();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }
}
