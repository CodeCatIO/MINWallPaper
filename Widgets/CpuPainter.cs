using System.Drawing;
using System.Drawing.Drawing2D;

namespace MINWallPaper.Widgets;

/// <summary>CPU 组件：数字折线、环形仪表、核心方块。</summary>
internal static class CpuPainter
{
    public static void Paint(Graphics g, Size size, WidgetConfig c)
    {
        MonitorSnapshot snap = SystemMonitor.Snapshot;
        if (!snap.Ready || snap.CpuCores.Length == 0)
        {
            snap = SystemMonitor.Demo;
        }

        int w = size.Width;
        int h = size.Height;
        PaintKit.DrawPanelBackground(g, w, h, c);

        switch (c.Style)
        {
            case "ring":
                PaintRing(g, w, h, c, snap);
                break;
            case "cores":
                PaintCores(g, w, h, c, snap);
                break;
            default:
                PaintLine(g, w, h, c, snap);
                break;
        }
    }

    private static void PaintLine(Graphics g, int w, int h, WidgetConfig c, MonitorSnapshot snap)
    {
        float pad = Math.Max(10f, w * 0.06f);
        using Font labelFont = PaintKit.CreateFont(c.FontFamily, Math.Max(9f, c.FontSize * 0.28f));
        using Font numFont = PaintKit.FitFont(g, "100", c.FontFamily, FontStyle.Bold,
            Math.Min(c.FontSize * 1.5f, h * 0.34f), w - pad * 2 - 30);

        PaintKit.DrawText(g, "CPU", labelFont, PaintKit.Alpha(c.Text, 150),
            new RectangleF(pad, h * 0.06f, w * 0.4f, 16), StringAlignment.Near, StringAlignment.Center);

        float numberTop = h * 0.14f;
        float numberH = numFont.GetHeight(g);
        PaintKit.DrawText(g, Math.Round(snap.CpuPercent).ToString("0"), numFont, c.Text,
            new RectangleF(pad, numberTop, w - pad * 2 - 28, numberH),
            StringAlignment.Near, StringAlignment.Center);

        float numberWidth = g.MeasureString(Math.Round(snap.CpuPercent).ToString("0"), numFont,
            PointF.Empty, StringFormat.GenericTypographic).Width;
        using Font unitFont = PaintKit.CreateFont(c.FontFamily, Math.Max(11f, c.FontSize * 0.42f));
        PaintKit.DrawText(g, "%", unitFont, c.Accent,
            new RectangleF(pad + numberWidth + 3, numberTop, 28, numberH),
            StringAlignment.Near, StringAlignment.Center);

        string sub = snap.Threads + " 线程";
        PaintKit.DrawText(g, sub, labelFont, PaintKit.Alpha(c.Text, 120),
            new RectangleF(w * 0.42f, h * 0.16f, w * 0.52f, numberH),
            StringAlignment.Far, StringAlignment.Center);

        var chart = new RectangleF(pad, h * 0.58f, w - pad * 2, Math.Max(12f, h * 0.32f));
        DrawHistory(g, chart, snap.CpuHistory, c.Accent);
    }

