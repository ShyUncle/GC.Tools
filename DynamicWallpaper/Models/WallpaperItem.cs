namespace DynamicWallpaper.Models
{
    public enum WallpaperType
    {
        Image,
        Video
    }
    public class WallpaperItem
    {
        public string Path { get; set; } = string.Empty;
        public WallpaperType Type { get; set; } = WallpaperType.Image;
        public string Category { get; set; } = "默认";
        public bool IsFavorite { get; set; }

        /// <summary>显示标题（在线壁纸使用来源标题；本地壁纸为空时回退到文件名）。</summary>
        public string? Title { get; set; }

        /// <summary>是否来自网络在线来源（用于「我的壁纸」栏区分自主上传内容）。</summary>
        public bool IsOnline { get; set; }

        /// <summary>在线壁纸的原始下载地址（转正后置空，用于识别已保存的网络壁纸）。</summary>
        public string? SourceUrl { get; set; }

        /// <summary>在线壁纸的缩略图地址（Pexels CDN 小图，列表浏览用，无需下载完整文件）。</summary>
        public string? ThumbnailUrl { get; set; }
    }
}
