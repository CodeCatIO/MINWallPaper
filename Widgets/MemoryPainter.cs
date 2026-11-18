using System.Drawing;
using System.Drawing.Drawing2D;

namespace MINWallPaper.Widgets;

/// <summary>内存组件：圆环进度与分类堆叠条。</summary>
internal static class MemoryPainter
{
    public static void Paint(Graphics g, Size size, WidgetConfig c)
    {
        MonitorSnapshot snap = SystemMonitor.Snapshot;
        if (!snap.Ready || snap.MemTotalGb <= 0)
        {
            snap = SystemMonitor.Demo;
        }

        int w = size.Width;
        int h = size.Height;
        PaintKit.DrawPanelBackground(g, w, h, c);

        if (c.Style == "stack")
        {
            PaintStack(g, w, h, c, snap);
        }
        else
        {
            PaintRing(g, w, h, c, snap);
        }
    }

    private static void PaintRing(Graphics g, int w, int h, WidgetConfig c, MonitorSnapshot snap)
    {
        float radius = Math.Max(18f, Math.Min(w * 0.28f, h * 0.36f));
        float cx = Math.Max(radius + 8f, w * 0.24f);
        float cy = h / 2f;

        using (var track = new Pen(PaintKit.Alpha(c.Text, 24), Math.Max(4f, radius * 0.16f)))
        {
            g.DrawEllipse(track, cx - radius, cy - radius, radius * 2, radius * 2);
        }

        using (var arcPen = new Pen(c.Accent, Math.Max(4f, radius * 0.16f))
               {
                   StartCap = LineCap.Round,
                   EndCap = LineCap.Round
               })
        {
            g.DrawArc(arcPen, cx - radius, cy - radius, radius * 2, radius * 2,
                -90, Math.Clamp((float)snap.MemPercent, 0, 100) * 3.6f);
        }

        using Font pctFont = PaintKit.CreateFont(c.FontFamily, Math.Max(11f, radius * 0.42f), FontStyle.Bold);
        PaintKit.DrawText(g, Math.Round(snap.MemPercent) + "%", pctFont, c.Text,
            new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2),
            StringAlignment.Center, StringAlignment.Center);

        float rightX = cx + radius + 14;
        float rightW = w - rightX - 10;

        if (rightW > 70)
        {
            using Font bigFont = PaintKit.CreateFont(c.FontFamily, Math.Max(11f, c.FontSize * 0.46f),
                FontStyle.Bold);
            using Font smallFont = PaintKit.CreateFont(c.FontFamily, Math.Max(8f, c.FontSize * 0.24f));

            PaintKit.DrawText(g, snap.MemUsedGb.ToString("0.0") + " / " + snap.MemTotalGb.ToString("0") + " GB",
                bigFont, c.Text, new RectangleF(rightX, h * 0.3f, rightW, 26),
                StringAlignment.Near, StringAlignment.Center);
            PaintKit.DrawText(g, "已用 " + Math.Round(snap.MemPercent) + "%", smallFont,
                PaintKit.Alpha(c.Text, 120), new RectangleF(rightX, h * 0.46f, rightW, 18),
                StringAlignment.Near, StringAlignment.Center);

            var bar = new RectangleF(rightX, h * 0.66f, rightW, Math.Max(4f, h * 0.045f));
            using (GraphicsPath trackPath = Round.Create(Rectangle.Round(bar), (int)(bar.Height / 2)))
            using (var brush = new SolidBrush(PaintKit.Alpha(c.Text, 30)))
            {
                g.FillPath(brush, trackPath);
            }

            var fill = bar with { Width = (float)(bar.Width * snap.MemPercent / 100) };
            if (fill.Width > 1)
            {
                using GraphicsPath fillPath = Round.Create(Rectangle.Round(fill), (int)(bar.Height / 2));
                using var gradient = new LinearGradientBrush(bar, c.Accent, c.Accent2, 0f);
                g.FillPath(gradient, fillPath);
            }
        }
    }

    private static void PaintStack(Graphics g, int w, int h, WidgetConfig c, MonitorSnapshot snap)
    {
        float pad = Math.Max(10f, w * 0.055f);
        using Font labelFont = PaintKit.CreateFont(c.FontFamily, Math.Max(9f, c.FontSize * 0.28f));

        PaintKit.DrawText(g, "内存 " + Math.Round(snap.MemPercent) + "%", labelFont, PaintKit.Alpha(c.Text, 170),
            new RectangleF(pad, h * 0.07f, w - pad * 2, 16), StringAlignment.Near, StringAlignment.Center);
        PaintKit.DrawText(g, snap.MemUsedGb.ToString("0.0") + " / " + snap.MemTotalGb.ToString("0") + " GB",
            labelFont, PaintKit.Alpha(c.Text, 110),
            new RectangleF(pad, h * 0.07f, w - pad * 2, 16), StringAlignment.Far, StringAlignment.Center);

        var bar = new RectangleF(pad, h * 0.24f, w - pad * 2, Math.Max(10f, h * 0.1f));
        using (GraphicsPath track = Round.Create(Rectangle.Round(bar), (int)(bar.Height / 2)))
        using (var brush = new SolidBrush(PaintKit.Alpha(c.Text, 22)))
        {
            g.FillPath(brush, track);
        }

        double total = Math.Max(0.01, snap.MemTotalGb);
        double apps = Math.Max(0, snap.MemUsedGb - snap.MemKernelGb - snap.MemCacheGb);
        double kernel = Math.Max(0, snap.MemKernelGb);
        double cache = Math.Max(0, snap.MemCacheGb);
        double free = Math.Max(0, snap.MemFreeGb);

        (double Value, Color Color)[] parts =
        {
            (apps, c.Accent),
            (kernel, c.Accent2),
            (cache, Color.FromArgb(251, 191, 36)),
            (free, PaintKit.Alpha(c.Text, 40))
        };

        float x = bar.Left;

        using (GraphicsPath clip = Round.Create(Rectangle.Round(bar), (int)(bar.Height / 2)))
        {
            g.SetClip(clip);
            foreach ((double value, Color color) in parts)
            {
                float width = (float)(value / total * bar.Width);
                if (width <= 0.5f)
                {
                    continue;
                }

                using var brush = new SolidBrush(color);
                g.FillRectangle(brush, x, bar.Top, width, bar.Height);
                x += width;
            }

            g.ResetClip();
        }

        // 图例：两列，尽量把应用 / 内核 / 缓存 / 可用都露出来
        float legendTop = bar.Bottom + Math.Max(8f, h * 0.06f);
        float rowH = Math.Max(14f, (h - legendTop - pad) / 2f);
        float colW = (w - pad * 2) / 2f;

        (string Name, double Value, Color Color)[] legend =
        {
            ("应用", apps, c.Accent),
            ("内核", kernel, c.Accent2),
            ("缓存", cache, Color.FromArgb(251, 191, 36)),
            ("可用", free, PaintKit.Alpha(c.Text, 80))
        };

        for (int i = 0; i < legend.Length; i++)
        {
            float lx = pad + i % 2 * colW;
            float ly = legendTop + i / 2 * rowH;

            using (var dot = new SolidBrush(legend[i].Color))
            {
                g.FillRectangle(dot, lx, ly + rowH / 2f - 3, 6, 6);
            }

            PaintKit.DrawText(g, legend[i].Name + "  " + legend[i].Value.ToString("0.0") + " GB", labelFont,
                PaintKit.Alpha(c.Text, 150),
                new RectangleF(lx + 11, ly, colW - 14, rowH), StringAlignment.Near, StringAlignment.Center);
        }
    }
}
