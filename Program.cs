namespace MINWallPaper;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 只允许一个实例。重复启动时把已经运行的面板叫到前台，不再开新进程。
        if (!SingleInstance.TryAcquire(out Mutex mutex))
        {
            SingleInstance.ActivateExisting();
            return;
        }

        using (mutex)
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext(args));
        }
    }
}
