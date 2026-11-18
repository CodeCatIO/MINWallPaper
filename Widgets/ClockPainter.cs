using System.Drawing;
using System.Drawing.Drawing2D;

namespace MINWallPaper.Widgets;

/// <summary>10 种时钟样式的 GDI+ 绘制。</summary>
internal static class ClockPainter
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
        string time = c.FormatTime(now);
        string date = c.FormatDate(now);
        string week = c.ShowWeek ? WidgetConfig.Weekday(now.DayOfWeek, false) : "";

        // 日期选「不显示」时，星期也一起隐藏，避免只剩半行。
        string sub = string.IsNullOrWhiteSpace(date)
            ? ""
            : string.Join("  ", new[] { date, week }.Where(s => !string.IsNullOrWhiteSpace(s)));

        float dateSize = Math.Max(9f, c.FontSize * 0.3f);

        using Font dateFont = PaintKit.CreateFont(c.FontFamily, dateSize);

        switch (c.Style)
        {
            case "flip":
                PaintFlip(g, w, h, c, time, sub, dateSize);
                break;
            case "analog":
                PaintAnalog(g, w, h, c, now, sub, dateSize);
                break;
            case "neon":
                PaintNeon(g, w, h, c, time, sub, dateFont);
                break;
            case "minimal":
                PaintMinimal(g, w, h, c, time, sub, dateFont);
                break;
            case "arc":
                PaintArc(g, w, h, c, now, time, sub, dateFont);
                break;
            case "capsule":
                PaintCapsule(g, w, h, c, time, sub, dateFont);
                break;
            case "led":
                PaintLed(g, w, h, c, time, sub, dateFont);
                break;
            case "poster":
                PaintPoster(g, w, h, c, time, sub, dateFont);
                break;
            case "outline":
                PaintOutline(g, w, h, c, time, sub, dateFont);
                break;
            default:
                PaintGlass(g, w, h, c, time, sub, dateFont);
                break;
        }
    }

    private static void PaintGlass(Graphics g, int w, int h, WidgetConfig c, string time, string sub,
        Font dateFont)
    {
        var rect = new Rectangle(0, 0, w - 1, h - 1);

        if (c.BgAlpha > 0.02)
        {
            PaintKit.DrawCard(g, rect, c.Radius, c.BgFill, 34);

            using GraphicsPath path = Round.Create(rect, c.Radius);
            g.SetClip(path);
            using var sheen = new LinearGradientBrush(rect,
                Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 118f);
            g.FillRectangle(sheen, rect);
            g.ResetClip();
        }

        float pad = w * 0.08f;
        using Font timeFont = PaintKit.FitFont(g, time, c.FontFamily, FontStyle.Bold,
            c.FontSize, w - pad * 2);

        float th = timeFont.GetHeight(g);
        float dh = string.IsNullOrEmpty(sub) ? 0 : dateFont.GetHeight(g) + 3;
        float gap = dh > 0 ? Math.Max(0f, (h - th - dh) * 0.14f) : 0f;
        float top = (h - (th + dh + gap)) / 2f;

        PaintKit.DrawText(g, time, timeFont, c.Text, new RectangleF(0, top, w, th),
            StringAlignment.Center, StringAlignment.Center);

        if (dh > 0)
        {
            PaintKit.DrawText(g, sub, dateFont, PaintKit.Alpha(c.Text, 168),
                new RectangleF(0, top + th + gap, w, dh), StringAlignment.Center, StringAlignment.Center);
        }
    }

    private static void PaintFlip(Graphics g, int w, int h, WidgetConfig c, string time, string sub,
        float dateSize)
    {
        float pad = Math.Min(w, h) * 0.06f;
        float availW = w - pad * 2;
        float dateH = string.IsNullOrEmpty(sub) ? 0 : dateSize + 8;
        float availH = h - pad * 1.2f - dateH;

        int digits = time.Count(char.IsDigit);
        int colons = time.Count(ch => ch == ':');

        if (digits == 0)
        {
            return;
        }

        float unit = availW / Math.Max(1f, digits * 1f + colons * 0.34f + (digits + colons - 1) * 0.14f);
        float tileW = Math.Min(unit, availH * 0.72f);
        float tileH = Math.Min(availH, tileW / 0.72f);
        tileW = tileH * 0.72f;

        float gap = tileW * 0.14f;
        float colonW = tileW * 0.34f;
        float totalW = digits * tileW + colons * colonW + (digits + colons - 1) * gap;
        float x = (w - totalW) / 2f;
        float gapY = dateH > 0 ? Math.Max(0f, (h - pad * 1.2f - tileH - dateH) * 0.35f) : 0f;
        float top = (h - (tileH + dateH + gapY)) / 2f;

        Color tileFill = Color.FromArgb((int)(Math.Max(c.BgAlpha, 0.6) * 235),
            PaintKit.Blend(c.Bg, Color.Black, 0.12f));
        using Font digitFont = PaintKit.CreateFont(c.FontFamily, tileH * 0.74f, FontStyle.Bold);

        foreach (char ch in time)
        {
            if (char.IsDigit(ch))
            {
                var tile = new RectangleF(x, top, tileW, tileH);

                if (c.BgAlpha > 0.02)
                {
                    using GraphicsPath path = Round.Create(Rectangle.Round(tile), Math.Min(12, (int)(tileW * 0.2f)));
                    using var brush = new SolidBrush(tileFill);
                    g.FillPath(brush, path);
                    using var edge = new Pen(Color.FromArgb(30, 255, 255, 255), 1f);
                    g.DrawPath(edge, path);
                }

                PaintKit.DrawText(g, ch.ToString(), digitFont, c.Text,
                    new RectangleF(tile.X, tile.Y - tileH * 0.02f, tile.Width, tile.Height),
                    StringAlignment.Center, StringAlignment.Center);

                x += tileW + gap;
            }
            else if (ch == ':')
            {
                float dot = Math.Max(2f, tileW * 0.09f);
                using var brush = new SolidBrush(PaintKit.Alpha(c.Text, 235));
                float cx = x + colonW / 2f - dot / 2f;
                g.FillEllipse(brush, cx, top + tileH * 0.32f, dot, dot);
                g.FillEllipse(brush, cx, top + tileH * 0.64f, dot, dot);
                x += colonW + gap;
            }
        }

        if (dateH > 0)
        {
            using Font dateFont = PaintKit.CreateFont(c.FontFamily, dateSize);
            PaintKit.DrawText(g, sub, dateFont, PaintKit.Alpha(c.Text, 150),
                new RectangleF(0, top + tileH + 6 + gapY, w, dateH), StringAlignment.Center, StringAlignment.Center);
        }
    }

    private static void PaintAnalog(Graphics g, int w, int h, WidgetConfig c, DateTime now, string sub,
        float dateSize)
    {
        float radius = Math.Min(w, h) * 0.45f;
        float cx = w / 2f;
        float cy = h / 2f;

        bool showDate = !string.IsNullOrEmpty(sub) && h > radius * 2f + dateSize + 10;
        if (showDate)
        {
            cy -= dateSize * 0.5f;
        }

        if (c.BgAlpha > 0.02)
        {
            using var disc = new SolidBrush(c.BgFill);
            g.FillEllipse(disc, cx - radius - 6, cy - radius - 6, (radius + 6) * 2, (radius + 6) * 2);
            using var edge = new Pen(Color.FromArgb(36, 255, 255, 255), 1f);
            g.DrawEllipse(edge, cx - radius - 6, cy - radius - 6, (radius + 6) * 2, (radius + 6) * 2);
        }

        for (int i = 0; i < 60; i++)
        {
            float angle = i / 60f * (float)Math.PI * 2 - (float)Math.PI / 2;
            bool hourTick = i % 5 == 0;
            float inner = radius - (hourTick ? radius * 0.1f : radius * 0.05f);

            using var pen = new Pen(hourTick
                ? PaintKit.Alpha(c.Text, 200)
                : PaintKit.Alpha(c.Text, 80), hourTick ? 2f : 1f);
            g.DrawLine(pen,
                cx + (float)Math.Cos(angle) * inner, cy + (float)Math.Sin(angle) * inner,
                cx + (float)Math.Cos(angle) * radius, cy + (float)Math.Sin(angle) * radius);
        }

        float seconds = now.Second + now.Millisecond / 1000f;
        float minutes = now.Minute + seconds / 60f;
        float hours = now.Hour % 12 + minutes / 60f;

        DrawHand(g, cx, cy, hours / 12f, radius * 0.5f, radius * 0.04f, c.Text);
        DrawHand(g, cx, cy, minutes / 60f, radius * 0.72f, radius * 0.026f, c.Text);
        DrawHand(g, cx, cy, seconds / 60f, radius * 0.82f, radius * 0.012f, c.Accent2);

        using (var dot = new SolidBrush(c.Accent2))
        {
            g.FillEllipse(dot, cx - radius * 0.035f, cy - radius * 0.035f, radius * 0.07f, radius * 0.07f);
        }

        if (showDate)
        {
            using Font dateFont = PaintKit.CreateFont(c.FontFamily, dateSize);
            PaintKit.DrawText(g, sub, dateFont, PaintKit.Alpha(c.Text, 150),
                new RectangleF(0, cy + radius * 0.42f, w, dateSize + 8),
                StringAlignment.Center, StringAlignment.Center);
        }
    }

    private static void DrawHand(Graphics g, float cx, float cy, float progress, float length, float width,
        Color color)
    {
        float angle = progress * (float)Math.PI * 2 - (float)Math.PI / 2;
        using var pen = new Pen(color, Math.Max(1.2f, width))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLine(pen, cx, cy, cx + (float)Math.Cos(angle) * length, cy + (float)Math.Sin(angle) * length);
    }

    private static void PaintNeon(Graphics g, int w, int h, WidgetConfig c, string time, string sub,
        Font dateFont)
    {
        if (c.BgAlpha > 0.02)
        {
            PaintKit.DrawCard(g, new Rectangle(0, 0, w - 1, h - 1), c.Radius, c.BgFill, 26);
        }

        float pad = w * 0.08f;
        using Font timeFont = PaintKit.FitFont(g, time, c.FontFamily, FontStyle.Bold,
            c.FontSize, w - pad * 2);

        float th = timeFont.GetHeight(g);
        float dh = string.IsNullOrEmpty(sub) ? 0 : dateFont.GetHeight(g) + 4;
        float gap = dh > 0 ? Math.Max(0f, (h - th - dh) * 0.14f) : 0f;
        float top = (h - (th + dh + gap)) / 2f;
        var rect = new RectangleF(0, top, w, th);

        // 没有真正的模糊，用几层偏移的半透明文字模拟灯管辉光。
        (float Dx, float Dy, int Alpha)[] glow =
        {
            (0, 0, 70), (2, 0, 55), (-2, 0, 55), (0, 2, 55), (0, -2, 55), (3, 3, 30), (-3, -3, 30)
        };

        foreach ((float dx, float dy, int alpha) in glow)
        {
            PaintKit.DrawText(g, time, timeFont, PaintKit.Alpha(c.Accent, alpha),
                new RectangleF(rect.X + dx, rect.Y + dy, rect.Width, rect.Height),
                StringAlignment.Center, StringAlignment.Center);
        }

        PaintKit.DrawText(g, time, timeFont, c.Text, rect, StringAlignment.Center, StringAlignment.Center);

        if (dh > 0)
        {
            PaintKit.DrawText(g, sub, dateFont, PaintKit.Alpha(c.Accent, 205),
                new RectangleF(0, top + th + gap, w, dh), StringAlignment.Center, StringAlignment.Center);
        }
    }

    private static void PaintMinimal(Graphics g, int w, int h, WidgetConfig c, string time, string sub,
        Font dateFont)
    {
        if (c.BgAlpha > 0.02)
        {
            PaintKit.DrawCard(g, new Rectangle(0, 0, w - 1, h - 1), c.Radius,
                PaintKit.Alpha(c.Bg, (int)(c.BgAlpha * 150)), 24);
        }

        using Font timeFont = PaintKit.FitFont(g, time, c.FontFamily, FontStyle.Regular,
            c.FontSize, w * 0.9f);

        float th = timeFont.GetHeight(g);
        float lineH = string.IsNullOrEmpty(sub) ? 0 : dateFont.GetHeight(g) + 8;
        float spread = lineH > 0 ? Math.Max(0f, (h - th - 14 - lineH) * 0.16f) : 0f;
        float top = (h - (th + 14 + lineH + spread)) / 2f;

        PaintKit.DrawSpacedText(g, time, timeFont, c.Text, w / 2f, top, c.FontSize * 0.12f);

        float lineY = top + th + 4;
        using (var pen = new Pen(c.Accent, Math.Max(1.5f, c.FontSize * 0.045f)))
        {
            float lineW = Math.Min(w * 0.24f, c.FontSize * 1.1f);
            g.DrawLine(pen, w / 2f - lineW / 2f, lineY, w / 2f + lineW / 2f, lineY);
        }

        if (lineH > 0)
        {
            PaintKit.DrawText(g, sub, dateFont, PaintKit.Alpha(c.Text, 150),
                new RectangleF(0, lineY + 8 + spread, w, lineH), StringAlignment.Center, StringAlignment.Center);
        }
    }

    private static void PaintArc(Graphics g, int w, int h, WidgetConfig c, DateTime now, string time,
        string sub, Font dateFont)
    {
        if (c.BgAlpha > 0.02)
        {
            PaintKit.DrawCard(g, new Rectangle(0, 0, w - 1, h - 1), c.Radius, c.BgFill, 26);
        }

        float cx = w / 2f;
        float cy = h / 2f;
        float radius = Math.Min(w, h) * 0.42f;

        using (var track = new Pen(PaintKit.Alpha(c.Text, 26), Math.Max(2f, radius * 0.075f)))
        {
            g.DrawEllipse(track, cx - radius, cy - radius, radius * 2, radius * 2);
        }

        float seconds = now.Second + now.Millisecond / 1000f;
        float minutes = now.Minute + seconds / 60f;
        float hours = now.Hour % 12 + minutes / 60f;

        DrawArcRing(g, cx, cy, radius, seconds / 60f, radius * 0.05f, c.Accent2);
        DrawArcRing(g, cx, cy, radius - radius * 0.1f, minutes / 60f, radius * 0.07f, c.Accent);
        DrawArcRing(g, cx, cy, radius - radius * 0.22f, hours / 12f, radius * 0.09f,
            PaintKit.Blend(c.Text, c.Accent, 0.35f));

        using Font timeFont = PaintKit.FitFont(g, time, c.FontFamily, FontStyle.Bold,
            Math.Min(c.FontSize * 0.72f, radius * 0.62f), radius * 1.1f);
        float th = timeFont.GetHeight(g);
        PaintKit.DrawText(g, time, timeFont, c.Text,
            new RectangleF(cx - radius, cy - radius * 0.5f, radius * 2, radius * 0.8f),
            StringAlignment.Center, StringAlignment.Center);

        if (!string.IsNullOrEmpty(sub))
        {
            PaintKit.DrawText(g, sub, dateFont, PaintKit.Alpha(c.Text, 150),
                new RectangleF(cx - radius, cy + radius * 0.34f, radius * 2, dateFont.GetHeight(g) + 6),
                StringAlignment.Center, StringAlignment.Center);
        }
    }

    private static void DrawArcRing(Graphics g, float cx, float cy, float radius, float progress,
        float width, Color color)
    {
        float sweep = Math.Clamp(progress, 0f, 1f) * 360f;
        if (sweep <= 0.5f)
        {
            return;
        }

        using var pen = new Pen(color, Math.Max(1.5f, width))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawArc(pen, cx - radius, cy - radius, radius * 2, radius * 2, -90, sweep);
    }

    private static void PaintCapsule(Graphics g, int w, int h, WidgetConfig c, string time, string sub,
        Font dateFont)
    {
        float pad = Math.Max(8, w * 0.05f);
        using Font timeFont = PaintKit.FitFont(g, time, c.FontFamily, FontStyle.Bold,
            c.FontSize, w - pad * 2 - 28);

        float capH = Math.Min(h * 0.6f, timeFont.GetHeight(g) * 1.7f);
        float dh = string.IsNullOrEmpty(sub) ? 0 : dateFont.GetHeight(g) + 8;
        float gap = dh > 0 ? Math.Max(0f, (h - capH - dh) * 0.1f) : 0f;
        float top = (h - (capH + dh + gap)) / 2f;
        var cap = new RectangleF(pad, top, w - pad * 2, capH);
        float radius = capH / 2f;

        using (GraphicsPath shadowPath = Round.Create(Rectangle.Round(new RectangleF(cap.X, cap.Y + 5, cap.Width, cap.Height)), (int)radius))
        {
            using var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
            g.FillPath(shadow, shadowPath);
        }

        using (GraphicsPath path = Round.Create(Rectangle.Round(cap), (int)radius))
        {
            using var gradient = new LinearGradientBrush(cap, c.Accent, c.Accent2, 22f);
            g.FillPath(gradient, path);

            g.SetClip(path);
            using (var gloss = new LinearGradientBrush(cap, Color.White, Color.White, 90f))
            {
                gloss.InterpolationColors = new ColorBlend
                {
                    Colors = new[]
                    {
                        Color.FromArgb(52, 255, 255, 255),
                        Color.FromArgb(10, 255, 255, 255),
                        Color.FromArgb(0, 255, 255, 255)
                    },
                    Positions = new[] { 0f, 0.55f, 1f }
                };
                g.FillRectangle(gloss, cap);
            }

            g.ResetClip();
        }

        PaintKit.DrawText(g, time, timeFont, Color.White, cap,
            StringAlignment.Center, StringAlignment.Center);

        if (dh > 0)
        {
            PaintKit.DrawText(g, sub, dateFont, PaintKit.Alpha(c.Text, 165),
                new RectangleF(0, top + capH + 6 + gap, w, dh), StringAlignment.Center, StringAlignment.Center);
        }
    }

    private static void PaintLed(Graphics g, int w, int h, WidgetConfig c, string time, string sub,
        Font dateFont)
    {
        if (c.BgAlpha > 0.02)
        {
            PaintKit.DrawCard(g, new Rectangle(0, 0, w - 1, h - 1), c.Radius,
                PaintKit.Alpha(c.Bg, (int)(Math.Max(c.BgAlpha, 0.75) * 255)), 30);
        }

        var cells = new List<char>();
        foreach (char ch in time)
        {
            if (char.IsDigit(ch) || ch == ':')
            {
                cells.Add(ch);
            }
        }

        if (cells.Count == 0)
        {
            return;
        }

        float pad = Math.Min(w, h) * 0.08f;
        float dateH = string.IsNullOrEmpty(sub) ? 0 : dateFont.GetHeight(g) + 6;
        float availW = w - pad * 2;
        float availH = h - pad * 1.4f - dateH;

        int digits = cells.Count(ch => ch != ':');
        int colons = cells.Count - digits;
        float units = digits * 1f + colons * 0.4f + (cells.Count - 1) * 0.16f;
        float unit = availW / Math.Max(1f, units);
        float digitW = Math.Min(unit, availH * 0.58f);
        float digitH = Math.Min(availH, digitW / 0.58f);
        digitW = digitH * 0.58f;

        float gap = digitW * 0.16f;
        float colonW = digitW * 0.4f;
        float totalW = digits * digitW + colons * colonW + (cells.Count - 1) * gap;
        float x = (w - totalW) / 2f;
        float gapY = dateH > 0 ? Math.Max(0f, (h - pad * 1.4f - digitH - dateH) * 0.35f) : 0f;
        float top = (h - (digitH + dateH + gapY)) / 2f;

        Color off = PaintKit.Alpha(PaintKit.Blend(c.Bg, c.Text, 0.12f), 42);
        bool colonOn = DateTime.Now.Second % 2 == 0;

        foreach (char ch in cells)
        {
            if (ch == ':')
            {
                float dot = Math.Max(2.5f, digitW * 0.13f);
                if (colonOn)
                {
                    using var glowBrush = new SolidBrush(PaintKit.Alpha(c.Accent, 70));
                    using var brush = new SolidBrush(c.Accent);
                    float cx = x + colonW / 2f - dot / 2f;
                    g.FillEllipse(glowBrush, cx - 1.5f, top + digitH * 0.3f - 1.5f, dot + 3, dot + 3);
                    g.FillEllipse(glowBrush, cx - 1.5f, top + digitH * 0.62f - 1.5f, dot + 3, dot + 3);
                    g.FillEllipse(brush, cx, top + digitH * 0.3f, dot, dot);
                    g.FillEllipse(brush, cx, top + digitH * 0.62f, dot, dot);
                }

                x += colonW + gap;
                continue;
            }

            DrawSevenSegment(g, new RectangleF(x, top, digitW, digitH), ch, c.Accent, off,
                Math.Max(2f, digitW * 0.14f));
            x += digitW + gap;
        }

        if (dateH > 0)
        {
            PaintKit.DrawText(g, sub, dateFont, PaintKit.Alpha(c.Text, 140),
                new RectangleF(0, top + digitH + 6 + gapY, w, dateH), StringAlignment.Center, StringAlignment.Center);
        }
    }

    private static readonly Dictionary<char, string> SegmentMap = new()
    {
        ['0'] = "abcdef",
        ['1'] = "bc",
        ['2'] = "abged",
        ['3'] = "abgcd",
        ['4'] = "fgbc",
        ['5'] = "afgcd",
        ['6'] = "afgecd",
        ['7'] = "abc",
        ['8'] = "abcdefg",
        ['9'] = "abfgcd"
    };

    private static void DrawSevenSegment(Graphics g, RectangleF rect, char digit, Color on, Color off,
        float thickness)
    {
        string lit = SegmentMap.TryGetValue(digit, out string? segments) ? segments : "";
        float w = rect.Width;
        float h = rect.Height;
        float t = thickness;
        float gap = t * 0.45f;
        float vLen = h / 2f - t - gap;

        (string Key, RectangleF Rect)[] parts =
        {
            ("a", new RectangleF(rect.X + gap, rect.Y, w - gap * 2, t)),
            ("b", new RectangleF(rect.Right - t, rect.Y + t + gap, t, vLen)),
            ("c", new RectangleF(rect.Right - t, rect.Y + h / 2f + gap, t, vLen)),
            ("d", new RectangleF(rect.X + gap, rect.Bottom - t, w - gap * 2, t)),
            ("e", new RectangleF(rect.X, rect.Y + h / 2f + gap, t, vLen)),
            ("f", new RectangleF(rect.X, rect.Y + t + gap, t, vLen)),
            ("g", new RectangleF(rect.X + gap, rect.Y + h / 2f - t / 2f, w - gap * 2, t))
        };

        foreach ((string key, RectangleF part) in parts)
        {
            bool active = lit.Contains(key);
            Color color = active ? on : off;

            if (active)
            {
                using var glow = new SolidBrush(PaintKit.Alpha(on, 55));
                using GraphicsPath glowPath = Round.Create(Rectangle.Round(new RectangleF(
                    part.X - 1.4f, part.Y - 1.4f, part.Width + 2.8f, part.Height + 2.8f)),
                    Math.Max(1, (int)(t / 2f)));
                g.FillPath(glow, glowPath);
            }

            using (GraphicsPath path = Round.Create(Rectangle.Round(part), Math.Max(1, (int)(t / 2f))))
            {
                using var brush = new SolidBrush(color);
                g.FillPath(brush, path);
            }
        }
    }

    private static void PaintPoster(Graphics g, int w, int h, WidgetConfig c, string time, string sub,
        Font dateFont)
    {
        if (c.BgAlpha > 0.02)
        {
            PaintKit.DrawCard(g, new Rectangle(0, 0, w - 1, h - 1), c.Radius, c.BgFill, 30);
        }

        bool wide = w >= 300;
        float pad = Math.Max(10f, w * 0.06f);
        float barW = Math.Max(4f, c.FontSize * 0.11f);

        float timeMax = wide ? w * 0.6f : w - pad * 2;
        using Font timeFont = PaintKit.FitFont(g, time, c.FontFamily, FontStyle.Bold,
            c.FontSize, timeMax);
        float th = timeFont.GetHeight(g);

        float subH = string.IsNullOrEmpty(sub) ? 0 : dateFont.GetHeight(g) + 2;
        float blockH = Math.Max(th, subH);

        if (wide)
        {
            float top = (h - blockH) / 2f;
            using (var gradient = new LinearGradientBrush(
                       new RectangleF(pad, top, barW, blockH), c.Accent2, c.Accent, 90f))
            using (GraphicsPath bar = Round.Create(
                       Rectangle.Round(new RectangleF(pad, top, barW, blockH)), (int)barW))
            {
                g.FillPath(gradient, bar);
            }

            float textX = pad + barW + 14;
            PaintKit.DrawText(g, time, timeFont, c.Text,
                new RectangleF(textX, top + (blockH - th) / 2f, timeMax, th),
                StringAlignment.Near, StringAlignment.Center);

            float rightW = Math.Max(60f, w - textX - w * 0.55f);
            float rightX = w - pad - rightW;
            float rightTop = top + (blockH - subH) / 2f;

            if (subH > 0)
            {
                PaintKit.DrawText(g, sub, dateFont, PaintKit.Alpha(c.Text, 170),
                    new RectangleF(rightX, rightTop, rightW, subH),
                    StringAlignment.Far, StringAlignment.Center);
            }
        }
        else
        {
            float contentH = th + subH + 8;
            float top = (h - contentH) / 2f;
            using (var gradient = new LinearGradientBrush(
                       new RectangleF(w / 2f - barW / 2f, top - 6, barW, 3f), c.Accent2, c.Accent, 0f))
            {
                g.FillRectangle(gradient, w / 2f - barW / 2f, top - 6, Math.Max(24f, w * 0.2f), barW * 0.5f);
            }

            PaintKit.DrawText(g, time, timeFont, c.Text,
                new RectangleF(0, top, w, th), StringAlignment.Center, StringAlignment.Center);

            if (subH > 0)
            {
                PaintKit.DrawText(g, sub, dateFont, PaintKit.Alpha(c.Text, 170),
                    new RectangleF(0, top + th, w, subH),
                    StringAlignment.Center, StringAlignment.Center);
            }
        }
    }

    private static void PaintOutline(Graphics g, int w, int h, WidgetConfig c, string time, string sub,
        Font dateFont)
    {
        if (c.BgAlpha > 0.02)
        {
            PaintKit.DrawCard(g, new Rectangle(0, 0, w - 1, h - 1), c.Radius, c.BgFill, 26);
        }

        float pad = w * 0.08f;
        using Font timeFont = PaintKit.FitFont(g, time, c.FontFamily, FontStyle.Bold,
            c.FontSize, w - pad * 2);

        using var path = new GraphicsPath();
        using (var format = new StringFormat(StringFormat.GenericTypographic))
        {
            path.AddString(time, timeFont.FontFamily, (int)FontStyle.Bold, timeFont.Size, PointF.Empty, format);
        }

        RectangleF bounds = path.GetBounds();
        float dateH = string.IsNullOrEmpty(sub) ? 0 : dateFont.GetHeight(g) + 6;
        float dx = (w - bounds.Width) / 2f - bounds.X;
        float dy = (h - bounds.Height - dateH) / 2f - bounds.Y;

        using (var matrix = new Matrix())
        {
            matrix.Translate(dx, dy);
            path.Transform(matrix);
        }

        using (GraphicsPath ghost = (GraphicsPath)path.Clone())
        using (var ghostMatrix = new Matrix())
        {
            ghostMatrix.Translate(4, 5);
            ghost.Transform(ghostMatrix);
            using var ghostBrush = new SolidBrush(PaintKit.Alpha(c.Accent, 70));
            g.FillPath(ghostBrush, ghost);
        }

        using (var pen = new Pen(c.Text, Math.Max(1.2f, c.FontSize * 0.045f)))
        {
            g.DrawPath(pen, path);
        }

        if (dateH > 0)
        {
            PaintKit.DrawText(g, sub, dateFont, PaintKit.Alpha(c.Text, 150),
                new RectangleF(0, h - dateH - pad * 0.4f, w, dateH),
                StringAlignment.Center, StringAlignment.Center);
        }
    }
}
