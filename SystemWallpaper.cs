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
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SystemParametersInfo(int action, int param, string value, int winIni);

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

            Log.Write($"系统壁纸已设置 mode={mode} offset={verticalOffset:F2} 结果={ok}");
            return ok != 0;
        }
        catch (Exception ex)
        {
            Log.Write("设置系统壁纸失败: " + ex.Message);
            return false;
        }
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
