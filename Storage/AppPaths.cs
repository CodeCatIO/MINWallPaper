using System.IO;

namespace MINWallPaper;

/// <summary>
/// 程序的数据目录。全部放在可执行文件旁边，不藏到 AppData，
/// 用户打开文件夹就能看到壁纸、缩略图、配置。
/// </summary>
internal static class AppPaths
{
    public static string BaseDir { get; } = AppContext.BaseDirectory;

    public static string DataDir { get; } = Path.Combine(BaseDir, "data");

    public static string ThumbnailDir { get; } = Path.Combine(DataDir, "thumbnails");

    public static string DefaultWallpaperDir { get; } = Path.Combine(DataDir, "wallpapers");

    public static string LibraryFile { get; } = Path.Combine(DataDir, "library.json");

    public static string SettingsFile { get; } = Path.Combine(DataDir, "settings.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ThumbnailDir);
        Directory.CreateDirectory(DefaultWallpaperDir);
    }
}
