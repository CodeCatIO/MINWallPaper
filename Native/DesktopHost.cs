namespace MINWallPaper.Native;

/// <summary>把窗口挂到 Windows 桌面壁纸层（桌面图标下方）。</summary>
internal static class DesktopHost
{
    /// <summary>
    /// 返回承载壁纸的宿主窗口，并把目标窗口挂上去。
    /// 兼容两种系统行为：
    ///   1. Win11 24H2 之后：WorkerW 是 Progman 的子窗口；
    ///   2. 更早版本：WorkerW 是顶层窗口，紧跟在 SHELLDLL_DefView 所在窗口之后。
    /// </summary>
    public static IntPtr Attach(IntPtr window)
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

        IntPtr host = workerw != IntPtr.Zero ? workerw : progman;
        if (host == IntPtr.Zero)
        {
            throw new InvalidOperationException("找不到桌面宿主窗口，无法挂载壁纸。");
        }

        NativeMethods.SetParent(window, host);

        // 压到宿主的最底层，确保不会盖住桌面图标。
        NativeMethods.SetWindowPos(window, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        return host;
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
