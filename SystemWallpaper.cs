using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MINWallPaper;

/// <summary>
/// 静态壁纸交给 Windows 设置，不自己渲染。
/// 需要自定义位置时，先把图片按屏幕尺寸和偏移画好，再交给系统铺满。
/// </summary>
internal static class SystemWallpaper
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPI_GETDESKWALLPAPER = 0x0073;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SystemParametersInfo(int action, int param, string value, int winIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SystemParametersInfo(int action, int param, System.Text.StringBuilder value,
        int winIni);

    // 视频播放前会把桌面壁纸清空，退出视频时再恢复，避免静态壁纸盖住视频层。
    private static string? _savedWallpaper;
    private static bool _clearedForVideo;

    // 视频首帧兜底在后台线程里调用，和界面上的图片壁纸可能同时设置，串行化避免抢同一个文件。
    private static readonly object Gate = new();

    public static bool Apply(string imagePath, ImageFillMode mode, double verticalOffset = 0.5)
    {
        if (!File.Exists(imagePath))
        {
            Log.Write("壁纸文件不存在: " + imagePath);
            return false;
        }

        lock (Gate)
        {
            // 明确设置了图片壁纸，它成为之后视频停止时的恢复点。
            _clearedForVideo = false;
            return ApplyCore(imagePath, mode, verticalOffset);
        }
    }

    private static bool ApplyCore(string imagePath, ImageFillMode mode, double verticalOffset)
    {
        try
        {
            // 平铺模式直接用原图，其余模式先合成一张整屏图，位置偏移才可控。
            string target = mode == ImageFillMode.Tile
                ? imagePath
                : ComposeScreenImage(imagePath, mode, verticalOffset);

            if (target.Length == 0)
            {
                return false;
            }

            SetStyle(mode == ImageFillMode.Tile ? "0" : "10",
                mode == ImageFillMode.Tile ? "1" : "0");

            int ok = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, target,
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            if (ok != 0 && !_clearedForVideo)
            {
                _savedWallpaper = target;
            }

            Log.Write($"系统壁纸已设置 mode={mode} offset={verticalOffset:F2} 结果={ok}");
            return ok != 0;
        }
        catch (Exception ex)
        {
            Log.Write("设置系统壁纸失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 播放视频前清空 Windows 静态壁纸。
    /// Win11 的静态壁纸会盖在 WorkerW 视频层上面，不清掉就只剩黑屏/旧壁纸。
    /// </summary>
    public static void ClearForVideo()
    {
        lock (Gate)
        {
            if (!_clearedForVideo)
            {
                string current = CurrentDesktopWallpaper();

                if (!string.IsNullOrEmpty(current) && File.Exists(current))
                {
                    _savedWallpaper = current;
                }

                _clearedForVideo = true;
            }

            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, "",
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

            Log.Write("已清除系统壁纸，避免盖住视频层");
        }
    }

    /// <summary>停止视频壁纸时把之前的系统壁纸还回去。</summary>
    public static void RestoreAfterVideo()
    {
        lock (Gate)
        {
            if (!_clearedForVideo)
            {
                return;
            }

            _clearedForVideo = false;

            if (!string.IsNullOrEmpty(_savedWallpaper) && File.Exists(_savedWallpaper))
            {
                ApplyCore(_savedWallpaper, ImageFillMode.Fill, 0.5);
                Log.Write("已恢复视频前的系统壁纸: " + _savedWallpaper);
            }
        }
    }

    /// <summary>系统壁纸是不是又被 Windows / 其他程序设回去了。</summary>
    public static bool IsDesktopWallpaperSet()
    {
        string current = CurrentDesktopWallpaper();
        return !string.IsNullOrEmpty(current);
    }

    private static string CurrentDesktopWallpaper()
    {
        var buffer = new System.Text.StringBuilder(1024);
        int ok = SystemParametersInfo(SPI_GETDESKWALLPAPER, buffer.Capacity, buffer, 0);
        return ok != 0 ? buffer.ToString() : "";
    }

    private static string ComposeScreenImage(string imagePath, ImageFillMode mode, double offset)
    {
        Rectangle screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        int width = screen.Width;
        int height = screen.Height;

        using var source = Image.FromFile(imagePath);
        using var canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (mode == ImageFillMode.Stretch)
            {
                g.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            else
            {
                bool cover = mode == ImageFillMode.Fill;
                float scale = cover
                    ? Math.Max((float)width / source.Width, (float)height / source.Height)
                    : Math.Min((float)width / source.Width, (float)height / source.Height);

                int drawW = Math.Max(1, (int)(source.Width * scale));
                int drawH = Math.Max(1, (int)(source.Height * scale));
                int drawX = (width - drawW) / 2;
                int drawY = (int)((height - drawH) * Math.Clamp(offset, 0, 1));

                g.DrawImage(source, new Rectangle(drawX, drawY, drawW, drawH));
            }
        }

        string dir = Path.Combine(AppPaths.DataDir, "generated");
        Directory.CreateDirectory(dir);
        string target = Path.Combine(dir, "current.jpg");

        using var stream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
        canvas.Save(stream, ImageFormat.Jpeg);
        return target;
    }

    private static void SetStyle(string wallpaperStyle, string tileWallpaper)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true);
        if (key == null)
        {
            return;
        }

        key.SetValue("WallpaperStyle", wallpaperStyle, RegistryValueKind.String);
        key.SetValue("TileWallpaper", tileWallpaper, RegistryValueKind.String);
    }
}
