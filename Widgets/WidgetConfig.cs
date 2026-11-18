using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace MINWallPaper.Widgets;

/// <summary>
/// 一个桌面组件的全部可编辑参数。
/// 存成 data/widgets.json，前端编辑抽屉改的就是这份数据。
/// </summary>
internal sealed class WidgetConfig
{
    public string Id { get; set; } = "";

    /// <summary>clock 或 wave。</summary>
    public string Type { get; set; } = "clock";

    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Style { get; set; } = "glass";

    /// <summary>top-left / top / top-right / left / center / right / bottom-left / bottom / bottom-right。</summary>
    public string Anchor { get; set; } = "top-right";
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    public int Width { get; set; } = 300;
    public int Height { get; set; } = 132;
    public int Radius { get; set; } = 24;

    public string TextColor { get; set; } = "#F5F6FA";
    public string AccentColor { get; set; } = "#8B7BFF";
    public string AccentColor2 { get; set; } = "#46E2A8";
    public string BgColor { get; set; } = "#12141A";
    public double BgAlpha { get; set; } = 0.46;

    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public int FontSize { get; set; } = 34;

    public string ClockFormat { get; set; } = "HH:mm";
    public string DateFormat { get; set; } = "M月d日";
    public bool ShowWeek { get; set; } = true;

    /// <summary>文本组件的文字内容。</summary>
    public string Content { get; set; } = "";

    /// <summary>图片组件使用的本地图片路径。</summary>
    public List<string> Images { get; set; } = new();

    public int CarouselSeconds { get; set; } = 4;

    /// <summary>音浪灵敏度，1 为标准，越高越容易满格。</summary>
    public double Sensitivity { get; set; } = 1.2;

    /// <summary>自由拖拽后的绝对坐标，Anchor 为 free 时生效。</summary>
    public int PosX { get; set; }
    public int PosY { get; set; }

    public int BarCount { get; set; } = 48;

    public WidgetConfig Clone() => (WidgetConfig)MemberwiseClone();

    public static WidgetConfig Create(string type, int index)
    {
        if (type == "wave")
        {
            return new WidgetConfig
            {
                Id = NewId(),
                Type = "wave",
                Name = "音浪 " + (index + 1),
                Style = "bars",
                Anchor = "bottom",
                Width = 560,
                Height = 150,
                Radius = 22,
                BgAlpha = 0.42,
                FontSize = 20,
                BarCount = 48,
                AccentColor = "#8B7BFF",
                AccentColor2 = "#46E2A8"
            };
        }

        if (type == "text")
        {
            return new WidgetConfig
            {
                Id = NewId(),
                Type = "text",
                Name = "文本 " + (index + 1),
                Style = "plain",
                Anchor = "center",
                Width = 320,
                Height = 84,
                Radius = 18,
                BgAlpha = 0,
                FontSize = 26,
                Content = "今天也要加油",
                AccentColor = "#8B7BFF"
            };
        }

        if (type is "cpu" or "memory" or "gpu" or "disk" or "image")
        {
            return CreatePanel(type, index);
        }

        return new WidgetConfig
        {
            Id = NewId(),
            Type = "clock",
            Name = "时钟 " + (index + 1),
            Style = "glass",
            Anchor = "top-right",
            Width = 300,
            Height = 136,
            Radius = 24,
            BgAlpha = 0.46,
            FontSize = 38,
            ClockFormat = "HH:mm",
            DateFormat = "M月d日",
            ShowWeek = true
        };
    }

