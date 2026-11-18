using System.Drawing;
using System.Drawing.Drawing2D;

namespace MINWallPaper.Widgets;

/// <summary>10 种音浪样式的 GDI+ 绘制，频谱数据由 AudioSpectrum 提供。</summary>
internal static class WavePainter
{
    public static void Paint(Graphics g, Size size, WidgetConfig c, float[] levels, double seconds)
    {
        int w = size.Width;
        int h = size.Height;

        if (w < 12 || h < 12)
        {
            return;
        }

        if (levels.Length == 0)
        {
            levels = WidgetPainter.DemoLevels(c.BarCount);
        }

        GraphicsPath? clip = null;
        if (c.BgAlpha > 0.02)
        {
            clip = Round.Create(new Rectangle(0, 0, w - 1, h - 1), c.Radius);
            g.SetClip(clip);
        }

        switch (c.Style)
        {
            case "line":
                PaintLine(g, w, h, c, levels, seconds);
                break;
            case "glass":
                PaintGlassBars(g, w, h, c, levels);
                break;
            case "radial":
                PaintRadial(g, w, h, c, levels);
                break;
            case "dots":
                PaintDots(g, w, h, c, levels, seconds);
                break;
            case "aurora":
                PaintAurora(g, w, h, c, levels, seconds);
                break;
            case "scan":
                PaintScan(g, w, h, c, levels, seconds);
                break;
            case "ink":
                PaintInk(g, w, h, c, levels, seconds);
                break;
            case "segments":
                PaintSegments(g, w, h, c, levels);
                break;
            case "mirror":
                PaintMirror(g, w, h, c, levels);
                break;
            default:
                PaintBars(g, w, h, c, levels);
                break;
        }

        if (clip != null)
        {
            g.ResetClip();
            clip.Dispose();
        }
    }

    private static void DrawBackground(Graphics g, int w, int h, WidgetConfig c)
    {
        if (c.BgAlpha <= 0.02)
        {
            return;
        }

        var rect = new Rectangle(0, 0, w - 1, h - 1);
        PaintKit.DrawCard(g, rect, c.Radius, c.BgFill, 30);
    }

    private static float At(float[] levels, float position)
    {
        if (levels.Length == 0)
        {
            return 0;
        }

        position = Math.Clamp(position, 0f, 0.9999f);
        float scaled = position * (levels.Length - 1);
        int index = (int)scaled;
        int next = Math.Min(levels.Length - 1, index + 1);
        float t = scaled - index;
        t = t * t * (3f - 2f * t);
        return levels[index] * (1 - t) + levels[next] * t;
    }

    /// <summary>把低频放在中间，两边对称，做出从中间向两侧震动的效果。</summary>
    private static int MirrorIndex(int index, int count)
    {
        int half = count / 2;
        return index < half ? half - 1 - index : index - half;
    }

    private static void PaintBars(Graphics g, int w, int h, WidgetConfig c, float[] levels)
    {
        DrawBackground(g, w, h, c);

        float pad = Math.Min(h * 0.16f, w * 0.03f);
        float maxH = h - pad * 2;
        int count = levels.Length;
        float slot = (w - pad * 2) / count;
        float barW = Math.Max(2f, slot * 0.62f);
        float mid = h / 2f;

        using var gradient = new LinearGradientBrush(
            new RectangleF(0, 0, w, h), c.Accent, c.Accent2, 0f);

        for (int i = 0; i < count; i++)
        {
            float level = Math.Clamp(levels[MirrorIndex(i, count)], 0f, 1f);
            float barH = Math.Max(2f, level * maxH * 0.46f);
            float x = pad + i * slot + (slot - barW) / 2f;
            var rect = new RectangleF(x, mid - barH, barW, barH * 2f);

            using GraphicsPath path = Round.Create(Rectangle.Round(rect), (int)(barW / 2f));
            g.FillPath(gradient, path);
        }
    }

