using System.Drawing;
using System.Drawing.Drawing2D;

namespace MINWallPaper;

/// <summary>圆角矩形路径辅助，桌面组件绘制圆角时共用。</summary>
internal static class Round
{
    public static GraphicsPath Create(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;

        if (d <= 0 || rect.Width <= 0 || rect.Height <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }

        d = Math.Min(d, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
