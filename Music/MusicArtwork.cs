using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MINWallPaper.Storage;

namespace MINWallPaper.Music;

/// <summary>从音频文件里取专辑封面（交给系统缩略图服务），缓存成 jpg 给界面显示。</summary>
internal static class MusicArtwork
{
    private static readonly ConcurrentDictionary<string, string?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, bool> Pending =
        new(StringComparer.OrdinalIgnoreCase);

    private static string Dir => Path.Combine(AppPaths.DataDir, "artwork");

    /// <summary>封面生成好之后通知界面刷新。</summary>
    public static event Action? Ready;

    /// <summary>拿封面路径；第一次会后台生成，生成好之前返回 null。</summary>
    public static string? Get(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        if (Cache.TryGetValue(path, out string? cached))
        {
            return cached;
        }

        string target = TargetPath(path);
        if (File.Exists(target))
        {
            Cache[path] = target;
            return target;
        }

        if (Pending.TryAdd(path, true))
        {
            _ = Task.Run(() => Generate(path, target));
        }

        return null;
    }

    private static void Generate(string path, string target)
    {
        try
        {
            Directory.CreateDirectory(Dir);

            if (ShellThumbnail.Save(path, target, 320, 320, allowIconFallback: false))
            {
                Cache[path] = target;
                Ready?.Invoke();
            }
            else
            {
                // 没有内嵌封面，界面继续用唱片底图。
                Cache[path] = null;
            }
        }
        catch (Exception ex)
        {
            Log.Write("读取专辑封面失败: " + ex.Message);
            Cache[path] = null;
        }
        finally
        {
            Pending.TryRemove(path, out _);
        }
    }

    private static string TargetPath(string path)
    {
        var info = new FileInfo(path);
        string key = path + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(Dir, Convert.ToHexString(hash)[..12] + ".jpg");
    }
}
