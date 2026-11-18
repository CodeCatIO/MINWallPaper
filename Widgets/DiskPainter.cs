using System.Drawing;
using System.Drawing.Drawing2D;

namespace MINWallPaper.Widgets;

/// <summary>磁盘组件：分区条与甜甜圈。</summary>
internal static class DiskPainter
{
    private static readonly Color[] Palette =
    {
        Color.FromArgb(236, 72, 153),
        Color.FromArgb(59, 130, 246),
        Color.FromArgb(52, 211, 153),
        Color.FromArgb(251, 191, 36),
        Color.FromArgb(167, 139, 250),
        Color.FromArgb(244, 114, 182)
    };

    public static void Paint(Graphics g, Size size, WidgetConfig c)
    {
        int w = size.Width;
        int h = size.Height;
        PaintKit.DrawPanelBackground(g, w, h, c);

        MonitorSnapshot snap = SystemMonitor.Snapshot;
        List<DiskSnapshot> disks = snap.Disks.Count > 0 ? snap.Disks : SystemMonitor.Demo.Disks;

        if (c.Style == "donut")
        {
            PaintDonut(g, w, h, c, disks);
        }
        else
        {
            PaintBars(g, w, h, c, disks);
        }
    }

    private static void PaintBars(Graphics g, int w, int h, WidgetConfig c, List<DiskSnapshot> disks)
    {
        float pad = Math.Max(10f, w * 0.05f);
        using Font nameFont = PaintKit.CreateFont(c.FontFamily, Math.Max(9f, c.FontSize * 0.27f),
            FontStyle.Bold);
        using Font valueFont = PaintKit.CreateFont(c.FontFamily, Math.Max(8f, c.FontSize * 0.22f));

        int count = Math.Min(disks.Count, 5);
        float rowH = (h - pad * 2) / Math.Max(1, count);

        for (int i = 0; i < count; i++)
        {
            DiskSnapshot disk = disks[i];
            float top = pad + i * rowH;
            string title = disk.Name + (string.IsNullOrWhiteSpace(disk.Label) ? "" : "  " + disk.Label);

            PaintKit.DrawText(g, title, nameFont, PaintKit.Alpha(c.Text, 190),
                new RectangleF(pad, top, w * 0.55f, 16), StringAlignment.Near, StringAlignment.Center);
            PaintKit.DrawText(g, disk.UsedGb.ToString("0") + " / " + disk.TotalGb.ToString("0") + " GB",
                valueFont, PaintKit.Alpha(c.Text, 110),
                new RectangleF(w * 0.45f, top, w * 0.5f - pad, 16), StringAlignment.Far,
                StringAlignment.Center);

            var bar = new RectangleF(pad, top + 19, w - pad * 2, Math.Max(4f, rowH * 0.16f));
            using (GraphicsPath track = Round.Create(Rectangle.Round(bar), (int)(bar.Height / 2)))
            using (var brush = new SolidBrush(PaintKit.Alpha(c.Text, 24)))
            {
                g.FillPath(brush, track);
            }

            var fill = bar with { Width = bar.Width * Math.Clamp(disk.Percent / 100f, 0, 1) };
            if (fill.Width > 1)
            {
                Color color = i == 0 ? c.Accent : i == 1 ? c.Accent2 : Palette[i % Palette.Length];
                using GraphicsPath path = Round.Create(Rectangle.Round(fill), (int)(bar.Height / 2));
                using var brush = new SolidBrush(color);
                g.FillPath(brush, path);
            }
        }
    }

    private static void PaintDonut(Graphics g, int w, int h, WidgetConfig c, List<DiskSnapshot> disks)
    {
        float radius = Math.Max(24f, Math.Min(w * 0.22f, h * 0.36f));
        float cx = radius + Math.Max(18f, w * 0.08f);
        float cy = h / 2f;
        float thickness = Math.Max(8f, radius * 0.34f);
        var rect = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);

        using (var track = new Pen(PaintKit.Alpha(c.Text, 22), thickness))
        {
            g.DrawEllipse(track, rect);
        }

        double total = Math.Max(0.01, disks.Sum(d => d.TotalGb));
        double usedTotal = disks.Sum(d => d.UsedGb);
        float angle = -90f;

        for (int i = 0; i < disks.Count; i++)
        {
            float sweep = (float)(disks[i].TotalGb / total * 360);
            Color color = i == 0 ? c.Accent : i == 1 ? c.Accent2 : Palette[i % Palette.Length];

            using (var pen = new Pen(color, thickness)
                   {
                       StartCap = LineCap.Flat,
                       EndCap = LineCap.Flat
                   })
            {
                g.DrawArc(pen, rect, angle + 1.5f, Math.Max(1f, sweep - 3f));
            }

            angle += sweep;
        }

        using Font centerFont = PaintKit.CreateFont(c.FontFamily, Math.Max(10f, radius * 0.3f),
            FontStyle.Bold);
        PaintKit.DrawText(g, usedTotal.ToString("0") + "G", centerFont, c.Text,
            new RectangleF(cx - radius, cy - radius * 0.4f, radius * 2, radius * 0.7f),
            StringAlignment.Center, StringAlignment.Center);

        using Font smallFont = PaintKit.CreateFont(c.FontFamily, Math.Max(7f, radius * 0.17f));
        PaintKit.DrawText(g, "已用", smallFont, PaintKit.Alpha(c.Text, 100),
            new RectangleF(cx - radius, cy + radius * 0.06f, radius * 2, 14),
            StringAlignment.Center, StringAlignment.Center);

        // 右侧图例
        float legendX = cx + radius + thickness + 16;
        float legendW = w - legendX - 10;
        int visible = Math.Min(disks.Count, 4);

        for (int i = 0; i < visible && legendW > 60; i++)
        {
            float ly = h * 0.24f + i * (h * 0.16f);
            Color color = i == 0 ? c.Accent : i == 1 ? c.Accent2 : Palette[i % Palette.Length];

            using (var dot = new SolidBrush(color))
            {
                g.FillRectangle(dot, legendX, ly + 5, 7, 7);
            }

            PaintKit.DrawText(g, disks[i].Name + "  " + disks[i].TotalGb.ToString("0") + "G",
                smallFont, PaintKit.Alpha(c.Text, 150),
                new RectangleF(legendX + 12, ly, legendW, 18), StringAlignment.Near,
                StringAlignment.Center);
        }
    }
}
