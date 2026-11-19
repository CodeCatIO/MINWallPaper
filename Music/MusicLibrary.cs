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

    /// <summary>music/files 下的分类文件夹名。</summary>
    public IReadOnlyList<string> Categories =>
        Directory.Exists(FilesDir)
            ? Directory.GetDirectories(FilesDir)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : Array.Empty<string>();

    private static bool IsValidCategoryName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.Length <= 40 &&
        name is not "." and not ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal);

    /// <summary>新建一个空的分类文件夹。</summary>
    public bool CreateCategory(string name)
    {
        name = (name ?? "").Trim();

        if (!IsValidCategoryName(name))
        {
            return false;
        }

        try
        {
            string dir = Path.Combine(FilesDir, name);
            if (Directory.Exists(dir))
            {
                return false;
            }

            Directory.CreateDirectory(dir);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("新建分类失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>重命名分类文件夹，并同步播放列表里的歌曲路径。</summary>
    public bool RenameCategory(string oldName, string newName)
    {
        oldName = (oldName ?? "").Trim();
        newName = (newName ?? "").Trim();

        if (!IsValidCategoryName(oldName) || !IsValidCategoryName(newName) ||
            string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string oldDir = Path.Combine(FilesDir, oldName);
        string newDir = Path.Combine(FilesDir, newName);

        if (!Directory.Exists(oldDir) || Directory.Exists(newDir))
        {
            return false;
        }

        try
        {
            Directory.Move(oldDir, newDir);

            string oldPrefix = oldDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string newPrefix = newDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            foreach (MusicPlaylist playlist in Playlists)
            {
                foreach (MusicTrack track in playlist.Tracks)
                {
                    if (track.Path.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        track.Path = newPrefix + track.Path.Substring(oldPrefix.Length);
                    }
                }
            }

            Save();
            Rescan();
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("重命名分类失败: " + ex.Message);
            return false;
        }
    }

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

    /// <summary>把歌曲（连同同名歌词）移动到指定分类文件夹，返回新路径。</summary>
    public string? MoveToCategory(string sourcePath, string category)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        string root = Path.GetFullPath(FilesDir);
        string source = Path.GetFullPath(sourcePath);

        if (!source.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(category), category, StringComparison.Ordinal))
        {
            return null;
        }

        string targetDir = Path.Combine(root, category);

        if (string.Equals(Path.GetDirectoryName(source), targetDir, StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        try
        {
            Directory.CreateDirectory(targetDir);

            string target = Path.Combine(targetDir, Path.GetFileName(source));
            int index = 2;

            while (File.Exists(target))
            {
                target = Path.Combine(targetDir,
                    $"{Path.GetFileNameWithoutExtension(source)} ({index++}){Path.GetExtension(source)}");
            }

            File.Move(source, target);

            string lrcSource = Path.ChangeExtension(source, ".lrc");
            if (File.Exists(lrcSource))
            {
                File.Move(lrcSource, Path.ChangeExtension(target, ".lrc"), true);
            }

            foreach (MusicPlaylist playlist in Playlists)
            {
                foreach (MusicTrack track in playlist.Tracks)
                {
                    if (string.Equals(track.Path, source, StringComparison.OrdinalIgnoreCase))
                    {
                        track.Path = target;
                    }
                }
            }

            Save();
            Rescan();
            return target;
        }
        catch (Exception ex)
        {
            Log.Write("移动歌曲分类失败: " + ex.Message);
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
