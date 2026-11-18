using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MINWallPaper.Widgets;

/// <summary>组件绘制入口：按类型分发给时钟或音浪画笔。</summary>
internal static class WidgetPainter
{
    public static void Paint(Graphics g, Size size, WidgetConfig config, float[]? levels, double seconds)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        if (config.Type == "wave")
        {
            WavePainter.Paint(g, size, config, levels ?? Array.Empty<float>(), seconds);
            return;
        }

        if (config.Type == "text")
        {
            TextPainter.Paint(g, size, config);
            return;
        }

        if (config.Type == "image")
        {
            ImagePainter.Paint(g, size, config, seconds);
            return;
        }

        if (config.Type == "cpu")
        {
            CpuPainter.Paint(g, size, config);
            return;
        }

        if (config.Type == "memory")
        {
            MemoryPainter.Paint(g, size, config);
            return;
        }

        if (config.Type == "gpu")
        {
            GpuPainter.Paint(g, size, config);
            return;
        }

        if (config.Type == "disk")
        {
            DiskPainter.Paint(g, size, config);
            return;
        }

        ClockPainter.Paint(g, size, config);
    }

    /// <summary>调试用示例频谱，让预览图里也能看到柱子。</summary>
    public static float[] DemoLevels(int count)
    {
        var levels = new float[Math.Max(8, count)];
        for (int i = 0; i < levels.Length; i++)
        {
            float x = i / (float)(levels.Length - 1);
            float bass = 0.22f + 0.68f * (float)Math.Pow(1 - x, 1.4);
            float ripple = 0.16f * (float)Math.Sin(i * 0.9f) * (0.35f + 0.65f * (1 - x));
            levels[i] = Math.Clamp(bass + ripple, 0.08f, 1f);
        }

        return levels;
    }
}

/// <summary>绘制小工具：字体回退、颜色混合、圆角卡片、居中文本。</summary>
internal static class PaintKit
{
    private static readonly HashSet<string> Installed = new(
        FontFamily.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);

    public static Font CreateFont(string family, float pixelSize, FontStyle style = FontStyle.Regular)
    {
        string name = !string.IsNullOrWhiteSpace(family) && Installed.Contains(family)
            ? family
            : "Microsoft YaHei UI";

        return new Font(name, Math.Max(6f, pixelSize), style, GraphicsUnit.Pixel);
    }

    public static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect,
        StringAlignment horizontal = StringAlignment.Near, StringAlignment vertical = StringAlignment.Center,
        bool wrap = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = horizontal,
            LineAlignment = vertical,
            FormatFlags = wrap ? 0 : StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter
        };

        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, rect, format);
    }

    /// <summary>按字距逐字绘制，做出细体钟那种疏朗的排版。</summary>
    public static void DrawSpacedText(Graphics g, string text, Font font, Color color, float centerX, float y,
        float spacing)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        float total = 0;
        foreach (char ch in text)
        {
            total += g.MeasureString(ch.ToString(), font, PointF.Empty, StringFormat.GenericTypographic).Width;
        }

        total += spacing * Math.Max(0, text.Length - 1);

        float x = centerX - total / 2f;
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic);

        foreach (char ch in text)
        {
            string s = ch.ToString();
            g.DrawString(s, font, brush, new PointF(x, y), format);
            x += g.MeasureString(s, font, PointF.Empty, StringFormat.GenericTypographic).Width + spacing;
        }
    }

    public static void DrawCard(Graphics g, Rectangle rect, int radius, Color fill, int borderAlpha = 34,
        Color? borderColor = null)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using GraphicsPath path = Round.Create(rect, Math.Max(0, radius));

        if (fill.A > 0)
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }

        if (borderAlpha > 0)
        {
            Color edge = borderColor ?? Color.White;
            using var pen = new Pen(Color.FromArgb(borderAlpha, edge), 1f);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>组件统一背景：背景浓度为 0 时什么都不画。</summary>
    public static void DrawPanelBackground(Graphics g, int w, int h, WidgetConfig c)
    {
        if (c.BgAlpha <= 0.02)
        {
            return;
        }

        DrawCard(g, new Rectangle(0, 0, w - 1, h - 1), c.Radius, c.BgFill, 30);
    }

    public static Color Alpha(Color color, int alpha) =>
        Color.FromArgb(Math.Clamp(alpha, 0, 255), color);

    public static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    public static Font FitFont(Graphics g, string text, string family, FontStyle style, float maxSize,
        float maxWidth, float minSize = 9f)
    {
        float size = Math.Max(minSize, maxSize);

        while (size > minSize)
        {
            Font probe = CreateFont(family, size, style);
            float width = g.MeasureString(text, probe, PointF.Empty, StringFormat.GenericTypographic).Width;
            if (width <= maxWidth)
            {
                return probe;
            }

            probe.Dispose();
            size -= Math.Max(1f, size * 0.06f);
        }

        return CreateFont(family, minSize, style);
    }

    /// <summary>顶部圆角的柱子路径，液态玻璃音浪用。</summary>
    public static GraphicsPath TopRounded(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);

        if (radius <= 0.5f)
        {
            path.AddRectangle(rect);
            return path;
        }

        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddLine(rect.Right, rect.Y + d / 2, rect.Right, rect.Bottom);
        path.AddLine(rect.Right, rect.Bottom, rect.X, rect.Bottom);
        path.AddLine(rect.X, rect.Bottom, rect.X, rect.Y + d / 2);
        path.CloseFigure();
        return path;
    }
}
