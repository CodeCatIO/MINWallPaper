using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

namespace MINWallPaper.Widgets;

/// <summary>图片组件：单图、三图拼贴、轮播。</summary>
internal static class ImagePainter
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Bitmap?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static void Paint(Graphics g, Size size, WidgetConfig c, double seconds)
    {
        int w = size.Width;
        int h = size.Height;
        PaintKit.DrawPanelBackground(g, w, h, c);

        List<string> images = c.Images.Where(File.Exists).ToList();

        if (images.Count == 0)
        {
            DrawPlaceholder(g, new RectangleF(6, 6, w - 12, h - 12), c);
            return;
        }

        var content = new RectangleF(6, 6, w - 12, h - 12);
        int radius = Math.Max(2, Math.Min(c.Radius, (int)(Math.Min(content.Width, content.Height) * 0.18f)));

        switch (c.Style)
        {
            case "grid":
            {
                int cells = Math.Min(3, images.Count);
                float gap = 5f;
                float cellW = (content.Width - gap * (cells - 1)) / cells;

                for (int i = 0; i < cells; i++)
                {
                    var rect = new RectangleF(content.Left + i * (cellW + gap), content.Top, cellW,
                        content.Height);
                    DrawCover(g, images[i], rect, radius);
                }

                break;
            }
            case "carousel":
            {
                int index = (int)(seconds / Math.Max(2, c.CarouselSeconds)) % images.Count;
                DrawCover(g, images[index], content, radius);
                break;
            }
            default:
                DrawCover(g, images[0], content, radius);
                break;
        }
    }

    private static void DrawCover(Graphics g, string path, RectangleF rect, int radius)
    {
        Bitmap? bitmap = Load(path);

        if (bitmap == null)
        {
            return;
        }

        using GraphicsPath clip = Round.Create(Rectangle.Round(rect), radius);
        g.SetClip(clip);

        float scale = Math.Max(rect.Width / bitmap.Width, rect.Height / bitmap.Height);
        float srcW = rect.Width / scale;
        float srcH = rect.Height / scale;
        float srcX = (bitmap.Width - srcW) / 2f;
        float srcY = (bitmap.Height - srcH) / 2f;

        g.DrawImage(bitmap, rect, new RectangleF(srcX, srcY, srcW, srcH), GraphicsUnit.Pixel);
        g.ResetClip();
    }

    private static Bitmap? Load(string path)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(path, out Bitmap? cached))
            {
                return cached;
            }

            Bitmap? bitmap = null;

            try
            {
                using var source = Image.FromFile(path);
                bitmap = new Bitmap(source);
            }
            catch (Exception ex)
            {
                Log.Write("加载组件图片失败: " + ex.Message);
            }

            if (Cache.Count > 24)
            {
                Cache.Clear();
            }

            Cache[path] = bitmap;
            return bitmap;
        }
    }

    private static void DrawPlaceholder(Graphics g, RectangleF rect, WidgetConfig c)
    {
        using (var pen = new Pen(PaintKit.Alpha(c.Text, 70), 1.2f) { DashStyle = DashStyle.Dash })
        using (GraphicsPath path = Round.Create(Rectangle.Round(rect), Math.Max(2, c.Radius)))
        {
            g.DrawPath(pen, path);
        }

        using Font font = PaintKit.CreateFont(c.FontFamily, Math.Max(9f, c.FontSize * 0.28f));
        PaintKit.DrawText(g, "编辑时选择图片", font, PaintKit.Alpha(c.Text, 95),
            rect, StringAlignment.Center, StringAlignment.Center);
    }
}
