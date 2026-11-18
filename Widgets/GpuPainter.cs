using System.Drawing;
using System.Drawing.Drawing2D;

namespace MINWallPaper.Widgets;

/// <summary>GPU 组件：温度占用、半圆仪表、信息卡。</summary>
internal static class GpuPainter
{
    public static void Paint(Graphics g, Size size, WidgetConfig c)
    {
        int w = size.Width;
        int h = size.Height;
        PaintKit.DrawPanelBackground(g, w, h, c);

        MonitorSnapshot snap = SystemMonitor.Snapshot;

        if (!snap.GpuAvailable)
        {
            using Font titleFont = PaintKit.CreateFont(c.FontFamily, Math.Max(11f, c.FontSize * 0.42f),
                FontStyle.Bold);
            using Font smallFont = PaintKit.CreateFont(c.FontFamily, Math.Max(8f, c.FontSize * 0.24f));
            PaintKit.DrawText(g, "GPU", titleFont, PaintKit.Alpha(c.Text, 190),
                new RectangleF(0, h * 0.32f, w, 24), StringAlignment.Center, StringAlignment.Center);
            PaintKit.DrawText(g, "未检测到可读取的显卡", smallFont, PaintKit.Alpha(c.Text, 100),
                new RectangleF(0, h * 0.5f, w, 18), StringAlignment.Center, StringAlignment.Center);
            return;
        }

        switch (c.Style)
        {
            case "ring":
                PaintRing(g, w, h, c, snap);
                break;
            case "info":
                PaintInfo(g, w, h, c, snap);
                break;
            default:
                PaintBars(g, w, h, c, snap);
                break;
        }
    }

    private static void PaintBars(Graphics g, int w, int h, WidgetConfig c, MonitorSnapshot snap)
    {
        float pad = Math.Max(10f, w * 0.06f);
        using Font labelFont = PaintKit.CreateFont(c.FontFamily, Math.Max(8f, c.FontSize * 0.26f));
        using Font numFont = PaintKit.CreateFont(c.FontFamily, Math.Max(14f, c.FontSize * 0.95f),
            FontStyle.Bold);

        string main = snap.GpuTempC > 0
            ? Math.Round(snap.GpuTempC).ToString("0")
            : Math.Round(snap.GpuPercent).ToString("0");
        float mainH = numFont.GetHeight(g);
        PaintKit.DrawText(g, main, numFont, c.Text,
            new RectangleF(pad, h * 0.08f, w - pad * 2, mainH), StringAlignment.Near, StringAlignment.Center);
        PaintKit.DrawText(g, snap.GpuTempC > 0 ? "°C" : "%", labelFont, c.Accent,
            new RectangleF(pad + 52, h * 0.12f, 40, mainH), StringAlignment.Near, StringAlignment.Center);

        using Font smallFont = PaintKit.CreateFont(c.FontFamily, Math.Max(8f, c.FontSize * 0.22f));
        PaintKit.DrawText(g, string.IsNullOrWhiteSpace(snap.GpuName) ? "GPU" : snap.GpuName, smallFont,
            PaintKit.Alpha(c.Text, 100),
            new RectangleF(w * 0.45f, h * 0.14f, w * 0.49f - pad, 18), StringAlignment.Far,
            StringAlignment.Center);

        float top = h * 0.46f;
        DrawUsageRow(g, pad, top, w - pad * 2, h * 0.1f, "核心占用",
            Math.Round(snap.GpuPercent) + "%", snap.GpuPercent / 100f, c.Accent, labelFont, c.Text);

        double vramPercent = snap.GpuMemTotalGb > 0 ? snap.GpuMemUsedGb / snap.GpuMemTotalGb * 100 : 0;
        DrawUsageRow(g, pad, top + h * 0.24f, w - pad * 2, h * 0.1f, "显存",
            snap.GpuMemUsedGb.ToString("0.0") + " / " + snap.GpuMemTotalGb.ToString("0") + " GB",
            (float)vramPercent / 100f, c.Accent2, labelFont, c.Text);
    }

