using System.Drawing;
using System.Windows.Forms;
using MINWallPaper.Native;

namespace MINWallPaper;

/// <summary>
/// 单实例控制：重复启动时不新开进程，而是唤起已经运行的主面板。
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\MINWallPaper.SingleInstance";
    private const string WindowTitle = "MINWallPaper.Activate.3F6C1A2B";
    private const int WM_ACTIVATE_PANEL = 0x8000 + 0x201;

    /// <summary>第一个实例拿到互斥体；后来者返回 false。</summary>
    public static bool TryAcquire(out Mutex mutex)
    {
        mutex = new Mutex(true, MutexName, out bool created);
        return created;
    }

    /// <summary>通知已经运行的实例打开主面板，并允许它抢到前台。</summary>
    public static void ActivateExisting()
    {
        // 第一个实例可能刚启动，窗口还没建好，短暂重试几次。
        for (int i = 0; i < 15; i++)
        {
            IntPtr hwnd = NativeMethods.FindWindow(null, WindowTitle);

            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                NativeMethods.AllowSetForegroundWindow((int)pid);
                NativeMethods.PostMessage(hwnd, WM_ACTIVATE_PANEL, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            Thread.Sleep(100);
        }
    }

    internal static string ActivationWindowTitle => WindowTitle;

    internal static int ActivationMessage => WM_ACTIVATE_PANEL;
}

/// <summary>
/// 只负责接收"唤起面板"消息的隐藏窗口。
/// 不 Show，只创建句柄，不会出现在任务栏或 Alt+Tab。
/// </summary>
internal sealed class ActivationWindow : Form
{
    private readonly Action _onActivate;

    public ActivationWindow(Action onActivate)
    {
        _onActivate = onActivate;
        Text = SingleInstance.ActivationWindowTitle;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-32000, -32000, 1, 1);
        CreateHandle();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == SingleInstance.ActivationMessage)
        {
            _onActivate();
            return;
        }

        base.WndProc(ref m);
    }
}
