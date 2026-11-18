using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MINWallPaper.Music;
using MINWallPaper.Native;
using MINWallPaper.Storage;
using MINWallPaper.Widgets;

namespace MINWallPaper;

/// <summary>托盘常驻上下文：管理托盘图标和控制面板的打开/退出。</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ActivationWindow _activation;
    private WebLibraryForm? _form;

    public TrayContext(string[] args)
    {
        // 第二个实例通过这个消息窗口唤起面板。
        _activation = new ActivationWindow(ShowPanel);

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "MINWallPaper"
        };
        _tray.DoubleClick += (_, _) => ShowPanel();

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开壁纸库", null, (_, _) => ShowPanel());
        menu.Items.Add("停止壁纸", null, (_, _) => WallpaperHost.Instance.Stop());
        menu.Items.Add("重新加载壁纸", null, (_, _) => WallpaperHost.Instance.Reload());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;

        Log.Write($"==== 启动 args={string.Join(" ", args)}");


        // 后台扫描壁纸库，不阻塞启动。
        WallpaperLibrary.Instance.Load();
        MusicLibrary.Instance.Load();
        MusicAssignments.Load();

        // 桌面组件按上次保存的配置恢复；第一次运行默认给一个时钟和一个音浪。
        WidgetService.Instance.Load();

        // 带视频参数时静默启动，适合开机自启。
        if (args.Length > 0 && File.Exists(args[0]))
        {
            try
            {
                WallpaperHost.Instance.Play(args[0]);
            }
            catch (Exception ex)
            {
                Log.Write("启动播放失败: " + ex.Message);
                ShowPanel();
            }
        }
        else
        {
            ShowPanel();
        }
    }

    private void ShowPanel()
    {
        try
        {
            if (_form == null || _form.IsDisposed)
            {
                _form = new WebLibraryForm();
            }

            _form.Show();

            if (_form.WindowState == FormWindowState.Minimized)
            {
                _form.WindowState = FormWindowState.Normal;
            }

            _form.Activate();
            NativeMethods.SetForegroundWindow(_form.Handle);
        }
        catch (Exception ex)
        {
            Log.Write("打开面板失败: " + ex);
        }
    }

    private void ExitApp()
    {
        WallpaperHost.Instance.Stop();
        MusicPlayer.Instance.Dispose();
        WidgetService.Instance.Shutdown();

        _tray.Visible = false;
        _tray.Dispose();
        _activation.Dispose();
        _form?.Dispose();
        Log.Write("==== 退出");
        ExitThread();
    }
}
