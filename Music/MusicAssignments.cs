using System.IO;
using System.Text.Json;

namespace MINWallPaper.Music;

internal sealed class MusicAssignment
{
    /// <summary>playlist 或 track。</summary>
    public string Kind { get; set; } = "";

    /// <summary>播放列表 id，或单曲路径。</summary>
    public string Value { get; set; } = "";
}

/// <summary>记住每张壁纸配的音乐，切换壁纸时自动播放。</summary>
internal static class MusicAssignments
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static Dictionary<string, MusicAssignment> _map =
        new(StringComparer.OrdinalIgnoreCase);

    private static string FilePath => Path.Combine(AppPaths.DataDir, "music_assignments.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            Dictionary<string, MusicAssignment>? data =
                JsonSerializer.Deserialize<Dictionary<string, MusicAssignment>>(
                    File.ReadAllText(FilePath), Options);

            if (data != null)
            {
                lock (Gate)
                {
                    _map = new Dictionary<string, MusicAssignment>(data, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("读取壁纸音乐配置失败: " + ex.Message);
        }
    }

    public static MusicAssignment? Get(string? wallpaperPath)
    {
        if (string.IsNullOrWhiteSpace(wallpaperPath))
        {
            return null;
        }

        lock (Gate)
        {
            return _map.TryGetValue(wallpaperPath, out MusicAssignment? assignment) ? assignment : null;
        }
    }

    public static void Set(string wallpaperPath, string kind, string value)
    {
        if (string.IsNullOrWhiteSpace(wallpaperPath))
        {
            return;
        }

        lock (Gate)
        {
            _map[wallpaperPath] = new MusicAssignment { Kind = kind, Value = value };

            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_map, Options));
            }
            catch (Exception ex)
            {
                Log.Write("保存壁纸音乐配置失败: " + ex.Message);
            }
        }
    }

    /// <summary>按壁纸配置开始播放音乐。</summary>
    public static void ApplyTo(string wallpaperPath)
    {
        MusicAssignment? assignment = Get(wallpaperPath);
        if (assignment == null)
        {
            return;
        }

        if (assignment.Kind == "playlist")
        {
            MusicPlaylist? playlist = MusicLibrary.Instance.Playlists
                .FirstOrDefault(p => p.Id == assignment.Value);

            if (playlist is { Tracks.Count: > 0 })
            {
                MusicPlayer.Instance.PlayQueue(
                    playlist.Tracks.Select(t => t.Path).ToList(), 0,
                    MusicLoopMode.List, playlist.Name);
            }
        }
        else if (assignment.Kind == "track" && File.Exists(assignment.Value))
        {
            MusicPlayer.Instance.PlaySingle(assignment.Value);
        }
    }
}
