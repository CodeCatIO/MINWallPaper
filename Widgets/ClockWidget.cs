using System.Drawing;
using System.Drawing.Drawing2D;

namespace MINWallPaper.Widgets;

/// <summary>时钟组件：半透明圆角卡片上显示时间和日期。</summary>
internal sealed class ClockWidget : WidgetWindow
{
    private readonly Font _timeFont = new("Microsoft YaHei UI", 34F, FontStyle.Bold);
    private readonly Font _dateFont = new("Microsoft YaHei UI", 10F);
    private readonly Font _weekFont = new("Microsoft YaHei UI", 9F);

    public ClockWidget() : base(300, 116, 1000)
    {
    }

    protected override void Paint(Graphics g, Size size)
    {
        var rect = new Rectangle(0, 0, size.Width - 1, size.Height - 1);
        using GraphicsPath path = Round.Create(rect, 22);

        // 玻璃感：深色半透明底 + 一圈更亮的描边。
        using (var fill = new SolidBrush(Color.FromArgb(122, 18, 20, 26)))
        {
            g.FillPath(fill, path);
        }

        using (var edge = new Pen(Color.FromArgb(46, 255, 255, 255), 1.2f))
        {
            g.DrawPath(edge, path);
        }

        DateTime now = DateTime.Now;

        TextRenderer.DrawText(g, now.ToString("HH:mm"), _timeFont,
            new Rectangle(0, 14, size.Width, 58), Color.FromArgb(250, 250, 252),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);

        TextRenderer.DrawText(g, now.ToString("M月d日"), _dateFont,
            new Rectangle(0, 76, size.Width, 22), Color.FromArgb(206, 210, 220),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);

        TextRenderer.DrawText(g, WeekName(now.DayOfWeek), _weekFont,
            new Rectangle(0, 94, size.Width, 18), Color.FromArgb(150, 154, 166),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
    }

    private static string WeekName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "星期一",
        DayOfWeek.Tuesday => "星期二",
        DayOfWeek.Wednesday => "星期三",
        DayOfWeek.Thursday => "星期四",
        DayOfWeek.Friday => "星期五",
        DayOfWeek.Saturday => "星期六",
        _ => "星期日"
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timeFont.Dispose();
            _dateFont.Dispose();
            _weekFont.Dispose();
        }

        base.Dispose(disposing);
    }
}
