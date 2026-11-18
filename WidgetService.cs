using System.IO;

namespace MINWallPaper.Widgets;

/// <summary>管理桌面组件的实例：增删、启停、配置更新和落盘。</summary>
internal sealed class WidgetService
{
    public static WidgetService Instance { get; } = new();

    private readonly List<WidgetConfig> _configs = new();
    private readonly Dictionary<string, ConfigWidget> _windows = new(StringComparer.OrdinalIgnoreCase);
    private RainWidget? _rain;

    private WidgetService()
    {
    }

    public IReadOnlyList<WidgetConfig> Configs => _configs;

    public bool RainEnabled { get; private set; }

    /// <summary>拖动改了位置后通知界面刷新（位置描述会变）。</summary>
    public event Action? Changed;

    public void Load()
    {
        WidgetStoreData data = WidgetStore.Load();

        _configs.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (WidgetConfig config in data.Items)
        {
            // 启动器已经下线，旧配置直接跳过。
            if (string.Equals(config.Type, "launcher", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            config.Sanitize();

            if (!seen.Add(config.Id))
            {
                config.Id = WidgetConfig.NewId();
                seen.Add(config.Id);
            }

            _configs.Add(config);
        }

        RainEnabled = data.Rain;

        foreach (WidgetConfig config in _configs.Where(c => c.Enabled))
        {
            EnsureWindow(config);
        }

        if (RainEnabled)
        {
            CreateRain();
        }

        Save();
    }

    public WidgetConfig Add(string type)
    {
        type = type is "wave" or "text" or "cpu" or "memory" or "gpu" or "disk" or "image" or "date"
            ? type
            : "clock";
        int sameType = _configs.Count(c => c.Type == type);
        WidgetConfig config = WidgetConfig.Create(type, sameType);

        // 新建的稍微错开一点，避免和已有组件完全重叠。
        config.OffsetX += (sameType % 3 - 1) * 28;
        config.OffsetY += (sameType % 3) * 46;

        _configs.Add(config);
        EnsureWindow(config);
        Save();
        return config;
    }

    public void Update(WidgetConfig incoming)
    {
        incoming.Sanitize();
        int index = _configs.FindIndex(c => c.Id == incoming.Id);

        if (index < 0)
        {
            return;
        }

        // 类型不允许在编辑里改，保持原类型。
        incoming.Type = _configs[index].Type;
        _configs[index] = incoming;

        if (incoming.Enabled)
        {
            EnsureWindow(incoming);
        }
        else
        {
            DisposeWindow(incoming.Id);
        }

        Save();
    }

    public void Remove(string id)
    {
        int index = _configs.FindIndex(c => c.Id == id);

        if (index < 0)
        {
            return;
        }

        _configs.RemoveAt(index);
        DisposeWindow(id);
        Save();
    }

    public void SetEnabled(string id, bool enabled)
    {
        WidgetConfig? config = _configs.FirstOrDefault(c => c.Id == id);

        if (config == null || config.Enabled == enabled)
        {
            return;
        }

        config.Enabled = enabled;

        if (enabled)
        {
            EnsureWindow(config);
        }
        else
        {
            DisposeWindow(id);
        }

        Save();
    }

    /// <summary>编辑时只让当前组件接收鼠标，其余保持穿透。</summary>
    public void SetDragMode(string? id)
    {
        foreach ((string key, ConfigWidget window) in _windows)
        {
            window.SetDragMode(string.Equals(key, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>拖动结束后把窗口位置记成自由坐标。</summary>
    public void MoveWidget(string id, int x, int y)
    {
        WidgetConfig? config = _configs.FirstOrDefault(c => c.Id == id);

        if (config == null)
        {
            return;
        }

        config.Anchor = "free";
        config.PosX = x;
        config.PosY = y;
        Save();
        Changed?.Invoke();
    }

    /// <summary>拖拽缩放结束后保存位置和大小。</summary>
    public void SaveGeometry(string id, int x, int y, int width, int height)
    {
        WidgetConfig? config = _configs.FirstOrDefault(c => c.Id == id);

        if (config == null)
        {
            return;
        }

        config.Anchor = "free";
        config.PosX = x;
        config.PosY = y;
        config.Width = width;
        config.Height = height;
        Save();
        Changed?.Invoke();
    }

    public void SetImages(string id, IEnumerable<string> paths)
    {
        Mutate(id, config =>
        {
            config.Images = paths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(9)
                .ToList();
        });
    }

    private void Mutate(string id, Action<WidgetConfig> change)
    {
        WidgetConfig? config = _configs.FirstOrDefault(c => c.Id == id);

        if (config == null)
        {
            return;
        }

        change(config);

        if (config.Enabled)
        {
            EnsureWindow(config);
        }

        Save();
        Changed?.Invoke();
    }

    public void SetRainEnabled(bool enabled)
    {
        RainEnabled = enabled;

        if (enabled)
        {
            CreateRain();
        }
        else if (_rain != null)
        {
            _rain.Stop();
            _rain.Dispose();
            _rain = null;
        }

        Save();
    }

    public void Shutdown()
    {
        foreach (ConfigWidget window in _windows.Values)
        {
            window.Stop();
            window.Dispose();
        }

        _windows.Clear();
        _rain?.Stop();
        _rain?.Dispose();
        _rain = null;
    }

    private void CreateRain()
    {
        if (_rain != null)
        {
            return;
        }

        _rain = new RainWidget();
        _rain.Start();
    }

    private ConfigWidget EnsureWindow(WidgetConfig config)
    {
        if (_windows.TryGetValue(config.Id, out ConfigWidget? existing))
        {
            existing.Apply(config);
            return existing;
        }

        var window = new ConfigWidget(config);
        _windows[config.Id] = window;
        window.Start();
        return window;
    }

    private void DisposeWindow(string id)
    {
        if (!_windows.Remove(id, out ConfigWidget? window))
        {
            return;
        }

        window.Stop();
        window.Dispose();
    }

    private void Save()
    {
        WidgetStore.Save(new WidgetStoreData
        {
            Rain = RainEnabled,
            Items = _configs
        });
    }
}