    private static void PaintLine(Graphics g, int w, int h, WidgetConfig c, float[] levels, double seconds)
    {
        DrawBackground(g, w, h, c);

        float pad = Math.Min(h * 0.14f, w * 0.03f);
        float maxH = h - pad * 2;
        float mid = h / 2f;
        int steps = Math.Max(48, w / 4);

        using var path = new GraphicsPath();
        for (int i = 0; i <= steps; i++)
        {
            float p = i / (float)steps;
            float level = At(levels, p);
            float wobble = (float)Math.Sin(p * 9f + seconds * 1.6) * 0.05f;
            float y = mid - (level * 0.9f + wobble) * maxH * 0.42f;
            float x = pad + p * (w - pad * 2);

            if (i == 0)
            {
                path.AddLine(x, y, x, y);
            }
            else
            {
                path.AddLine(x, y, x, y);
            }
        }

        (float Width, int Alpha)[] passes = { (8f, 22), (4.5f, 55), (1.8f, 255) };
        foreach ((float width, int alpha) in passes)
        {
            using var pen = new Pen(PaintKit.Alpha(c.Accent, alpha), width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            g.DrawPath(pen, path);
        }

        // 下方一条淡淡的倒影
        using (var mirrored = new GraphicsPath())
        {
            mirrored.AddPath(path, false);
            using var matrix = new Matrix();
            matrix.Translate(0, mid * 2);
            matrix.Scale(1, -1);
            mirrored.Transform(matrix);
            using var pen = new Pen(PaintKit.Alpha(c.Accent2, 40), 1.4f);
            g.DrawPath(pen, mirrored);
        }
    }

    private static void PaintGlassBars(Graphics g, int w, int h, WidgetConfig c, float[] levels)
    {
        DrawBackground(g, w, h, c);

        float pad = Math.Min(h * 0.14f, w * 0.03f);
        float maxH = h - pad * 2;
        int count = levels.Length;
        float slot = (w - pad * 2) / count;
        float barW = Math.Max(2f, slot * 0.64f);

        for (int i = 0; i < count; i++)
        {
            float level = Math.Clamp(levels[i], 0f, 1f);
            float barH = Math.Max(3f, level * maxH);
            float x = pad + i * slot + (slot - barW) / 2f;
            var rect = new RectangleF(x, h - pad - barH, barW, barH);

            using GraphicsPath path = PaintKit.TopRounded(rect, barW / 2f);
            using var gradient = new LinearGradientBrush(
                new RectangleF(x, h - pad - maxH, barW, maxH), c.Accent, c.Accent2, 90f);
            g.FillPath(gradient, path);

            using var highlight = new SolidBrush(Color.FromArgb(46, 255, 255, 255));
            g.FillRectangle(highlight, x + barW * 0.2f, h - pad - barH + 3, Math.Max(1f, barW * 0.16f),
                Math.Max(0f, barH - 6));
        }
    }

    private static void PaintRadial(Graphics g, int w, int h, WidgetConfig c, float[] levels)
    {
        DrawBackground(g, w, h, c);

        float cx = w / 2f;
        float cy = h / 2f;
        float inner = Math.Min(w, h) * 0.14f;
        float amp = Math.Min(w, h) * 0.3f;
        int count = levels.Length;

        for (int i = 0; i < count; i++)
        {
            float angle = i / (float)count * (float)Math.PI * 2 - (float)Math.PI / 2;
            float level = Math.Clamp(levels[i], 0f, 1f);
            float outer = inner + 4 + level * amp;

            Color color = PaintKit.Blend(c.Accent, c.Accent2, level);
            using var pen = new Pen(color, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(pen,
                cx + (float)Math.Cos(angle) * inner, cy + (float)Math.Sin(angle) * inner,
                cx + (float)Math.Cos(angle) * outer, cy + (float)Math.Sin(angle) * outer);
        }

        using (var disc = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
        {
            g.FillEllipse(disc, cx - inner * 0.72f, cy - inner * 0.72f, inner * 1.44f, inner * 1.44f);
        }

        using (var ring = new Pen(PaintKit.Alpha(c.Accent, 130), 1.4f))
        {
            g.DrawEllipse(ring, cx - inner, cy - inner, inner * 2, inner * 2);
        }
    }

    private static void PaintDots(Graphics g, int w, int h, WidgetConfig c, float[] levels, double seconds)
    {
        DrawBackground(g, w, h, c);

        float pad = Math.Min(h * 0.14f, w * 0.03f);
        int cols = levels.Length;
        int rows = 6;
        float cellW = (w - pad * 2) / cols;
        float cellH = (h - pad * 2) / rows;
        float baseR = Math.Min(cellW, cellH) * 0.3f;

        for (int i = 0; i < cols; i++)
        {
            float level = Math.Clamp(levels[i], 0f, 1f);

            for (int j = 0; j < rows; j++)
            {
                float distance = Math.Abs(j - (rows - 1) / 2f) / ((rows - 1) / 2f);
                float envelope = 1f - distance * 0.82f;
                float lit = level * envelope;
                int alpha = (int)(22 + lit * 220);
                float radius = baseR * (0.45f + lit * 0.95f);
                float x = pad + cellW * (i + 0.5f);
                float y = pad + cellH * (j + 0.5f)
                          + (float)Math.Sin(seconds * 1.7 + i * 0.45f) * cellH * 0.14f;

                using var brush = new SolidBrush(PaintKit.Alpha(
                    PaintKit.Blend(c.Accent, c.Accent2, lit), alpha));
                g.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
            }
        }
    }

    private static void PaintAurora(Graphics g, int w, int h, WidgetConfig c, float[] levels, double seconds)
    {
        DrawBackground(g, w, h, c);

        float pad = Math.Min(h * 0.1f, w * 0.03f);
        float maxH = h - pad * 2;
        int steps = Math.Max(40, w / 6);

        (Color Color, float Amp, float Speed, double Phase)[] layers =
        {
            (c.Accent, 0.55f, 1.1f, 0),
            (c.Accent2, 0.75f, 1.5f, 1.7),
            (PaintKit.Blend(c.Accent, c.Accent2, 0.5f), 0.95f, 1.9f, 3.1)
        };

        for (int layer = 0; layer < layers.Length; layer++)
        {
            using var path = new GraphicsPath();
            path.AddLine(0, h, 0, h);

            for (int i = 0; i <= steps; i++)
            {
                float p = i / (float)steps;
                float level = At(levels, p);
                float x = pad + p * (w - pad * 2);
                float y = h * 0.58f - level * maxH * 0.42f * layers[layer].Amp
                          + (float)Math.Sin(p * 6.2f * (1 + layer * 0.4f) + seconds * layers[layer].Speed
                                             + layers[layer].Phase) * h * 0.06f;
                path.AddLine(x, y, x, y);
            }

            path.AddLine(w, h, 0, h);
            path.CloseFigure();

            using var gradient = new LinearGradientBrush(
                new RectangleF(0, 0, w, h),
                PaintKit.Alpha(layers[layer].Color, 88),
                PaintKit.Alpha(layers[layer].Color, 0), 90f);
            g.FillPath(gradient, path);
        }
    }

    private static void PaintScan(Graphics g, int w, int h, WidgetConfig c, float[] levels, double seconds)
    {
        DrawBackground(g, w, h, c);

        using (var grid = new Pen(PaintKit.Alpha(c.Accent, 20), 1f))
        {
            for (int x = 0; x < w; x += 24)
            {
                g.DrawLine(grid, x, 0, x, h);
            }

            for (int y = 0; y < h; y += 24)
            {
                g.DrawLine(grid, 0, y, w, y);
            }
        }

        float scanY = (float)(seconds * 52 % (h + 90)) - 45;
        using (var band = new LinearGradientBrush(
                   new RectangleF(0, scanY - 34, w, 68),
                   PaintKit.Alpha(c.Accent, 0), PaintKit.Alpha(c.Accent, 0), 90f))
        {
            var blend = new ColorBlend
            {
                Colors = new[]
                {
                    PaintKit.Alpha(c.Accent, 0),
                    PaintKit.Alpha(c.Accent, 62),
                    PaintKit.Alpha(c.Accent, 0)
                },
                Positions = new[] { 0f, 0.5f, 1f }
            };
            band.InterpolationColors = blend;
            g.FillRectangle(band, 0, scanY - 34, w, 68);
        }

        float pad = Math.Min(h * 0.16f, w * 0.03f);
        float mid = h / 2f;
        float amp = (h - pad * 2) * 0.4f;
        int steps = Math.Max(48, w / 4);

        using var path = new GraphicsPath();
        for (int i = 0; i <= steps; i++)
        {
            float p = i / (float)steps;
            float y = mid - (At(levels, p) * 0.85f + 0.05f) * amp;
            float x = pad + p * (w - pad * 2);
            path.AddLine(x, y, x, y);
        }

        using (var glow = new Pen(PaintKit.Alpha(c.Accent, 60), 5f) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(glow, path);
        }

        using (var core = new Pen(c.Accent, 1.6f) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(core, path);
        }
    }

    private static void PaintInk(Graphics g, int w, int h, WidgetConfig c, float[] levels, double seconds)
    {
        DrawBackground(g, w, h, c);

        float cx = w / 2f;
        float cy = h / 2f;
        float average = levels.Length == 0 ? 0 : levels.Average();
        float radiusBase = Math.Min(w, h) * 0.42f;

        for (int i = 0; i < 5; i++)
        {
            float phase = (float)((seconds * 0.32 + i * 0.2) % 1.0);
            float radius = radiusBase * (0.18f + 0.82f * phase) * (0.78f + average * 0.5f);
            int alpha = (int)((1 - phase) * 120);

            using var pen = new Pen(PaintKit.Alpha(c.Accent, alpha), 1.4f) { LineJoin = LineJoin.Round };
            using var path = new GraphicsPath();

            for (int step = 0; step <= 72; step++)
            {
                float angle = step / 72f * (float)Math.PI * 2;
                float wobble = (float)Math.Sin(angle * 5f + seconds * 1.8 + i) * 3f;
                float x = cx + (float)Math.Cos(angle) * (radius + wobble);
                float y = cy + (float)Math.Sin(angle) * (radius + wobble) * 0.62f;

                if (step == 0)
                {
                    path.AddLine(x, y, x, y);
                }
                else
                {
                    path.AddLine(x, y, x, y);
                }
            }

            g.DrawPath(pen, path);
        }

        using (var dot = new SolidBrush(PaintKit.Alpha(c.Accent, (int)(40 + average * 120))))
        {
            float r = 4f + average * 8f;
            g.FillEllipse(dot, cx - r, cy - r, r * 2, r * 2);
        }
    }

    private static void PaintSegments(Graphics g, int w, int h, WidgetConfig c, float[] levels)
    {
        DrawBackground(g, w, h, c);

        float pad = Math.Min(h * 0.14f, w * 0.03f);
        float maxH = h - pad * 2;
        int count = levels.Length;
        float slot = (w - pad * 2) / count;
        float barW = Math.Max(2f, slot * 0.56f);
        const int blocks = 12;
        float blockH = maxH / blocks;

        for (int i = 0; i < count; i++)
        {
            float level = Math.Clamp(levels[i], 0f, 1f);
            int lit = (int)Math.Round(level * blocks);

            for (int b = 0; b < blocks; b++)
            {
                float x = pad + i * slot + (slot - barW) / 2f;
                float y = h - pad - (b + 1) * blockH + 1.2f;
                Color color = b < lit ? c.Accent : PaintKit.Alpha(c.Accent, 26);

                if (b < lit)
                {
                    color = PaintKit.Blend(c.Accent2, c.Accent, b / (float)blocks);
                }

                using GraphicsPath path = Round.Create(
                    Rectangle.Round(new RectangleF(x, y, barW, Math.Max(1.4f, blockH - 2.4f))), 2);
                using var brush = new SolidBrush(color);
                g.FillPath(brush, path);
            }
        }
    }

    private static void PaintMirror(Graphics g, int w, int h, WidgetConfig c, float[] levels)
    {
        DrawBackground(g, w, h, c);

        float pad = Math.Min(h * 0.12f, w * 0.03f);
        float maxH = h - pad * 2;
        int count = levels.Length;
        float slot = (w - pad * 2) / count;
        float barW = Math.Max(2f, slot * 0.6f);
        float mid = h / 2f;

        using var gradient = new LinearGradientBrush(
            new RectangleF(0, 0, w, h), c.Accent, c.Accent2, 0f);
        using var reflection = new LinearGradientBrush(
            new RectangleF(0, 0, w, h),
            PaintKit.Alpha(c.Accent, 70), PaintKit.Alpha(c.Accent2, 70), 0f);

        for (int i = 0; i < count; i++)
        {
            float level = Math.Clamp(levels[MirrorIndex(i, count)], 0f, 1f);
            float barH = Math.Max(2f, level * maxH * 0.44f);
            float x = pad + i * slot + (slot - barW) / 2f;

            using (GraphicsPath top = Round.Create(
                       Rectangle.Round(new RectangleF(x, mid - barH - 1, barW, barH)), (int)(barW / 2f)))
            {
                g.FillPath(gradient, top);
            }

            using GraphicsPath bottom = Round.Create(
                Rectangle.Round(new RectangleF(x, mid + 1, barW, barH * 0.72f)), (int)(barW / 2f));
            g.FillPath(reflection, bottom);
        }

        using var line = new Pen(PaintKit.Alpha(c.Text, 22), 1f);
        g.DrawLine(line, pad, mid, w - pad, mid);
    }
}
