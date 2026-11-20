using System.IO;
using System.Text;
using DynamicWallpaper.Config;

namespace DynamicWallpaper.Logging;

/// <summary>
/// 轻量文件日志：异常统一写入 %AppData%\DynamicWallpaper\logs\log-yyyyMMdd.txt。
/// 所有写入都有兜底保护，日志失败绝不影响主流程。
/// </summary>
public static class ErrorLogger
{
    private static readonly object Lock = new();
    private static bool _cleanedUp;

    /// <summary>日志保留天数，超过的日志文件自动删除。</summary>
    private const int RetentionDays = 30;

    /// <summary>日志目录（AppData\DynamicWallpaper\logs）。</summary>
    public static string LogFolder { get; } =
        Path.Combine(ConfigService.GetAppDataFolder(), "logs");

    /// <summary>记录异常（含完整堆栈），可附加上下文说明。</summary>
    public static void Log(Exception ex, string? context = null)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context))
            sb.AppendLine($"[上下文] {context}");
        sb.AppendLine($"[类型] {ex.GetType().FullName}");
        sb.AppendLine($"[消息] {ex.Message}");
        sb.AppendLine($"[堆栈] {ex.StackTrace}");
        if (ex.InnerException != null)
            sb.AppendLine($"[内部异常] {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
        Write(sb.ToString());
    }

    /// <summary>记录普通消息。</summary>
    public static void Log(string message)
    {
        Write(message);
    }

    private static void Write(string content)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            var file = Path.Combine(LogFolder, $"log-{DateTime.Now:yyyyMMdd}.txt");

            lock (Lock)
            {
                File.AppendAllText(file,
                    $"""
                    ================ {DateTime.Now:yyyy-MM-dd HH:mm:ss} ================
                    {content}

                    """, Encoding.UTF8);

                CleanupExpiredLogs();
            }
        }
        catch
        {
            // 日志写入失败时静默，绝不影响主流程
        }
    }

    /// <summary>
    /// 自动清理超过 30 天的日志文件（每次写日志时惰性触发，仅第一次实际执行）。
    /// 只删除本程序命名规则的 log-*.txt 文件，不影响目录内其他内容。
    /// </summary>
    private static void CleanupExpiredLogs()
    {
        if (_cleanedUp)
            return;
        _cleanedUp = true;

        try
        {
            var deadline = DateTime.Now.AddDays(-RetentionDays);

            foreach (var file in Directory.EnumerateFiles(LogFolder, "log-*.txt"))
            {
                // 优先按文件名中的日期判断（log-yyyyMMdd.txt），文件名异常时回退到修改时间
                var name = Path.GetFileNameWithoutExtension(file); // log-yyyyMMdd
                if (DateTime.TryParseExact(
                        name.Length > 4 ? name[4..] : name, "yyyyMMdd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var date))
                {
                    if (date < deadline)
                        File.Delete(file);
                }
                else if (File.GetLastWriteTime(file) < deadline)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // 清理失败不影响日志记录
        }
    }
}
