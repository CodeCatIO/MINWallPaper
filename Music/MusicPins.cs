using System.IO;
using System.Text.Json;

namespace MINWallPaper.Music;

/// <summary>沉浸模式里手动置顶的歌曲，存 data/music/pins.json。</summary>
internal static class MusicPins
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static HashSet<string>? _paths;

    private static string FilePath => Path.Combine(AppPaths.DataDir, "music", "pins.json");

    private static HashSet<string> Paths
    {
        get
        {
            lock (Gate)
            {
                if (_paths != null)
                {
                    return _paths;
                }

                try
                {
                    List<string> list = File.Exists(FilePath)
                        ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? new List<string>()
                        : new List<string>();
                    _paths = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    Log.Write("读取置顶歌曲失败: " + ex.Message);
                    _paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }

                return _paths;
            }
        }
    }

    public static bool Contains(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        lock (Gate)
        {
            return Paths.Contains(path);
        }
    }

    public static bool Toggle(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        lock (Gate)
        {
            bool pinned;

            if (Paths.Contains(path))
            {
                Paths.Remove(path);
                pinned = false;
            }
            else
            {
                Paths.Add(path);
                pinned = true;
            }

            Save();
            return pinned;
        }
    }

    public static void ReplacePath(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
        {
            return;
        }

        lock (Gate)
        {
            if (Paths.Remove(oldPath))
            {
                Paths.Add(newPath);
                Save();
            }
        }
    }

    /// <summary>分类文件夹改名后，批量更新置顶路径。</summary>
    public static void ReplacePrefix(string oldPrefix, string newPrefix)
    {
        if (string.IsNullOrWhiteSpace(oldPrefix) || string.IsNullOrWhiteSpace(newPrefix))
        {
            return;
        }

        lock (Gate)
        {
            string oldRoot = oldPrefix.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string newRoot = newPrefix.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            List<string> affected = Paths
                .Where(path => path.StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (affected.Count == 0)
            {
                return;
            }

            foreach (string path in affected)
            {
                Paths.Remove(path);
                Paths.Add(newRoot + path.Substring(oldRoot.Length));
            }

            Save();
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Paths.ToList(), JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Write("保存置顶歌曲失败: " + ex.Message);
        }
    }
}
