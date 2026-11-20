using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using DynamicWallpaper.Logging;

namespace DynamicWallpaper;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例保护：壁纸由注册表/桌面窗口全局状态承载，多实例会互相覆盖。
        _singleInstanceMutex = new Mutex(true, @"Local\DynamicWallpaper.SingleInstance", out var createdNew);

        if (!createdNew)
        {
            MessageBox.Show("动态壁纸程序已在运行中（请查看系统托盘）。", "动态壁纸",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // 全局异常兜底：提醒用户 + 写入日志文件，尽量避免程序直接崩溃。
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            ErrorLogger.Log(ex, "主窗口初始化失败");
            MessageBox.Show(
                "程序启动失败，异常信息已写入日志。\n\n" + ex.Message,
                "动态壁纸", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    /// <summary>UI 线程异常：记录日志并提示；标记已处理让程序继续运行（不崩溃）。</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ErrorLogger.Log(e.Exception, "UI 线程未处理异常");
        e.Handled = true;

        var result = MessageBox.Show(
            "程序遇到一个错误，已记录到日志。\n\n" +
            e.Exception.Message + "\n\n" +
            "是否继续运行？（点「取消」将退出程序）\n\n" +
            "日志位置：" + ErrorLogger.LogFolder,
            "动态壁纸", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Cancel)
        {
            e.Handled = false; // 交给默认处理，触发退出
            Shutdown();
        }
    }

    /// <summary>非 UI 线程致命异常：记录日志（此时程序通常无法挽救，仅留档）。</summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ErrorLogger.Log(ex, "非 UI 线程致命异常");
        else
            ErrorLogger.Log("非 UI 线程致命异常：" + e.ExceptionObject);
    }

    /// <summary>后台 Task 未观察异常：记录日志并标记已观察（默认策略下不会崩溃，仅留档）。</summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ErrorLogger.Log(e.Exception, "后台任务未观察异常");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;

        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
