using System.IO;
using System.Text.Json;

namespace MINWallPaper.Music;

internal sealed class MusicTrack
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
}

internal sealed class MusicPlaylist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "新播放列表";
    public List<MusicTrack> Tracks { get; set; } = new();
}

internal sealed class MusicLibraryData
{
    public List<MusicPlaylist> Playlists { get; set; } = new();
}

/// <summary>扫描到的本地音乐文件。</summary>
internal sealed class MusicFile
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public long SizeBytes { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// 本地音乐库：播放列表和歌曲都存在 data/music 下，
/// 歌曲文件会复制进库，挪走原文件也不会丢。
/// </summary>
internal sealed class MusicLibrary
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> AudioExts =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma" };

    public static MusicLibrary Instance { get; } = new();

    public string Root { get; } = Path.Combine(AppPaths.DataDir, "music");

    public string FilesDir { get; } = Path.Combine(AppPaths.DataDir, "music", "files");

    private string IndexFile => Path.Combine(Root, "playlists.json");

    public List<MusicPlaylist> Playlists { get; } = new();

    private List<MusicFile> _files = new();

    public IReadOnlyList<MusicFile> Files => _files;

    public bool IsScanning { get; private set; }

    public event Action? Changed;

    /// <summary>本地音乐扫描完成。</summary>
    public event Action? Scanned;

    private MusicLibrary()
    {
    }

    public void Load()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(FilesDir);
        _ = Task.Run(Scan);

        try
        {
            if (!File.Exists(IndexFile))
            {
                Playlists.Add(new MusicPlaylist { Name = "我的音乐" });
                Save();
                return;
            }

            var data = JsonSerializer.Deserialize<MusicLibraryData>(
                File.ReadAllText(IndexFile), JsonOptions);

            if (data?.Playlists != null)
            {
                Playlists.Clear();
                Playlists.AddRange(data.Playlists);
            }

            if (Playlists.Count == 0)
            {
                Playlists.Add(new MusicPlaylist { Name = "我的音乐" });
                Save();
            }
        }
        catch (Exception ex)
        {
            Log.Write("读取音乐库失败: " + ex.Message);
        }
    }

    /// <summary>重新扫描音乐文件夹。</summary>
    public void Rescan() => _ = Task.Run(Scan);

    private void Scan()
    {
        IsScanning = true;
        Scanned?.Invoke();

        var found = new List<MusicFile>();

        try
        {
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                if (!AudioExts.Contains(Path.GetExtension(file)))
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

                found.Add(new MusicFile
                {
                    Path = file,
                    Title = Path.GetFileNameWithoutExtension(file),
                    SizeBytes = info.Length,
                    Duration = ReadDuration(file)
                });
            }
        }
        catch (Exception ex)
        {
            Log.Write("扫描音乐失败: " + ex.Message);
        }

        _files = found
            .OrderBy(f => f.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        IsScanning = false;
        Log.Write($"音乐扫描完成：{_files.Count} 首");
        Scanned?.Invoke();
    }

    private static TimeSpan ReadDuration(string path)
    {
        try
        {
            using var reader = new NAudio.Wave.MediaFoundationReader(path);
            return reader.TotalTime;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    public MusicPlaylist CreatePlaylist(string name)
    {
        var playlist = new MusicPlaylist { Name = name };
        Playlists.Add(playlist);
        Save();
        Changed?.Invoke();
        return playlist;
    }

    public void RemovePlaylist(MusicPlaylist playlist)
    {
        Playlists.Remove(playlist);
        Save();
        Changed?.Invoke();
    }

    public void RenamePlaylist(MusicPlaylist playlist, string name)
    {
        playlist.Name = name;
        Save();
        Changed?.Invoke();
    }

    public MusicTrack? AddTrack(MusicPlaylist playlist, string sourcePath)
    {
        if (!AudioExts.Contains(Path.GetExtension(sourcePath)))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(FilesDir);

            string fileName = Path.GetFileName(sourcePath);
            string target = sourcePath;

            // 已经在音乐库里（扫描出来的文件）直接引用，不重复复制。
            if (!Path.GetFullPath(sourcePath).StartsWith(
                    Path.GetFullPath(Root) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                target = Path.Combine(FilesDir, fileName);
                int index = 1;

                while (File.Exists(target) &&
                       !string.Equals(Path.GetFullPath(target), Path.GetFullPath(sourcePath),
                           StringComparison.OrdinalIgnoreCase))
                {
                    target = Path.Combine(FilesDir,
                        $"{Path.GetFileNameWithoutExtension(fileName)} ({index++}){Path.GetExtension(fileName)}");
                }

                if (!File.Exists(target))
                {
                    File.Copy(sourcePath, target);
                }
            }

            var track = new MusicTrack
            {
                Path = target,
                Title = Path.GetFileNameWithoutExtension(fileName)
            };

            playlist.Tracks.Add(track);
            Save();
            Changed?.Invoke();
            return track;
        }
        catch (Exception ex)
        {
            Log.Write("添加歌曲失败: " + ex.Message);
            return null;
        }
    }

    public void RemoveTrack(MusicPlaylist playlist, MusicTrack track)
    {
        playlist.Tracks.Remove(track);
        Save();
        Changed?.Invoke();
    }

    public void MoveTrack(MusicPlaylist playlist, int from, int to)
    {
        if (from < 0 || from >= playlist.Tracks.Count ||
            to < 0 || to >= playlist.Tracks.Count || from == to)
        {
            return;
        }

        MusicTrack track = playlist.Tracks[from];
        playlist.Tracks.RemoveAt(from);
        playlist.Tracks.Insert(to, track);
        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        try
        {
            var data = new MusicLibraryData { Playlists = Playlists };
            File.WriteAllText(IndexFile, JsonSerializer.Serialize(data, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Write("保存音乐库失败: " + ex.Message);
        }
    }
}
