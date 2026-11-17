using System.IO;
using NAudio.Wave;

namespace MINWallPaper.Music;

internal enum MusicLoopMode
{
    Single,
    List,
    Shuffle
}

/// <summary>音乐播放器：队列播放、单曲/列表/随机循环、音量与进度。</summary>
internal sealed class MusicPlayer
{
    public static MusicPlayer Instance { get; } = new();

    private WaveOutEvent? _output;
    private WaveStream? _reader;
    private readonly List<string> _queue = new();
    private int _index = -1;
    private MusicLoopMode _mode = MusicLoopMode.List;
    private float _volume = 0.5f;
    private bool _muted;
    private bool _manualStop;
    private readonly Random _random = new();

    private MusicPlayer()
    {
    }

    public event Action? StateChanged;

    public string? CurrentPath =>
        _index >= 0 && _index < _queue.Count ? _queue[_index] : null;

    public string CurrentTitle =>
        CurrentPath == null ? "" : Path.GetFileNameWithoutExtension(CurrentPath);

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public double Position => _reader?.CurrentTime.TotalSeconds ?? 0;

    public double Duration => _reader?.TotalTime.TotalSeconds ?? 0;

    public MusicLoopMode Mode => _mode;

    public double Volume => _volume;

    public bool Muted => _muted;

    public string QueueName { get; private set; } = "";

    /// <summary>播放一整个队列。</summary>
    public void PlayQueue(IReadOnlyList<string> tracks, int startIndex, MusicLoopMode mode, string queueName)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        _queue.Clear();
        _queue.AddRange(tracks);
        _index = Math.Clamp(startIndex, 0, _queue.Count - 1);
        _mode = mode;
        QueueName = queueName;
        StartCurrent();
    }

    public void PlaySingle(string path)
    {
        PlayQueue(new[] { path }, 0, MusicLoopMode.Single, "单曲循环");
    }

    public void Toggle()
    {
        if (_output == null)
        {
            if (_queue.Count > 0)
            {
                StartCurrent();
            }

            return;
        }

        if (_output.PlaybackState == PlaybackState.Playing)
        {
            _output.Pause();
        }
        else
        {
            _output.Play();
        }

        StateChanged?.Invoke();
    }

    public void Next()
    {
        if (_queue.Count == 0)
        {
            return;
        }

        if (_mode == MusicLoopMode.Shuffle && _queue.Count > 1)
        {
            int next;
            do
            {
                next = _random.Next(_queue.Count);
            } while (next == _index);

            _index = next;
        }
        else
        {
            _index = (_index + 1) % _queue.Count;
        }

        StartCurrent();
    }

    public void Previous()
    {
        if (_queue.Count == 0)
        {
            return;
        }

        if (Position > 3)
        {
            Seek(0);
            return;
        }

        _index = _index <= 0 ? _queue.Count - 1 : _index - 1;
        StartCurrent();
    }

    public void SetMode(MusicLoopMode mode)
    {
        _mode = mode;
        StateChanged?.Invoke();
    }

    public void SetVolume(double volume)
    {
        _volume = (float)Math.Clamp(volume, 0, 1);

        if (_output != null)
        {
            _output.Volume = _muted ? 0f : _volume;
        }

        StateChanged?.Invoke();
    }

    public void SetMuted(bool muted)
    {
        _muted = muted;

        if (_output != null)
        {
            _output.Volume = _muted ? 0f : _volume;
        }

        StateChanged?.Invoke();
    }

    public void Seek(double seconds)
    {
        if (_reader == null)
        {
            return;
        }

        double target = Math.Clamp(seconds, 0, Math.Max(0, _reader.TotalTime.TotalSeconds - 0.5));
        _reader.CurrentTime = TimeSpan.FromSeconds(target);
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        StopInternal();
        _queue.Clear();
        _index = -1;
        QueueName = "";
        StateChanged?.Invoke();
    }

    private void StartCurrent()
    {
        StopInternal();

        string? path = CurrentPath;
        if (path == null)
        {
            return;
        }

        try
        {
            WaveStream reader;

            try
            {
                reader = new MediaFoundationReader(path);
            }
            catch
            {
                reader = new AudioFileReader(path);
            }

            _reader = reader;
            _output = new WaveOutEvent { Volume = _muted ? 0f : _volume };
            _output.Init(reader);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Play();
            Log.Write("音乐开始播放: " + path);
        }
        catch (Exception ex)
        {
            Log.Write("音乐播放失败: " + path + " : " + ex.Message);
        }

        StateChanged?.Invoke();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // 换歌时旧播放器可能异步抛出 Stopped，忽略过期实例，否则会多跳一首。
        if (!ReferenceEquals(sender, _output) || _manualStop || _queue.Count == 0)
        {
            return;
        }

        switch (_mode)
        {
            case MusicLoopMode.Single:
                StartCurrent();
                break;

            case MusicLoopMode.Shuffle when _queue.Count > 1:
            {
                int next;
                do
                {
                    next = _random.Next(_queue.Count);
                } while (next == _index);

                _index = next;
                StartCurrent();
                break;
            }

            default:
                _index = (_index + 1) % _queue.Count;
                StartCurrent();
                break;
        }
    }

    private void StopInternal()
    {
        _manualStop = true;

        if (_output != null)
        {
            try
            {
                _output.Stop();
            }
            catch
            {
                // 停止阶段忽略
            }

            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Dispose();
            _output = null;
        }

        _reader?.Dispose();
        _reader = null;
        _manualStop = false;
    }

    public void Dispose()
    {
        Stop();
    }
}
