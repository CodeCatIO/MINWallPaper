using System.IO;

namespace MINWallPaper;

/// <summary>极简文件日志，只用于排查问题。</summary>
internal static class Log
{
    private static readonly string Path =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MINWallPaper.log");

    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志不允许影响主流程
        }
    }
}