    private static void DrawHistory(Graphics g, RectangleF rect, float[] history, Color accent)
    {
        if (history.Length < 2)
        {
            return;
        }

        using var line = new GraphicsPath();
        using var fill = new GraphicsPath();

        for (int i = 0; i < history.Length; i++)
        {
            float x = rect.Left + i / (float)(history.Length - 1) * rect.Width;
            float y = rect.Bottom - Math.Clamp(history[i], 0, 100) / 100f * rect.Height;

            if (i == 0)
            {
                line.AddLine(x, y, x, y);
                fill.AddLine(x, rect.Bottom, x, y);
            }
            else
            {
                line.AddLine(x, y, x, y);
                fill.AddLine(x, y, x, y);
            }
        }

        fill.AddLine(rect.Right, rect.Bottom, rect.Left, rect.Bottom);
        fill.CloseFigure();

        using (var gradient = new LinearGradientBrush(rect,
                   PaintKit.Alpha(accent, 88), PaintKit.Alpha(accent, 0), 90f))
        {
            g.FillPath(gradient, fill);
        }

        using var pen = new Pen(accent, 1.6f) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, line);
    }

    private static void PaintRing(Graphics g, int w, int h, WidgetConfig c, MonitorSnapshot snap)
    {
        float radius = Math.Max(18f, Math.Min(w * 0.3f, h * 0.4f));
        float cx = Math.Max(radius + 8f, w * 0.26f);
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
                -90, Math.Clamp(snap.CpuPercent, 0, 100) * 3.6f);
        }

        using Font pctFont = PaintKit.CreateFont(c.FontFamily, Math.Max(11f, radius * 0.44f), FontStyle.Bold);
        PaintKit.DrawText(g, Math.Round(snap.CpuPercent) + "%", pctFont, c.Text,
            new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2),
            StringAlignment.Center, StringAlignment.Center);

        float rightX = cx + radius + 14;
        float rightW = w - rightX - 10;

        if (rightW > 60)
        {
            using Font titleFont = PaintKit.CreateFont(c.FontFamily, Math.Max(10f, c.FontSize * 0.38f),
                FontStyle.Bold);
            using Font smallFont = PaintKit.CreateFont(c.FontFamily, Math.Max(8f, c.FontSize * 0.24f));

            PaintKit.DrawText(g, "CPU", titleFont, c.Text,
                new RectangleF(rightX, h * 0.3f, rightW, 20), StringAlignment.Near, StringAlignment.Center);
            PaintKit.DrawText(g, snap.Threads + " 线程", smallFont, PaintKit.Alpha(c.Text, 120),
                new RectangleF(rightX, h * 0.46f, rightW, 18), StringAlignment.Near, StringAlignment.Center);
            PaintKit.DrawText(g, string.IsNullOrWhiteSpace(snap.CpuName) ? "处理器" : snap.CpuName,
                smallFont, PaintKit.Alpha(c.Text, 90),
                new RectangleF(rightX, h * 0.62f, rightW, 18), StringAlignment.Near, StringAlignment.Center);
        }
    }

    private static void PaintCores(Graphics g, int w, int h, WidgetConfig c, MonitorSnapshot snap)
    {
        float pad = Math.Max(10f, w * 0.05f);
        using Font labelFont = PaintKit.CreateFont(c.FontFamily, Math.Max(9f, c.FontSize * 0.28f));

        PaintKit.DrawText(g, "CPU " + Math.Round(snap.CpuPercent) + "%", labelFont, PaintKit.Alpha(c.Text, 170),
            new RectangleF(pad, h * 0.06f, w - pad * 2, 16), StringAlignment.Near, StringAlignment.Center);
        PaintKit.DrawText(g, snap.CpuCores.Length + " 核心", labelFont, PaintKit.Alpha(c.Text, 100),
            new RectangleF(pad, h * 0.06f, w - pad * 2, 16), StringAlignment.Far, StringAlignment.Center);

        int count = Math.Max(1, snap.CpuCores.Length);
        int columns = Math.Clamp((int)Math.Ceiling(Math.Sqrt(count * 1.7)), 3, 8);
        int rows = (int)Math.Ceiling(count / (double)columns);

        float gridTop = h * 0.24f;
        float gridH = h - gridTop - pad;
        float cellW = (w - pad * 2) / columns;
        float cellH = gridH / rows;
        float gap = Math.Min(5f, cellW * 0.18f);

        for (int i = 0; i < count; i++)
        {
            int col = i % columns;
            int row = i / columns;
            var rect = new RectangleF(
                pad + col * cellW + gap / 2,
                gridTop + row * cellH + gap / 2,
                cellW - gap,
                cellH - gap);

            float usage = Math.Clamp(snap.CpuCores[i], 0f, 1f);
            Color color = usage > 0.72f
                ? Color.FromArgb(239, 113, 113)
                : usage > 0.4f
                    ? c.Accent2
                    : c.Accent;

            using (var brush = new SolidBrush(PaintKit.Alpha(color, (int)(70 + usage * 170))))
            using (GraphicsPath path = Round.Create(Rectangle.Round(rect), 3))
            {
                g.FillPath(brush, path);
            }
        }
    }
}
