using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Vortice.MediaFoundation;

namespace MINWallPaper.Playback;

/// <summary>
/// 视频壁纸的悬停预览：用 Media Foundation 抽出前两秒的低分辨率帧并缓存成 JPEG。
/// 这样浏览器只需要轮换图片，不需要解码 HEVC 之类的视频，既省资源又不会和桌面壁纸抢显卡。
/// </summary>
internal static class PreviewFrames
{
    private const int FrameWidth = 320;
    private const double Seconds = 2.0;
    private const int Fps = 12;

    private static readonly object Gate = new();
    private static readonly Dictionary<string, string[]?> Cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Loading =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim DecodeLock = new(1, 1);

    private static bool _mfStarted;

    /// <summary>预览帧生成完成（可能在后台线程触发）。</summary>
    public static event Action<string>? Ready;

    /// <summary>取已缓存的帧文件；没有就开始后台生成并返回 null。</summary>
    public static string[]? GetFrames(string videoPath)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(videoPath, out string[]? cached))
            {
                return cached;
            }

            if (!Loading.Add(videoPath))
            {
                return null;
            }
        }

        _ = Task.Run(() => Generate(videoPath));
        return null;
    }

    private static void Generate(string path)
    {
        string[]? frames = null;

        try
        {
            string dir = Path.Combine(AppPaths.DataDir, "previews", Hash(path));
            Directory.CreateDirectory(dir);

            string[] existing = Directory.GetFiles(dir, "f*.jpg")
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (existing.Length >= 8)
            {
                frames = existing;
            }
            else
            {
                DecodeLock.Wait();

                try
                {
                    List<Bitmap>? decoded = Decode(path, FrameWidth, Seconds, Fps);
                    if (decoded is { Count: > 0 })
                    {
                        var list = new List<string>(decoded.Count);

                        for (int i = 0; i < decoded.Count; i++)
                        {
                            string file = Path.Combine(dir, $"f{i:D3}.jpg");
                            decoded[i].Save(file, ImageFormat.Jpeg);
                            list.Add(file);
                        }

                        frames = list.ToArray();
                    }

                    if (decoded != null)
                    {
                        foreach (Bitmap bitmap in decoded)
                        {
                            bitmap.Dispose();
                        }
                    }
                }
                finally
                {
                    DecodeLock.Release();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("生成视频预览失败 " + path + " : " + ex.Message);
        }

        lock (Gate)
        {
            Loading.Remove(path);
            Cache[path] = frames;
        }

        Ready?.Invoke(path);
    }

    private static string Hash(string path)
    {
        byte[] key = SHA1.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(key)[..12];
    }

    private static List<Bitmap>? Decode(string path, int maxWidth, double seconds, int fps)
    {
        EnsureMediaFoundation();

        var frames = new List<Bitmap>();
        IMFAttributes? attributes = null;
        IMFSourceReader? reader = null;

        try
        {
            attributes = MediaFactory.MFCreateAttributes(1);
            attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u);

            reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);
            reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);

            using (IMFMediaType output = MediaFactory.MFCreateMediaType())
            {
                output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, output);
            }

            int width;
            int height;

            using (IMFMediaType current = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream))
            {
                ulong packed = current.GetUInt64(MediaTypeAttributeKeys.FrameSize);
                width = (int)(packed >> 32);
                height = (int)(packed & 0xFFFFFFFF);
            }

            if (width <= 0 || height <= 0)
            {
                return frames;
            }

            double interval = 1.0 / Math.Max(1, fps);
            double next = 0;
            long deadline = (long)(seconds * 10_000_000);
            int emptyReads = 0;

            while (true)
            {
                using IMFSample? sample = reader.ReadSample(
                    SourceReaderIndex.FirstVideoStream,
                    SourceReaderControlFlag.None,
                    out _,
                    out SourceReaderFlag flags,
                    out long timestamp);

                if ((flags & SourceReaderFlag.EndOfStream) != 0)
                {
                    break;
                }

                // 解码器切换格式等事件会返回空帧，跳过继续读，不能直接结束。
                if (sample == null)
                {
                    if (++emptyReads > 60)
                    {
                        break;
                    }

                    continue;
                }

                if (timestamp > deadline)
                {
                    break;
                }

                if (timestamp >= next * 10_000_000)
                {
                    Bitmap? frame = ToBitmap(sample, width, height, maxWidth);
                    if (frame != null)
                    {
                        frames.Add(frame);
                    }

                    next += interval;
                }
            }
        }
        finally
        {
            reader?.Dispose();
            attributes?.Dispose();
        }

        if (frames.Count == 0)
        {
            Log.Write("预览抽帧为空: " + path);
        }

        return frames;
    }

    private static Bitmap? ToBitmap(IMFSample sample, int width, int height, int maxWidth)
    {
        using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();
        buffer.Lock(out IntPtr data, out _, out int currentLength);

        try
        {
            int stride = Math.Max(width * 4, height > 0 ? currentLength / height : width * 4);

            using var source = new Bitmap(width, height, stride, PixelFormat.Format32bppRgb, data);
            using var copy = new Bitmap(source);

            if (width <= maxWidth)
            {
                return new Bitmap(copy);
            }

            int targetHeight = Math.Max(1, (int)(height * (maxWidth / (double)width)));
            var scaled = new Bitmap(maxWidth, targetHeight);

            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(copy, 0, 0, maxWidth, targetHeight);
            }

            return scaled;
        }
        finally
        {
            buffer.Unlock();
        }
    }

    private static void EnsureMediaFoundation()
    {
        if (_mfStarted)
        {
            return;
        }

        lock (typeof(PreviewFrames))
        {
            if (_mfStarted)
            {
                return;
            }

            MediaFactory.MFStartup(false);
            _mfStarted = true;
        }
    }
}
