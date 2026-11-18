using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MINWallPaper.Widgets;

/// <summary>按配置绘制的一个桌面组件窗口。改配置时原地更新尺寸、位置和内容。</summary>
internal sealed class ConfigWidget : WidgetWindow
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int MinWidth = 120;
    private const int MinHeight = 48;

    private WidgetConfig _config;
    private float[] _levels;
    private float[] _display;
    private readonly float[] _spectrum = new float[AudioSpectrum.Bands];
    private readonly DateTime _start = DateTime.Now;
    private readonly bool _gpuPolling;

    private bool _dragMode;
    private bool _dragging;
    private string _resizeEdge = "";
    private bool _moved;
    private Point _dragStartCursor;
    private Point _dragStartLocation;
    private Rectangle _resizeStartBounds;

    public ConfigWidget(WidgetConfig config)
        : base(config.Width, config.Height, IntervalFor(config.Type))
    {
        _config = config.Clone();
        _levels = new float[Math.Max(8, _config.BarCount)];
        _display = new float[_levels.Length];

        if (_config.Type == "wave")
        {
            AudioSpectrum.Instance.Acquire();
        }

        if (_config.Type == "gpu")
        {
            _gpuPolling = true;
            SystemMonitor.SetGpuPolling(true);
        }
    }

    public string WidgetId => _config.Id;

    public bool DragMode => _dragMode;

    private static int IntervalFor(string type) => type switch
    {
        "wave" => 33,
        "image" => 500,
        "cpu" or "memory" or "gpu" or "disk" => 1000,
        _ => 500
    };

    /// <summary>把新配置应用到已经显示的窗口。</summary>
    public void Apply(WidgetConfig config)
    {
        _config = config.Clone();

        if (_levels.Length != Math.Max(8, _config.BarCount))
        {
            _levels = new float[Math.Max(8, _config.BarCount)];
            _display = new float[_levels.Length];
        }

        Size = new Size(_config.Width, _config.Height);
        SetInterval(IntervalFor(_config.Type));
        PlaceWindow();
        Tick();
    }

    /// <summary>
    /// 编辑时打开拖拽：临时取消鼠标穿透，按住组件就能在桌面上拖动，
    /// 拖四边/四角可以缩放。关闭编辑器后恢复穿透，不挡桌面图标。
    /// </summary>
    public void SetDragMode(bool enabled)
    {
        if (_dragMode == enabled)
        {
            return;
        }

        _dragMode = enabled;
        _dragging = false;
        _resizeEdge = "";
        Capture = false;
        Cursor = enabled ? Cursors.SizeAll : Cursors.Default;

        if (IsHandleCreated)
        {
            int exStyle = GetWindowLong(Handle, GwlExStyle);
            exStyle = enabled
                ? exStyle & ~WsExTransparent
                : exStyle | WsExTransparent;

            SetWindowLong(Handle, GwlExStyle, exStyle);
            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);

            // 编辑时置顶，保证在其他窗口和面板上面也抓得到；退出时取消置顶并沉回桌面层。
            SetWindowPos(Handle, enabled ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0,
                0x0001 | 0x0002 | 0x0010);

            if (!enabled)
            {
                SetWindowPos(Handle, new IntPtr(1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            }
        }

        Tick();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;

            if (_dragMode)
            {
                cp.ExStyle &= ~WsExTransparent;
            }

            return cp;
        }
    }

    protected override void PlaceWindow()
    {
        Location = WidgetConfig.ComputePlacement(_config, Size);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (_dragMode && e.Button == MouseButtons.Left)
        {
            _dragStartCursor = Cursor.Position;
            _dragStartLocation = Location;
            _resizeStartBounds = Bounds;
            _moved = false;

            string edge = HitHandle(e.Location);

            if (edge.Length > 0)
            {
                _resizeEdge = edge;
            }
            else
            {
                _dragging = true;
            }

            Capture = true;
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragMode)
        {
            Point cursor = Cursor.Position;

            if (_dragging || _resizeEdge.Length > 0)
            {
                int dx = cursor.X - _dragStartCursor.X;
                int dy = cursor.Y - _dragStartCursor.Y;

                if (Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
                {
                    _moved = true;
                }

                if (_resizeEdge.Length > 0)
                {
                    ResizeStep(dx, dy);
                }
                else
                {
                    Location = new Point(_dragStartLocation.X + dx, _dragStartLocation.Y + dy);
                }

                Tick();
                return;
            }

            Cursor = CursorFor(HitHandle(e.Location));
            return;
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_dragMode && e.Button == MouseButtons.Left && (_dragging || _resizeEdge.Length > 0))
        {
            bool wasResize = _resizeEdge.Length > 0;
            _dragging = false;
            _resizeEdge = "";
            Capture = false;
            Cursor = Cursors.SizeAll;

            if (_moved || wasResize)
            {
                _config.Anchor = "free";
                _config.PosX = Left;
                _config.PosY = Top;
                _config.Width = Width;
                _config.Height = Height;
                WidgetService.Instance.SaveGeometry(_config.Id, Left, Top, Width, Height);
            }

            return;
        }

        base.OnMouseUp(e);
    }

    private void ResizeStep(int dx, int dy)
    {
        Rectangle bounds = _resizeStartBounds;
        int left = bounds.Left;
        int top = bounds.Top;
        int right = bounds.Right;
        int bottom = bounds.Bottom;

        if (_resizeEdge.Contains('w'))
        {
            left += dx;
        }

        if (_resizeEdge.Contains('e'))
        {
            right += dx;
        }

        if (_resizeEdge.Contains('n'))
        {
            top += dy;
        }

        if (_resizeEdge.Contains('s'))
        {
            bottom += dy;
        }

        if (right - left < MinWidth)
        {
            if (_resizeEdge.Contains('w'))
            {
                left = right - MinWidth;
            }
            else
            {
                right = left + MinWidth;
            }
        }

        if (bottom - top < MinHeight)
        {
            if (_resizeEdge.Contains('n'))
            {
                top = bottom - MinHeight;
            }
            else
            {
                bottom = top + MinHeight;
            }
        }

        Bounds = Rectangle.FromLTRB(left, top, right, bottom);
    }

    private (string Edge, Point Center)[] GetHandles(Size size)
    {
        const int margin = 5;
        return new[]
        {
            ("nw", new Point(margin, margin)),
            ("n", new Point(size.Width / 2, margin)),
            ("ne", new Point(size.Width - margin, margin)),
            ("w", new Point(margin, size.Height / 2)),
            ("e", new Point(size.Width - margin, size.Height / 2)),
            ("sw", new Point(margin, size.Height - margin)),
            ("s", new Point(size.Width / 2, size.Height - margin)),
            ("se", new Point(size.Width - margin, size.Height - margin))
        };
    }

    private string HitHandle(Point point)
    {
        foreach ((string edge, Point center) in GetHandles(Size))
        {
            if (Math.Abs(point.X - center.X) <= 7 && Math.Abs(point.Y - center.Y) <= 7)
            {
                return edge;
            }
        }

        return "";
    }

    private static Cursor CursorFor(string edge) => edge switch
    {
        "nw" or "se" => Cursors.SizeNWSE,
        "ne" or "sw" => Cursors.SizeNESW,
        "n" or "s" => Cursors.SizeNS,
        "w" or "e" => Cursors.SizeWE,
        _ => Cursors.SizeAll
    };

    protected override void Paint(Graphics g, Size size)
    {
        float[]? levels = null;

        if (_config.Type == "wave")
        {
            AudioSpectrum.Instance.CopyTo(_spectrum);
            Resample();

            float sensitivity = (float)Math.Clamp(_config.Sensitivity, 0.3, 3.0);

            for (int i = 0; i < _levels.Length; i++)
            {
                _display[i] = Math.Clamp(_levels[i] * sensitivity, 0f, 1f);
            }

            levels = _display;
        }

        // 分层窗口按 alpha 命中测试：编辑时铺一层几乎看不见的底色，
        // 让整个组件区域都能被鼠标抓住，而不是只有文字笔画。
        if (_dragMode)
        {
            using var wash = new SolidBrush(Color.FromArgb(12, _config.Accent));
            g.FillRectangle(wash, 0, 0, size.Width, size.Height);
        }

        WidgetPainter.Paint(g, size, _config, levels, (DateTime.Now - _start).TotalSeconds);

        if (_dragMode)
        {
            DrawEditFrame(g, size);
        }
    }

    /// <summary>编辑框：蓝色边框 + 8 个白色圆点 + 尺寸角标。</summary>
    private void DrawEditFrame(Graphics g, Size size)
    {
        Color accent = Color.FromArgb(59, 130, 246);

        using (var pen = new Pen(accent, 1.4f))
        {
            g.DrawRectangle(pen, 0.7f, 0.7f, size.Width - 1.4f, size.Height - 1.4f);
        }

        foreach ((string _, Point center) in GetHandles(size))
        {
            using (var fill = new SolidBrush(Color.White))
            using (var edge = new Pen(accent, 1.2f))
            {
                g.FillEllipse(fill, center.X - 4.5f, center.Y - 4.5f, 9f, 9f);
                g.DrawEllipse(edge, center.X - 4.5f, center.Y - 4.5f, 9f, 9f);
            }
        }

        string label = Width + " × " + Height;
        using Font font = PaintKit.CreateFont("Microsoft YaHei UI", 10f);
        SizeF text = g.MeasureString(label, font);
        var chip = new RectangleF(7, 7, text.Width + 14, 20);

        using (GraphicsPath path = Round.Create(Rectangle.Round(chip), 5))
        using (var brush = new SolidBrush(Color.FromArgb(228, 15, 23, 42)))
        {
            g.FillPath(brush, path);
        }

        PaintKit.DrawText(g, label, font, Color.White,
            new RectangleF(chip.X, chip.Y, chip.Width, chip.Height),
            StringAlignment.Center, StringAlignment.Center);
    }

    private void Resample()
    {
        int count = _levels.Length;

        for (int i = 0; i < count; i++)
        {
            float target = SampleSpectrum((i + 0.5f) / count);
            _levels[i] = target > _levels[i] ? target : _levels[i] * 0.84f;
        }
    }

    private float SampleSpectrum(float position)
    {
        float scaled = Math.Clamp(position, 0f, 0.9999f) * (AudioSpectrum.Bands - 1);
        int index = (int)scaled;
        int next = Math.Min(AudioSpectrum.Bands - 1, index + 1);
        float t = scaled - index;
        return _spectrum[index] * (1 - t) + _spectrum[next] * t;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_config.Type == "wave")
            {
                AudioSpectrum.Instance.Release();
            }

            if (_gpuPolling)
            {
                SystemMonitor.SetGpuPolling(false);
            }
        }

        base.Dispose(disposing);
    }
}