    private static void DrawUsageRow(Graphics g, float x, float y, float width, float height, string label,
        string value, float percent, Color color, Font font, Color textColor)
    {
        PaintKit.DrawText(g, label, font, PaintKit.Alpha(textColor, 150),
            new RectangleF(x, y, width * 0.5f, 16), StringAlignment.Near, StringAlignment.Center);
        PaintKit.DrawText(g, value, font, PaintKit.Alpha(textColor, 120),
            new RectangleF(x + width * 0.5f, y, width * 0.5f, 16), StringAlignment.Far, StringAlignment.Center);

        var bar = new RectangleF(x, y + 18, width, Math.Max(3f, height * 0.28f));
        using (GraphicsPath track = Round.Create(Rectangle.Round(bar), (int)(bar.Height / 2)))
        using (var brush = new SolidBrush(PaintKit.Alpha(textColor, 26)))
        {
            g.FillPath(brush, track);
        }

        var fill = bar with { Width = bar.Width * Math.Clamp(percent, 0, 1) };
        if (fill.Width > 1)
        {
            using GraphicsPath path = Round.Create(Rectangle.Round(fill), (int)(bar.Height / 2));
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
    }

    private static void PaintRing(Graphics g, int w, int h, WidgetConfig c, MonitorSnapshot snap)
    {
        float radius = Math.Max(24f, Math.Min(w * 0.34f, h * 0.42f));
        float cx = w / 2f;
        float cy = h * 0.58f;
        var rect = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);

        using (var track = new Pen(PaintKit.Alpha(c.Text, 24), Math.Max(4f, radius * 0.16f))
               {
                   StartCap = LineCap.Round,
                   EndCap = LineCap.Round
               })
        {
            g.DrawArc(track, rect, 180, 180);
        }

        using (var arcPen = new Pen(c.Accent, Math.Max(4f, radius * 0.16f))
               {
                   StartCap = LineCap.Round,
                   EndCap = LineCap.Round
               })
        {
            g.DrawArc(arcPen, rect, 180, Math.Clamp(snap.GpuPercent, 0, 100) * 1.8f);
        }

        using Font pctFont = PaintKit.CreateFont(c.FontFamily, Math.Max(12f, radius * 0.4f), FontStyle.Bold);
        PaintKit.DrawText(g, Math.Round(snap.GpuPercent) + "%", pctFont, c.Text,
            new RectangleF(cx - radius, cy - radius * 0.55f, radius * 2, radius * 0.7f),
            StringAlignment.Center, StringAlignment.Center);

        using Font smallFont = PaintKit.CreateFont(c.FontFamily, Math.Max(8f, c.FontSize * 0.24f));
        string sub = snap.GpuTempC > 0
            ? Math.Round(snap.GpuTempC) + "°C · 显存 " + snap.GpuMemUsedGb.ToString("0.0") + "G"
            : "显存 " + snap.GpuMemUsedGb.ToString("0.0") + "G";
        PaintKit.DrawText(g, sub, smallFont, PaintKit.Alpha(c.Text, 110),
            new RectangleF(0, cy + 8, w, 18), StringAlignment.Center, StringAlignment.Center);
    }

    private static void PaintInfo(Graphics g, int w, int h, WidgetConfig c, MonitorSnapshot snap)
    {
        float pad = Math.Max(10f, w * 0.06f);
        using Font nameFont = PaintKit.CreateFont(c.FontFamily, Math.Max(9f, c.FontSize * 0.34f),
            FontStyle.Bold);
        using Font rowFont = PaintKit.CreateFont(c.FontFamily, Math.Max(8f, c.FontSize * 0.25f));

        PaintKit.DrawText(g, string.IsNullOrWhiteSpace(snap.GpuName) ? "GPU" : snap.GpuName, nameFont,
            c.Text, new RectangleF(pad, h * 0.1f, w - pad * 2, 22), StringAlignment.Near,
            StringAlignment.Center);

        string[] rows =
        {
            "占用   " + Math.Round(snap.GpuPercent) + "%",
            "温度   " + (snap.GpuTempC > 0 ? Math.Round(snap.GpuTempC) + " °C" : "--"),
            "显存   " + snap.GpuMemUsedGb.ToString("0.0") + " / " + snap.GpuMemTotalGb.ToString("0") + " GB"
        };

        for (int i = 0; i < rows.Length; i++)
        {
            PaintKit.DrawText(g, rows[i], rowFont, PaintKit.Alpha(c.Text, 150),
                new RectangleF(pad, h * 0.38f + i * h * 0.19f, w - pad * 2, 18),
                StringAlignment.Near, StringAlignment.Center);
        }
    }
}