    private static WidgetConfig CreatePanel(string type, int index)
    {
        var config = new WidgetConfig
        {
            Id = NewId(),
            Type = type,
            Name = type switch
            {
                "cpu" => "CPU " + (index + 1),
                "memory" => "内存 " + (index + 1),
                "gpu" => "GPU " + (index + 1),
                "disk" => "磁盘 " + (index + 1),
                _ => "图片 " + (index + 1)
            },
            Anchor = "center",
            BgAlpha = 0.46,
            FontSize = 30
        };

        switch (type)
        {
            case "cpu":
                config.Style = "line";
                config.Width = 320;
                config.Height = 180;
                config.AccentColor = "#22D3EE";
                config.AccentColor2 = "#34D399";
                break;
            case "memory":
                config.Style = "ring";
                config.Width = 320;
                config.Height = 160;
                config.AccentColor = "#8B5CF6";
                config.AccentColor2 = "#EC4899";
                break;
            case "gpu":
                config.Style = "bar";
                config.Width = 320;
                config.Height = 170;
                config.AccentColor = "#34D399";
                config.AccentColor2 = "#FBBF24";
                break;
            case "disk":
                config.Style = "bars";
                config.Width = 380;
                config.Height = 190;
                config.AccentColor = "#3B82F6";
                config.AccentColor2 = "#EC4899";
                break;
            case "image":
                config.Style = "single";
                config.Width = 320;
                config.Height = 220;
                config.Radius = 18;
                config.BgAlpha = 0;
                break;
        }

        return config;
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    public void Sanitize()
    {
        if (string.IsNullOrWhiteSpace(Id))
        {
            Id = NewId();
        }

        Type = Type switch
        {
            "wave" => "wave",
            "text" => "text",
            "cpu" => "cpu",
            "memory" => "memory",
            "gpu" => "gpu",
            "disk" => "disk",
            "image" => "image",
            _ => "clock"
        };
        Width = Math.Clamp(Width, 120, 1600);
        Height = Math.Clamp(Height, 48, 900);
        Radius = Math.Clamp(Radius, 0, 80);
        FontSize = Math.Clamp(FontSize, 8, 180);
        BgAlpha = Math.Clamp(BgAlpha, 0, 1);
        BarCount = Math.Clamp(BarCount, 8, 96);
        OffsetX = Math.Clamp(OffsetX, -4000, 4000);
        OffsetY = Math.Clamp(OffsetY, -4000, 4000);
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Microsoft YaHei UI" : FontFamily;
        Content ??= "";
        Images ??= new List<string>();

        if (Images.Count > 9)
        {
            Images = Images.Take(9).ToList();
        }

        CarouselSeconds = Math.Clamp(CarouselSeconds, 2, 30);
        Sensitivity = Math.Clamp(Sensitivity, 0.3, 3.0);
        PosX = Math.Clamp(PosX, -8000, 8000);
        PosY = Math.Clamp(PosY, -8000, 8000);
        if (Name.Length > 40)
        {
            Name = Name[..40];
        }
    }

    public Color Text => ParseColor(TextColor, Color.FromArgb(245, 246, 250));
    public Color Accent => ParseColor(AccentColor, Color.FromArgb(139, 123, 255));
    public Color Accent2 => ParseColor(AccentColor2, Color.FromArgb(70, 226, 168));
    public Color Bg => ParseColor(BgColor, Color.FromArgb(18, 20, 26));
    public Color BgFill => Color.FromArgb((int)Math.Round(Math.Clamp(BgAlpha, 0, 1) * 255), Bg);

    public static Color ParseColor(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string text = value.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        if (text.Length == 7 || text.Length == 4)
        {
            try
            {
                return ColorTranslator.FromHtml(text);
            }
            catch
            {
                return fallback;
            }
        }

        if (text.Length == 9 && int.TryParse(text[1..], NumberStyles.HexNumber, null, out int argb))
        {
            return Color.FromArgb((argb >> 24) & 0xFF, (argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);
        }

        return fallback;
    }

    /// <summary>时间格式：HH/H 24 小时，hh/h 12 小时，mm 分，ss 秒，A/a 上午下午。</summary>
    public string FormatTime(DateTime now)
    {
        string pattern = string.IsNullOrWhiteSpace(ClockFormat) ? "HH:mm" : ClockFormat;
        bool twelveHour = pattern.Contains("hh") || pattern.Contains('h');
        int hour = twelveHour ? ((now.Hour + 11) % 12) + 1 : now.Hour;
        var builder = new StringBuilder();

        for (int i = 0; i < pattern.Length;)
        {
            if (pattern.AsSpan(i).StartsWith("HH"))
            {
                builder.Append(now.Hour.ToString("00"));
                i += 2;
            }
            else if (pattern.AsSpan(i).StartsWith("H"))
            {
                builder.Append(now.Hour);
                i++;
            }
            else if (pattern.AsSpan(i).StartsWith("hh"))
            {
                builder.Append(hour.ToString("00"));
                i += 2;
            }
            else if (pattern.AsSpan(i).StartsWith("h"))
            {
                builder.Append(hour);
                i++;
            }
            else if (pattern.AsSpan(i).StartsWith("mm"))
            {
                builder.Append(now.Minute.ToString("00"));
                i += 2;
            }
            else if (pattern.AsSpan(i).StartsWith("ss"))
            {
                builder.Append(now.Second.ToString("00"));
                i += 2;
            }
            else if (pattern.AsSpan(i).StartsWith("A"))
            {
                builder.Append(now.Hour < 12 ? "AM" : "PM");
                i++;
            }
            else if (pattern.AsSpan(i).StartsWith("a"))
            {
                builder.Append(now.Hour < 12 ? "am" : "pm");
                i++;
            }
            else
            {
                builder.Append(pattern[i]);
                i++;
            }
        }

        return builder.ToString();
    }

    /// <summary>日期格式：yyyy/yy、MM/M、dd/d、ddd/dddd 星期。</summary>
    public string FormatDate(DateTime now)
    {
        string pattern = DateFormat ?? "";
        if (string.IsNullOrWhiteSpace(pattern) || pattern == "none")
        {
            return "";
        }

        var builder = new StringBuilder();

        for (int i = 0; i < pattern.Length;)
        {
            ReadOnlySpan<char> rest = pattern.AsSpan(i);

            if (rest.StartsWith("yyyy"))
            {
                builder.Append(now.Year.ToString("0000"));
                i += 4;
            }
            else if (rest.StartsWith("yy"))
            {
                builder.Append((now.Year % 100).ToString("00"));
                i += 2;
            }
            else if (rest.StartsWith("dddd"))
            {
                builder.Append(Weekday(now.DayOfWeek, false));
                i += 4;
            }
            else if (rest.StartsWith("ddd"))
            {
                builder.Append(Weekday(now.DayOfWeek, true));
                i += 3;
            }
            else if (rest.StartsWith("MM"))
            {
                builder.Append(now.Month.ToString("00"));
                i += 2;
            }
            else if (rest.StartsWith("M"))
            {
                builder.Append(now.Month);
                i++;
            }
            else if (rest.StartsWith("dd"))
            {
                builder.Append(now.Day.ToString("00"));
                i += 2;
            }
            else if (rest.StartsWith("d"))
            {
                builder.Append(now.Day);
                i++;
            }
            else
            {
                builder.Append(pattern[i]);
                i++;
            }
        }

        return builder.ToString();
    }

    public static string Weekday(DayOfWeek day, bool shortName)
    {
        string[] full = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
        string[] brief = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
        return shortName ? brief[(int)day] : full[(int)day];
    }

    public static string StyleName(string type, string style)
    {
        (string Id, string Name)[] table = type switch
        {
            "wave" => WaveStyles,
            "text" => TextStyles,
            "cpu" => CpuStyles,
            "memory" => MemoryStyles,
            "gpu" => GpuStyles,
            "disk" => DiskStyles,
            "image" => ImageStyles,
            _ => ClockStyles
        };

        foreach ((string id, string name) in table)
        {
            if (id == style)
            {
                return name;
            }
        }

        return style;
    }

    public static readonly (string Id, string Name)[] ClockStyles =
    {
        ("glass", "玻璃水滴"),
        ("flip", "翻页数字"),
        ("analog", "圆环指针"),
        ("neon", "霓虹辉光"),
        ("minimal", "极简细体"),
        ("arc", "弧形进度"),
        ("capsule", "渐变胶囊"),
        ("led", "点阵 LED"),
        ("poster", "海报"),
        ("outline", "描边")
    };

    public static readonly (string Id, string Name)[] WaveStyles =
    {
        ("bars", "渐变柱形"),
        ("line", "霓虹线波"),
        ("glass", "液态玻璃"),
        ("radial", "环形辐射"),
        ("dots", "点阵粒子"),
        ("aurora", "极光流动"),
        ("scan", "赛博扫描"),
        ("ink", "水墨涟漪"),
        ("segments", "LED 分段"),
        ("mirror", "双面镜像")
    };

    public static readonly (string Id, string Name)[] TextStyles =
    {
        ("plain", "极简"),
        ("spaced", "字距"),
        ("glow", "辉光"),
        ("outline", "描边")
    };

    public static readonly (string Id, string Name)[] CpuStyles =
    {
        ("line", "数字折线"),
        ("ring", "环形仪表"),
        ("cores", "核心方块")
    };

    public static readonly (string Id, string Name)[] MemoryStyles =
    {
        ("ring", "圆环进度"),
        ("stack", "分类堆叠")
    };

    public static readonly (string Id, string Name)[] GpuStyles =
    {
        ("bar", "温度占用"),
        ("ring", "半圆仪表"),
        ("info", "信息卡")
    };

    public static readonly (string Id, string Name)[] DiskStyles =
    {
        ("bars", "分区条"),
        ("donut", "甜甜圈")
    };

    public static readonly (string Id, string Name)[] ImageStyles =
    {
        ("single", "单图相框"),
        ("grid", "三图拼贴"),
        ("carousel", "轮播")
    };

    public static Point ComputePlacement(WidgetConfig config, Size size)
    {
        if (config.Anchor == "free")
        {
            return new Point(config.PosX, config.PosY);
        }

        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        const int margin = 36;
        string anchor = string.IsNullOrWhiteSpace(config.Anchor) ? "top-right" : config.Anchor;

        int x = anchor.Contains("left")
            ? area.Left + margin
            : anchor.Contains("right")
                ? area.Right - size.Width - margin
                : area.Left + (area.Width - size.Width) / 2;

        int y = anchor.Contains("top")
            ? area.Top + margin
            : anchor.Contains("bottom")
                ? area.Bottom - size.Height - margin
                : area.Top + (area.Height - size.Height) / 2;

        return new Point(x + config.OffsetX, y + config.OffsetY);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static WidgetConfig FromJson(string json)
    {
        try
        {
            WidgetConfig config = JsonSerializer.Deserialize<WidgetConfig>(json, JsonOptions) ?? new WidgetConfig();
            config.Sanitize();
            return config;
        }
        catch (Exception ex)
        {
            Log.Write("解析组件配置失败: " + ex.Message);
            return new WidgetConfig { Id = NewId() };
        }
    }
}

internal sealed class WidgetStoreData
{
    public int Version { get; set; } = 1;
    public bool Rain { get; set; }
    public List<WidgetConfig> Items { get; set; } = new();
}

internal static class WidgetStore
{
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static string FilePath => Path.Combine(AppPaths.DataDir, "widgets.json");

    public static WidgetStoreData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                WidgetStoreData? data = JsonSerializer.Deserialize<WidgetStoreData>(
                    File.ReadAllText(FilePath), JsonOptions);

                if (data != null)
                {
                    data.Items ??= new List<WidgetConfig>();
                    foreach (WidgetConfig item in data.Items)
                    {
                        item.Sanitize();
                    }

                    return data;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("读取组件配置失败: " + ex.Message);
        }

        // 第一次运行：沿用原来的默认——一个时钟 + 一个音浪。
        var seeds = new WidgetStoreData();
        seeds.Items.Add(WidgetConfig.Create("clock", 0));
        WidgetConfig wave = WidgetConfig.Create("wave", 0);
        wave.Name = "音浪";
        seeds.Items.Add(wave);
        return seeds;
    }

    public static void Save(WidgetStoreData data)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureCreated();
                File.WriteAllText(FilePath, JsonSerializer.Serialize(data, JsonOptions));
            }
        }
        catch (Exception ex)
        {
            Log.Write("保存组件配置失败: " + ex.Message);
        }
    }
}
