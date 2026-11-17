using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MINWallPaper.Widgets;

/// <summary>落雨组件：全屏透明雨丝，鼠标穿透，像窗外在下雨。</summary>
internal sealed class RainWidget : WidgetWindow
{
    private sealed class Drop
    {
        public float X;
        public float Y;
        public float Speed;
        public float Length;
        public float Wind;
        public int Layer;
    }

    private const int DropCount = 170;

    private readonly List<Drop> _drops = new(DropCount);
    private readonly Random _random = new();
    private readonly Pen[] _pens;

    public RainWidget()
        : base(
            Screen.PrimaryScreen?.Bounds.Width ?? 1920,
            Screen.PrimaryScreen?.Bounds.Height ?? 1080,
            40)
    {
        // 三档雨丝的粗细和亮度，近处更亮更粗。
        _pens = new[]
        {
            new Pen(Color.FromArgb(38, 198, 224, 242), 0.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round },
            new Pen(Color.FromArgb(72, 198, 224, 242), 1.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round },
            new Pen(Color.FromArgb(120, 208, 232, 248), 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round }
        };

        for (int i = 0; i < DropCount; i++)
        {
            var drop = new Drop();
            Reset(drop, true);
            _drops.Add(drop);
        }
    }

    protected override void PlaceWindow()
    {
        Rectangle screen = Screen.PrimaryScreen?.Bounds
            ?? new Rectangle(0, 0, Width, Height);
        Bounds = screen;
    }

    protected override void Paint(Graphics g, Size size)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        foreach (Drop drop in _drops)
        {
            Pen pen = _pens[drop.Layer];
            g.DrawLine(pen, drop.X, drop.Y, drop.X - drop.Wind * 3f, drop.Y + drop.Length);

            drop.Y += drop.Speed;
            drop.X -= drop.Wind;

            if (drop.Y - drop.Length > size.Height || drop.X < -60)
            {
                Reset(drop, false);
            }
        }
    }

    private void Reset(Drop drop, bool initial)
    {
        drop.Layer = _random.Next(_pens.Length);

        // 层数越大越近：更快、更长、更亮。
        float factor = 0.55f + drop.Layer * 0.35f;
        drop.Speed = (10f + (float)_random.NextDouble() * 12f) * factor;
        drop.Length = (12f + (float)_random.NextDouble() * 14f) * factor;
        drop.Wind = 0.8f + (float)_random.NextDouble() * 1.8f;
        drop.X = _random.Next(-40, Width + 40);
        drop.Y = initial
            ? _random.Next(-Height, Height)
            : -drop.Length - _random.Next(10, 120);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (Pen pen in _pens)
            {
                pen.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
