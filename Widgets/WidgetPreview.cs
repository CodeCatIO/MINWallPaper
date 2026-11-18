using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;

namespace MINWallPaper.Widgets;

/// <summary>把组件配置渲染成 PNG，供工具库卡片和编辑抽屉预览。</summary>
internal static class WidgetPreview
{
    private sealed class CacheEntry
    {
        public string Signature = "";
        public string Path = "";
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new();

    public static string? Ensure(WidgetConfig config)
    {
        try
        {
            string signature = "v2:" + JsonSerializer.Serialize(config, JsonOptions);

            lock (Gate)
            {
                if (Cache.TryGetValue(config.Id, out CacheEntry? cached) &&
                    cached.Signature == signature && File.Exists(cached.Path))
                {
                    return cached.Path;
                }
            }

            string dir = Path.Combine(AppPaths.DataDir, "previews");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, config.Id + ".png");

            float scale = Math.Min(1f, Math.Min(760f / config.Width, 420f / config.Height));
            int width = Math.Max(1, (int)Math.Round(config.Width * scale));
            int height = Math.Max(1, (int)Math.Round(config.Height * scale));

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.ScaleTransform(scale, scale);
                float[] levels = WidgetPainter.DemoLevels(config.BarCount);
                float sensitivity = (float)Math.Clamp(config.Sensitivity, 0.3, 3.0);

                for (int i = 0; i < levels.Length; i++)
                {
                    levels[i] = Math.Clamp(levels[i] * sensitivity, 0f, 1f);
                }

                WidgetPainter.Paint(g, new Size(config.Width, config.Height), config, levels, 0.8);
            }

            bitmap.Save(file, ImageFormat.Png);

            lock (Gate)
            {
                Cache[config.Id] = new CacheEntry { Signature = signature, Path = file };
            }

            return file;
        }
        catch (Exception ex)
        {
            Log.Write("生成组件预览失败: " + ex.Message);
            return null;
        }
    }
}
