using System.Text;

namespace MINWallPaper.Native;

/// <summary>把窗口挂到 Windows 桌面壁纸层（桌面图标下方）。</summary>
internal static class DesktopHost
{
    /// <summary>
    /// 找到当前承载壁纸的宿主窗口。
    /// 兼容两种系统行为：
    ///   1. Win11 24H2 之后：WorkerW 是 Progman 的子窗口；
    ///   2. 更早版本：WorkerW 是顶层窗口，紧跟在 SHELLDLL_DefView 所在窗口之后。
    /// </summary>
    public static IntPtr FindHost()
    {
        IntPtr progman = NativeMethods.FindWindow("Progman", null);

        // 先看是否已经存在可用的 WorkerW，避免重复请求生成。
        IntPtr workerw = FindWorkerWUnderProgman(progman);

        if (workerw == IntPtr.Zero && progman != IntPtr.Zero)
        {
            NativeMethods.SendMessageTimeout(progman, NativeMethods.WM_SPAWN_WORKER,
                IntPtr.Zero, IntPtr.Zero, NativeMethods.SMTO_NORMAL, 1000, out _);
            workerw = FindWorkerWUnderProgman(progman);
        }

        if (workerw == IntPtr.Zero)
        {
            workerw = FindWorkerWBehindDesktopIcons();
        }

        return workerw != IntPtr.Zero ? workerw : progman;
    }

    /// <summary>把目标窗口挂到当前桌面宿主，并压到宿主里合适的位置。</summary>
    public static IntPtr Attach(IntPtr window)
    {
        IntPtr host = FindHost();

        if (host == IntPtr.Zero)
        {
            throw new InvalidOperationException("找不到桌面宿主窗口，无法挂载壁纸。");
        }

        NativeMethods.SetParent(window, host);

        // WorkerW 里可能还有系统自己的壁纸层：放到最上面才不会被盖住。
        // 回退到 Progman 时桌面图标在同一层级，仍然压到底部避免挡图标。
        IntPtr insertAfter = IsWorkerW(host) ? IntPtr.Zero : new IntPtr(1);
        NativeMethods.SetWindowPos(window, insertAfter, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        return host;
    }

    public static bool IsWorkerW(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var name = new StringBuilder(32);
        NativeMethods.GetClassName(hwnd, name, name.Capacity);
        return name.ToString() == "WorkerW";
    }

    /// <summary>Win11 24H2+：WorkerW 直接挂在 Progman 下面。</summary>
    private static IntPtr FindWorkerWUnderProgman(IntPtr progman)
    {
        if (progman == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        return NativeMethods.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
    }

    /// <summary>旧版 Windows：SHELLDLL_DefView 所在窗口的后面那个 WorkerW。</summary>
    private static IntPtr FindWorkerWBehindDesktopIcons()
    {
        IntPtr defViewHost = IntPtr.Zero;
        NativeMethods.EnumWindows((top, _) =>
        {
            if (NativeMethods.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            {
                defViewHost = top;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return defViewHost != IntPtr.Zero
            ? NativeMethods.FindWindowEx(IntPtr.Zero, defViewHost, "WorkerW", null)
            : IntPtr.Zero;
    }
}
