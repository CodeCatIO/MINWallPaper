using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MINWallPaper.Widgets;

/// <summary>
/// 桌面小组件的基类。
/// 壁纸层只合成 D3D 内容，普通窗口挂进去是看不见的，
/// 所以组件改用分层窗口直接浮在桌面上：整窗逐像素透明，
/// 只有组件实际绘制的部分才挡东西，不遮桌面图标。
/// </summary>
internal abstract class WidgetWindow : Form
{
    private readonly System.Windows.Forms.Timer _timer;

    protected WidgetWindow(int width, int height, int intervalMs)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(width, height);
        BackColor = Color.Black;

        _timer = new System.Windows.Forms.Timer { Interval = intervalMs };
        _timer.Tick += (_, _) => Tick();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= 0x00080000; // WS_EX_LAYERED：分层窗口
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW：不进 Alt+Tab
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE：不抢焦点
            cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT：鼠标穿透
            return cp;
        }
    }

    /// <summary>窗口出现在桌面上的位置，默认右上角。</summary>
    protected void PlaceTopRight(int margin = 40)
    {
        Rectangle screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.Right - Width - margin, screen.Top + margin);
    }

    /// <summary>窗口放在屏幕底部居中。</summary>
    protected void PlaceBottomCenter(int margin = 60)
    {
        Rectangle screen = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point((screen.Width - Width) / 2, screen.Bottom - Height - margin);
    }

    /// <summary>子类可以在启动时指定摆放方式。</summary>
    protected virtual void PlaceWindow()
    {
        PlaceTopRight();
    }

    public void Start()
    {
        Show();
        PlaceWindow();

        // 沉到窗口栈底部：桌面可见时显示，被其他应用窗口遮挡。
        SetWindowPos(Handle, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0,
            0x0002 /* SWP_NOMOVE */ | 0x0001 /* SWP_NOSIZE */ | 0x0010 /* SWP_NOACTIVATE */);

        Tick();
        _timer.Start();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint flags);

    public void Stop()
    {
        _timer.Stop();
    }

    /// <summary>定时刷新入口。</summary>
    protected void Tick()
    {
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Paint(g, bitmap.Size);
        }

        Push(bitmap);
    }

    /// <summary>把自己画到透明画布上。</summary>
    protected abstract void Paint(Graphics g, Size size);

    /// <summary>用 UpdateLayeredWindow 把带透明通道的位图推给窗口。</summary>
    private void Push(Bitmap bitmap)
    {
        if (!IsHandleCreated)
        {
            return;
        }

        IntPtr screenDc = NativeLayer.GetDC(IntPtr.Zero);
        IntPtr memDc = NativeLayer.CreateCompatibleDC(screenDc);
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = NativeLayer.SelectObject(memDc, hBitmap);

            var size = new NativeLayer.SIZE(bitmap.Width, bitmap.Height);
            var source = new NativeLayer.POINT(0, 0);
            var destination = new NativeLayer.POINT(Left, Top);
            var blend = new NativeLayer.BLENDFUNCTION
            {
                BlendOp = 0,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = 1 // AC_SRC_ALPHA
            };

            NativeLayer.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size,
                memDc, ref source, 0, ref blend, 2);
        }
        catch (Exception ex)
        {
            Log.Write("组件绘制失败: " + ex.Message);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
            {
                NativeLayer.SelectObject(memDc, oldBitmap);
            }

            if (hBitmap != IntPtr.Zero)
            {
                NativeLayer.DeleteObject(hBitmap);
            }

            NativeLayer.DeleteDC(memDc);
            NativeLayer.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>分层窗口用到的 GDI 接口。</summary>
internal static class NativeLayer
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Width;
        public int Height;

        public SIZE(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
        int crKey, ref BLENDFUNCTION pblend, int dwFlags);
}
