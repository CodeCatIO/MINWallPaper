using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace MINWallPaper.Widgets;

/// <summary>独立日期组件：签名手写 / 极简细体。</summary>
internal static class DatePainter
{
    public static void Paint(Graphics g, Size size, WidgetConfig c)
    {
        int w = size.Width;
        int h = size.Height;

        if (w < 8 || h < 8)
        {
            return;
        }

        DateTime now = DateTime.Now;
        string date = c.FormatDate(now);

        if (string.IsNullOrEmpty(date))
        {
            return;
        }

        if (c.BgAlpha > 0.02)
        {
            PaintKit.DrawPanelBackground(g, w, h, c);
        }

        string dateFamily = c.DateFormat is "en" or "enWeek" ? "Bodoni MT" : c.FontFamily;
        int textAlpha = (int)Math.Round(Math.Clamp(c.TextAlpha, 0, 1) * 255);

        if (c.Style == "minimal")
        {
            using Font font = PaintKit.FitFont(g, date, dateFamily, FontStyle.Regular, c.FontSize, w * 0.88f);
            float height = font.GetHeight(g);
            PaintKit.DrawSpacedText(g, date, font, PaintKit.Alpha(c.Text, Math.Min(230, textAlpha)), w / 2f,
                (h - height) / 2f, 1.6f);
            return;
        }

        // 签名手写：背后放一个放大的英文星期，前面是金色日期
        int signatureAlpha = (int)Math.Round(Math.Clamp(c.SignatureAlpha, 0, 1) * 255);
        string weekday = now.ToString("dddd", CultureInfo.InvariantCulture);
        using Font script = PaintKit.FitFont(g, weekday, PaintKit.ScriptFamily, FontStyle.Italic,
            h * 0.66f, w * 0.9f);

        GraphicsState state = g.Save();
        g.RotateTransform(-4f);
        PaintKit.DrawText(g, weekday, script, PaintKit.Alpha(c.Signature, signatureAlpha),
            new RectangleF(w * 0.03f, -h * 0.01f, w * 0.94f, h * 0.72f),
            StringAlignment.Near, StringAlignment.Center);
        g.Restore(state);

        using Font dateFont = PaintKit.FitFont(g, date, dateFamily, FontStyle.Regular,
            c.FontSize * 0.95f, w * 0.78f);

        PaintKit.DrawGoldText(g, date, dateFont,
            new RectangleF(0, h * 0.52f, w, dateFont.GetHeight(g)),
            StringAlignment.Center, StringAlignment.Center, textAlpha);

        using var pen = new Pen(PaintKit.Alpha(c.Accent, 160), 1.2f);
        g.DrawLine(pen, w * 0.32f, h * 0.48f, w * 0.68f, h * 0.48f);
    }
}
