using System.Security.Cryptography;
using System.Text;

namespace m3u8downloader.Services;

/// <summary>
/// 统一管理单个任务的路径：
///   &lt;保存目录&gt;/&lt;任务名&gt;/                        只放成品与下载中的实时文件
///   %LOCALAPPDATA%/m3u8downloader/cache/&lt;urlHash&gt;/   分片与断点状态，任务结束即删
/// 缓存外置，任务文件夹保持干净，也不会被网盘同步到临时分片。
/// </summary>
public sealed class TaskPaths
{
    private const string MarkerFile = "task.path";
    private const string AppFolder = "m3u8downloader";

    private TaskPaths(string saveDirectory, string taskDirectory, string fileName,
        string visiblePath, string cacheDirectory, bool isResumed)
    {
        SaveDirectory = saveDirectory;
        TaskDirectory = taskDirectory;
        FileName = fileName;
        VisiblePath = visiblePath;
        CacheDirectory = cacheDirectory;
        IsResumed = isResumed;
        OutputPath = Path.Combine(taskDirectory, fileName);
    }

    public string SaveDirectory { get; }
    public string TaskDirectory { get; }
    public string FileName { get; }
    public string OutputPath { get; }
    public string VisiblePath { get; }
    public string CacheDirectory { get; }
    public bool IsResumed { get; }

    public string VodStatePath => Path.Combine(CacheDirectory, "resume.state");
    public string LiveStatePath => Path.Combine(CacheDirectory, "live.state");

    public static string CacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolder, "cache");

    /// <summary>旧版本散落在系统临时目录的缓存，用于兼容清理。</summary>
    public static string LegacyCacheRoot => Path.Combine(Path.GetTempPath(), "M3u8Downloader");

    /// <summary>
    /// 分配任务路径。若同一 URL 还有未完成的状态且任务文件夹仍在，直接复用以便断点续传；
    /// 否则新建文件夹，重名时追加日期（同日再重名追加序号）。
    /// </summary>
    public static TaskPaths Resolve(string saveDirectory, string fileName, string url)
    {
        Directory.CreateDirectory(saveDirectory);
        var root = Path.GetFullPath(saveDirectory);
        var cacheDirectory = Path.Combine(CacheRoot, Hash(url));
        Directory.CreateDirectory(cacheDirectory);

        var marker = Path.Combine(cacheDirectory, MarkerFile);
        var hasPendingState = File.Exists(Path.Combine(cacheDirectory, "resume.state")) ||
                              File.Exists(Path.Combine(cacheDirectory, "live.state"));

        if (hasPendingState && File.Exists(marker))
        {
            var lines = File.ReadAllLines(marker);
            if (lines.Length >= 3 && Directory.Exists(lines[0]) && File.Exists(lines[1]) &&
                IsUnderRoot(lines[0], root))
                return new TaskPaths(root, lines[0], lines[2], lines[1], cacheDirectory, true);
        }

        var stem = Sanitize(Path.GetFileNameWithoutExtension(fileName));
        if (stem.Length == 0) stem = "video";
        var candidate = stem;
        if (Directory.Exists(Path.Combine(root, candidate)))
        {
            var date = DateTime.Now.ToString("yyyyMMdd");
            candidate = $"{stem}_{date}";
            for (var i = 2; Directory.Exists(Path.Combine(root, candidate)); i++)
                candidate = $"{stem}_{date}_{i}";
        }

        var taskDirectory = Path.Combine(root, candidate);
        Directory.CreateDirectory(taskDirectory);
        var visible = Path.Combine(taskDirectory, stem + ".ts");
        File.WriteAllLines(marker, [taskDirectory, visible, fileName]);
        return new TaskPaths(root, taskDirectory, fileName, visible, cacheDirectory, false);
    }

    /// <summary>删除该任务的整个缓存目录（成品不受影响）。</summary>
    public void DeleteCache() => DeleteDirectory(CacheDirectory);

    /// <summary>
    /// 清理超期的程序缓存目录。只处理缓存根目录下的直接子目录，且必须带程序标记，
    /// 绝不触碰保存目录中的任何视频文件。protectedDirectories 用于排除正在下载的任务。
    /// </summary>
    public static (int Directories, long Bytes) CleanupStale(
        TimeSpan maxAge, IEnumerable<string>? protectedDirectories = null,
        IEnumerable<string>? roots = null)
    {
        var protectedSet = new HashSet<string>(
            protectedDirectories?.Select(Path.GetFullPath) ?? [],
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var cutoff = DateTime.UtcNow - maxAge;
        var removed = 0;
        long bytes = 0;

        // roots 仅供测试限定清理范围，生产调用走默认的两个缓存根目录
        foreach (var root in roots ?? [CacheRoot, LegacyCacheRoot])
        {
            if (!Directory.Exists(root)) continue;
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                try
                {
                    if (protectedSet.Contains(Path.GetFullPath(directory))) continue;
                    if (Directory.GetLastWriteTimeUtc(directory) > cutoff) continue;
                    if (!IsProgramCache(directory)) continue;

                    bytes += DirectorySize(directory);
                    Directory.Delete(directory, true);
                    removed++;
                }
                catch
                {
                    // 被占用或权限不足时跳过，下次启动再清
                }
            }
        }
        return (removed, bytes);
    }

    public static long CacheSize()
    {
        long total = 0;
        foreach (var root in new[] { CacheRoot, LegacyCacheRoot })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                try { if (IsProgramCache(directory)) total += DirectorySize(directory); }
                catch { /* 忽略不可读目录 */ }
            }
        }
        return total;
    }

    private static bool IsProgramCache(string directory)
    {
        var name = Path.GetFileName(directory);
        // 目录名必须是程序生成的 16 位十六进制哈希，且含程序标记或分片文件
        if (name.Length != 16 || !name.All(Uri.IsHexDigit)) return false;
        return File.Exists(Path.Combine(directory, MarkerFile)) ||
               !Directory.EnumerateFileSystemEntries(directory).Any() || // 空目录也是程序残留
               Directory.EnumerateFiles(directory).Any(f =>
                   f.EndsWith(".seg", StringComparison.OrdinalIgnoreCase) ||
                   f.EndsWith(".part", StringComparison.OrdinalIgnoreCase) ||
                   f.EndsWith(".state", StringComparison.OrdinalIgnoreCase));
    }

    private static long DirectorySize(string directory)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(file).Length; } catch { /* 忽略 */ }
        }
        return total;
    }

    private static bool IsUnderRoot(string taskDirectory, string root)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(taskDirectory));
        return string.Equals(parent?.TrimEnd(Path.DirectorySeparatorChar),
            root.TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..16];

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.', ' ');
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { /* 忽略 */ }
    }
}
