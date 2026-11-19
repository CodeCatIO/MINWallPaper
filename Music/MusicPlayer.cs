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

    /// <summary>实际播放顺序，元素是 _queue 的下标；随机模式就是一份打乱后的列表。</summary>
    private readonly List<int> _order = new();
    private int _pos = -1;
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
        _pos >= 0 && _pos < _order.Count ? _queue[_order[_pos]] : null;

    private int CurrentQueueIndex =>
        _pos >= 0 && _pos < _order.Count ? _order[_pos] : -1;

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
        _mode = mode;
        QueueName = queueName;

        int start = Math.Clamp(startIndex, 0, _queue.Count - 1);
        _order.Clear();

        if (mode == MusicLoopMode.Shuffle)
        {
            // 随机模式：点中的歌先播，其余打乱，形成一份乱序播放列表。
            _order.Add(start);
            List<int> rest = Enumerable.Range(0, _queue.Count).Where(i => i != start).ToList();
            Shuffle(rest);
            _order.AddRange(rest);
            _pos = 0;
        }
        else
        {
            _order.AddRange(Enumerable.Range(0, _queue.Count));
            _pos = start;
        }

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

        Advance();
        StartCurrent();
    }

    public void Previous()
    {
        if (_queue.Count == 0 || _order.Count == 0)
        {
            return;
        }

        if (Position > 3)
        {
            Seek(0);
            return;
        }

        _pos = _pos <= 0 ? _order.Count - 1 : _pos - 1;
        StartCurrent();
    }

    /// <summary>往后走一首：随机模式下就是走到乱序列表的下一项。</summary>
    private void Advance()
    {
        if (_queue.Count == 0 || _order.Count == 0)
        {
            return;
        }

        if (_mode == MusicLoopMode.Shuffle)
        {
            int last = CurrentQueueIndex;
            _pos++;

            if (_pos >= _order.Count)
            {
                // 一轮播完，重新洗牌；避免新一轮第一首还是刚播完的那首。
                BuildShuffleBag(last);
                _pos = 0;
            }

            return;
        }

        _pos = _pos < 0 ? 0 : (_pos + 1) % _order.Count;
    }

    private void BuildShuffleBag(int avoidFirst)
    {
        _order.Clear();
        _order.AddRange(Enumerable.Range(0, _queue.Count));
        Shuffle(_order);

        if (_order.Count > 1 && _order[0] == avoidFirst)
        {
            int swap = 1 + _random.Next(_order.Count - 1);
            (_order[0], _order[swap]) = (_order[swap], _order[0]);
        }
    }

    private void Shuffle(List<int> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public void SetMode(MusicLoopMode mode)
    {
        if (_queue.Count > 0 && mode != _mode)
        {
            int current = CurrentQueueIndex;

            if (mode == MusicLoopMode.Shuffle)
            {
                // 已经播过的不再进入剩余列表，剩下的才洗牌。
                List<int> played = _order.Take(Math.Max(0, _pos + 1)).ToList();
                var playedSet = new HashSet<int>(played);
                List<int> remaining = Enumerable.Range(0, _queue.Count)
                    .Where(i => !playedSet.Contains(i)).ToList();
                Shuffle(remaining);

                _order.Clear();
                _order.AddRange(played);
                _order.AddRange(remaining);
                _pos = played.Count - 1;
            }
            else
            {
                // 退出随机时恢复原始顺序，并定位到正在播放的歌。
                _order.Clear();
                _order.AddRange(Enumerable.Range(0, _queue.Count));
                _pos = current >= 0 ? _order.IndexOf(current) : -1;
            }
        }

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
        _order.Clear();
        _pos = -1;
        QueueName = "";
        StateChanged?.Invoke();
    }

    /// <summary>歌曲被移到别的分类后，同步当前队列里的路径。</summary>
    public void ReplacePath(string oldPath, string newPath)
    {
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
        {
            return;
        }

        bool changed = false;
        for (int i = 0; i < _queue.Count; i++)
        {
            if (string.Equals(_queue[i], oldPath, StringComparison.OrdinalIgnoreCase))
            {
                _queue[i] = newPath;
                changed = true;
            }
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }
    }

    /// <summary>分类文件夹改名后，同步当前队列里的路径。</summary>
    public void ReplacePrefix(string oldPrefix, string newPrefix)
    {
        if (string.IsNullOrWhiteSpace(oldPrefix) || string.IsNullOrWhiteSpace(newPrefix))
        {
            return;
        }

        string oldRoot = oldPrefix.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string newRoot = newPrefix.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        bool changed = false;

        for (int i = 0; i < _queue.Count; i++)
        {
            if (_queue[i].StartsWith(oldRoot, StringComparison.OrdinalIgnoreCase))
            {
                _queue[i] = newRoot + _queue[i].Substring(oldRoot.Length);
                changed = true;
            }
        }

        if (changed)
        {
            StateChanged?.Invoke();
        }
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

            default:
                Advance();
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
