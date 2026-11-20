using System.IO;
using System.Text.Json;

namespace DynamicWallpaper.Config
{
    public class AppConfig
    {
        public int RotationIntervalSeconds { get; set; } = 60;
        public bool AutoStartRotation { get; set; }
        public bool MinimizeToTray { get; set; } = true;

        /// <summary>上次显示的壁纸路径，启动时自动恢复；路径失效时回退到库中第一张。</summary>
        public string? LastWallpaperPath { get; set; }

        public string ThumbnailCacheFolder { get; set; } = "thumbnails";
        public string ConfigPath { get; set; } = "config.json";

        /// <summary>
        /// 应用自有壁纸库文件夹名称（位于 AppData\DynamicWallpaper 下）。
        /// 通过“导入图片/视频”按钮选中的文件会被复制进这里，与用户添加的外部文件夹区分开。
        /// </summary>
        public string LibraryFolderName { get; set; } = "WallpaperLibrary";

        /// <summary>图库存储地址自定义覆盖（设置页可修改）；为空时使用 AppData 默认位置。</summary>
        public string? LibraryFolderPath { get; set; }

        /// <summary>
        /// Pexels API Key（https://www.pexels.com/api/ 免费申请）。
        /// 在线壁纸完全基于 Pexels（分类 + 分页浏览），请求时放在 Authorization 请求头中。
        /// </summary>
        public string PexelsApiKey { get; set; } = "";

        /// <summary>轮换范围一级分类：dynamic(动态壁纸)/static(静态壁纸)/favorites(我的收藏)/uploads(我的壁纸)。</summary>
        public string RotationScope { get; set; } = "static";

        /// <summary>轮换范围二级分类（仅动态/静态壁纸生效；null 或空=全部）。</summary>
        public string? RotationCategory { get; set; }
    }
    public static class ConfigService
    {
        public static string GetAppDataFolder()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DynamicWallpaper");

            Directory.CreateDirectory(folder);
            return folder;
        }

        public static string GetConfigPath()
        {
            return Path.Combine(GetAppDataFolder(), "config.json");
        }

        /// <summary>
        /// 获取应用自有壁纸库的绝对路径。
        /// </summary>
        public static string GetLibraryFolder(AppConfig? config = null)
        {
            // 设置页配置的自定义存储地址优先
            var custom = config?.LibraryFolderPath;
            if (!string.IsNullOrWhiteSpace(custom) && Path.IsPathRooted(custom))
                return custom;

            var name = config?.LibraryFolderName;
            if (string.IsNullOrWhiteSpace(name))
                name = "WallpaperLibrary";

            return Path.Combine(GetAppDataFolder(), name);
        }

        public static AppConfig Load()
        {
            var path = GetConfigPath();

            if (!File.Exists(path))
                return new AppConfig { ConfigPath = path };

            try
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    config.ConfigPath = path;
                    return config;
                }
            }
            catch
            {
                // 配置损坏时返回默认配置
            }

            return new AppConfig { ConfigPath = path };
        }

        public static void Save(AppConfig config)
        {
            try
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(config.ConfigPath, json);
            }
            catch
            {
                // 保存失败时不阻断程序运行
            }
        }
    }
}
