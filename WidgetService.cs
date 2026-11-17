using MINWallPaper.Widgets;

namespace MINWallPaper;

internal sealed class WidgetDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
}

/// <summary>管理桌面组件的注册与开关。</summary>
internal sealed class WidgetService
{
    public static WidgetService Instance { get; } = new();

    private readonly Dictionary<string, WidgetDefinition> _definitions = new();
    private readonly Dictionary<string, Func<WidgetWindow>> _factories = new();
    private readonly Dictionary<string, WidgetWindow> _active = new();

    private WidgetService()
    {
        Register("clock", "时钟", "在桌面显示时间、日期和星期", () => new ClockWidget());
        Register("audiowave", "音浪", "跟随系统声音跳动的频谱", () => new AudioWaveWidget());
        Register("rain", "落雨", "全屏细雨，安静地落在桌面上", () => new RainWidget());
    }

    public IReadOnlyList<WidgetDefinition> All => _definitions.Values.ToList();

    private void Register(string id, string name, string description, Func<WidgetWindow> factory)
    {
        _definitions[id] = new WidgetDefinition { Id = id, Name = name, Description = description };
        _factories[id] = factory;
    }

    public bool IsEnabled(string id) => _active.ContainsKey(id);

    public void SetEnabled(string id, bool enabled)
    {
        if (enabled)
        {
            if (_active.ContainsKey(id) || !_factories.TryGetValue(id, out Func<WidgetWindow>? factory))
            {
                return;
            }

            WidgetWindow widget = factory();
            widget.Start();
            _active[id] = widget;
            Log.Write("组件已开启: " + id);
        }
        else
        {
            if (!_active.Remove(id, out WidgetWindow? widget))
            {
                return;
            }

            widget.Stop();
            widget.Dispose();
            Log.Write("组件已关闭: " + id);
        }
    }

    public void Shutdown()
    {
        foreach (WidgetWindow widget in _active.Values)
        {
            widget.Stop();
            widget.Dispose();
        }

        _active.Clear();
    }
}
