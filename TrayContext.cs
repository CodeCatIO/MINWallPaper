using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MINWallPaper.Music;
using MINWallPaper.Storage;
using MINWallPaper.Widgets;

namespace MINWallPaper;

/// <summary>托盘常驻上下文：管理托盘图标和控制面板的打开/退出。</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private WebLibraryForm? _form;

    public TrayContext(string[] args)
    {
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
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;

        Log.Write($"==== 启动 args={string.Join(" ", args)}");


        // 后台扫描壁纸库，不阻塞启动。
        WallpaperLibrary.Instance.Load();
        MusicLibrary.Instance.Load();
        MusicAssignments.Load();

        // 桌面组件默认开启时钟和音浪。
        WidgetService.Instance.SetEnabled("clock", true);
        WidgetService.Instance.SetEnabled("audiowave", true);

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
            _form.WindowState = FormWindowState.Normal;
            _form.Activate();
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
        _form?.Dispose();
        Log.Write("==== 退出");
        ExitThread();
    }
}
