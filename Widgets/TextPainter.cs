using System.Drawing;
using System.Drawing.Drawing2D;

namespace MINWallPaper.Widgets;

/// <summary>文本组件的 4 种样式。</summary>
internal static class TextPainter
{
    public static void Paint(Graphics g, Size size, WidgetConfig c)
    {
        int w = size.Width;
        int h = size.Height;

        if (w < 8 || h < 8)
        {
            return;
        }

        string text = string.IsNullOrWhiteSpace(c.Content) ? "文本" : c.Content.Trim();

        if (c.BgAlpha > 0.02)
        {
            PaintKit.DrawCard(g, new Rectangle(0, 0, w - 1, h - 1), c.Radius, c.BgFill, 26);
        }

        float pad = Math.Max(8f, w * 0.06f);
        FontStyle style = c.Style == "outline" ? FontStyle.Bold : FontStyle.Regular;

        if (PaintKit.IsScript(c.FontFamily))
        {
            style |= FontStyle.Italic;
        }

        using Font font = PaintKit.FitFont(g, text, c.FontFamily, style, c.FontSize, w - pad * 2);

        int alpha = (int)Math.Round(Math.Clamp(c.TextAlpha, 0, 1) * 255);
        Color color = PaintKit.Alpha(c.Text, alpha);

        float th = font.GetHeight(g);
        float top = (h - th) / 2f;

        switch (c.Style)
        {
            case "spaced":
                PaintKit.DrawSpacedText(g, text, font, color, w / 2f, top, c.FontSize * 0.1f);
                break;
            case "glow":
                PaintGlow(g, w, text, font, c, top, th, alpha);
                break;
            case "outline":
                PaintOutline(g, w, text, font, c, top, th, alpha);
                break;
            default:
                var rect = new RectangleF(0, top, w, th);
                if (c.GoldText)
                {
                    PaintKit.DrawGoldText(g, text, font, rect, StringAlignment.Center,
                        StringAlignment.Center, alpha);
                }
                else
                {
                    PaintKit.DrawText(g, text, font, color, rect,
                        StringAlignment.Center, StringAlignment.Center);
                }

                break;
        }
    }

    private static void PaintGlow(Graphics g, int w, string text, Font font, WidgetConfig c, float top,
        float th, int alpha)
    {
        (float Dx, float Dy, int Alpha)[] glow =
        {
            (0, 0, 72), (2, 0, 55), (-2, 0, 55), (0, 2, 55), (0, -2, 55), (3, 3, 30), (-3, -3, 30)
        };

        var rect = new RectangleF(0, top, w, th);

        foreach ((float dx, float dy, int layerAlpha) in glow)
        {
            PaintKit.DrawText(g, text, font, PaintKit.Alpha(c.Accent, layerAlpha * alpha / 255),
                new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height),
                StringAlignment.Center, StringAlignment.Center);
        }

        if (c.GoldText)
        {
            PaintKit.DrawGoldText(g, text, font, rect, StringAlignment.Center,
                StringAlignment.Center, alpha);
            return;
        }

        PaintKit.DrawText(g, text, font, PaintKit.Alpha(c.Text, alpha), rect,
            StringAlignment.Center, StringAlignment.Center);
    }

    private static void PaintOutline(Graphics g, int w, string text, Font font, WidgetConfig c, float top,
        float th, int alpha)
    {
        using var path = new GraphicsPath();
        using (var format = new StringFormat(StringFormat.GenericTypographic))
        {
            path.AddString(text, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, format);
        }

        RectangleF bounds = path.GetBounds();
        float dx = (w - bounds.Width) / 2f - bounds.X;
        float dy = top + (th - bounds.Height) / 2f - bounds.Y;

        using (var matrix = new Matrix())
        {
            matrix.Translate(dx, dy);
            path.Transform(matrix);
        }

        using (GraphicsPath ghost = (GraphicsPath)path.Clone())
        using (var ghostMatrix = new Matrix())
        {
            ghostMatrix.Translate(3, 4);
            ghost.Transform(ghostMatrix);
            using var brush = new SolidBrush(PaintKit.Alpha(c.Accent, 70 * alpha / 255));
            g.FillPath(brush, ghost);
        }

        Color stroke = c.GoldText
            ? Color.FromArgb(alpha, 218, 170, 60)
            : PaintKit.Alpha(c.Text, alpha);

        using (var pen = new Pen(stroke, Math.Max(1.2f, c.FontSize * 0.05f)))
        {
            g.DrawPath(pen, path);
        }
    }
}
