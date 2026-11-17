using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MINWallPaper.Storage;

internal sealed class AppSettings
{
    public string WallpaperRoot { get; set; } = "";
}

/// <summary>
/// 壁纸库：扫描本地文件夹、维护索引、生成缩略图、记录收藏。
/// 索引存在 data/library.json，缩略图存在 data/thumbnails。
/// </summary>
internal sealed class WallpaperLibrary
{
    private static readonly HashSet<string> VideoExts =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".wmv", ".avi", ".mov", ".mkv" };

    private static readonly HashSet<string> ImageExts =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static WallpaperLibrary Instance { get; } = new();

    private List<WallpaperItem> _items = new();

    public string Root { get; private set; } = AppPaths.DefaultWallpaperDir;

    public IReadOnlyList<WallpaperItem> Items => _items;

    public bool IsScanning { get; private set; }

    public event Action? Changed;

    public event Action<string>? Status;

    public int VideoCount => _items.Count(i => i.Kind == WallpaperKind.Video);

    public int ImageCount => _items.Count(i => i.Kind == WallpaperKind.Image);

    /// <summary>读设置和索引，然后在后台扫描。</summary>
    public void Load()
    {
        AppPaths.EnsureCreated();
        LoadSettings();
        LoadIndex();
        _ = Task.Run(Scan);
    }

    public void Rescan() => _ = Task.Run(Scan);

    public void SetRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        Root = path;
        SaveSettings();
        Rescan();
    }

    public void ToggleFavorite(WallpaperItem item)
    {
        item.Favorite = !item.Favorite;
        SaveIndex();
        Changed?.Invoke();
    }

    private void Scan()
    {
        IsScanning = true;
        Status?.Invoke("正在扫描壁纸库…");

        try
        {
            Directory.CreateDirectory(Root);

            var old = _items.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
            var result = new List<WallpaperItem>();

            foreach (string file in SafeEnumerate(Root))
            {
                WallpaperKind? kind = DetectKind(file);
                if (kind == null)
                {
                    continue;
                }

                FileInfo info;
                try
                {
                    info = new FileInfo(file);
                    if (!info.Exists)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                old.TryGetValue(file, out WallpaperItem? existing);

                var item = new WallpaperItem
                {
                    Path = file,
                    Name = Path.GetFileNameWithoutExtension(file),
                    Kind = kind.Value,
                    SizeBytes = info.Length,
                    ModifiedTicks = info.LastWriteTimeUtc.Ticks,
                    Favorite = existing?.Favorite ?? false
                };

                item.ThumbnailPath = EnsureThumbnail(item);
                result.Add(item);
            }

            _items = result;
            SaveIndex();
            Log.Write($"壁纸库扫描完成：{VideoCount} 个视频，{ImageCount} 张图片，目录 {Root}");
            Status?.Invoke($"壁纸库：{VideoCount} 个视频 / {ImageCount} 张图片");
        }
        catch (Exception ex)
        {
            Log.Write("壁纸库扫描失败: " + ex);
            Status?.Invoke("扫描失败：" + ex.Message);
        }
        finally
        {
            IsScanning = false;
            Changed?.Invoke();
        }
    }

    private static string? EnsureThumbnail(WallpaperItem item)
    {
        string target = System.IO.Path.Combine(AppPaths.ThumbnailDir, ThumbKey(item) + ".jpg");
        if (File.Exists(target))
        {
            return target;
        }

        return ShellThumbnail.Save(item.Path, target, 480, 270) ? target : null;
    }

    private static string ThumbKey(WallpaperItem item)
    {
        string raw = item.Path + "|" + item.ModifiedTicks;
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..16];
    }

    private static WallpaperKind? DetectKind(string file)
    {
        string ext = System.IO.Path.GetExtension(file);
        if (VideoExts.Contains(ext))
        {
            return WallpaperKind.Video;
        }

        return ImageExts.Contains(ext) ? WallpaperKind.Image : null;
    }

    private static IEnumerable<string> SafeEnumerate(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            string[] files;
            string[] dirs;

            try
            {
                files = Directory.GetFiles(dir);
                dirs = Directory.GetDirectories(dir);
            }
            catch
            {
                // 没有权限的目录直接跳过，不影响整体扫描
                continue;
            }

            foreach (string file in files)
            {
                yield return file;
            }

            foreach (string sub in dirs)
            {
                stack.Push(sub);
            }
        }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsFile))
            {
                SaveSettings();
                return;
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(AppPaths.SettingsFile), JsonOptions);

            if (!string.IsNullOrWhiteSpace(settings?.WallpaperRoot))
            {
                Root = settings.WallpaperRoot;
            }
        }
        catch (Exception ex)
        {
            Log.Write("读取设置失败: " + ex.Message);
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new AppSettings { WallpaperRoot = Root };
            File.WriteAllText(AppPaths.SettingsFile,
                JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Write("保存设置失败: " + ex.Message);
        }
    }

    private void LoadIndex()
    {
        try
        {
            if (!File.Exists(AppPaths.LibraryFile))
            {
                return;
            }

            var index = JsonSerializer.Deserialize<LibraryIndex>(
                File.ReadAllText(AppPaths.LibraryFile), JsonOptions);

            if (index?.Items is { Count: > 0 })
            {
                _items = index.Items;
            }
        }
        catch (Exception ex)
        {
            Log.Write("读取索引失败: " + ex.Message);
        }
    }

    private void SaveIndex()
    {
        try
        {
            var index = new LibraryIndex { Root = Root, Items = _items };
            File.WriteAllText(AppPaths.LibraryFile,
                JsonSerializer.Serialize(index, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Write("保存索引失败: " + ex.Message);
        }
    }
}
