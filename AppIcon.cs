using System.Drawing;
using System.Windows.Forms;

namespace MINWallPaper;

/// <summary>应用图标：直接用编译进 exe 的图标，托盘和窗口共用。</summary>
internal static class AppIcon
{
    public static Icon Value { get; } =
        Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
}
