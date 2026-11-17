using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace MINWallPaper.Playback;

/// <summary>
/// 用 Media Foundation 的 MediaEngine 把视频硬解渲染到指定窗口。
/// MediaEngine 内部走 EVR，全程 GPU 解码，不经过 WPF 的 MediaElement。
/// 切换视频时整只引擎重建，避免上一段还在加载时被强行换源导致 MF 报错。
/// </summary>
internal sealed class VideoWallpaper : IDisposable
{
    private IMFMediaEngineClassFactory? _factory;
    private IMFMediaEngine? _engine;
    private IntPtr _hwnd;
    private string? _source;
    private static bool _mfStarted;
    private double _volume = 0.5;
    private bool _muted = true;
    private bool _disposing;
    private bool _retrying;

    private readonly SynchronizationContext? _sync = SynchronizationContext.Current;

    /// <summary>播放出错时给 UI 的提示文本。</summary>
    public event Action<string>? PlaybackError;

    public bool IsAttached => _engine != null;

    /// <summary>把播放器绑定到目标窗口，必须在窗口挂到壁纸层之后调用。</summary>
    public void Attach(IntPtr hwnd)
    {
        if (_engine != null)
        {
            return;
        }

        _hwnd = hwnd;
        EnsureMediaFoundation();
        CreateEngine();
    }

    private void EnsureMediaFoundation()
    {
        if (_mfStarted)
        {
            return;
        }

        MediaFactory.MFStartup(false);
        _mfStarted = true;
    }

    private void CreateEngine()
    {
        _engine?.Dispose();
        _engine = null;

        _factory ??= new IMFMediaEngineClassFactory();

        IMFAttributes attributes = MediaFactory.MFCreateAttributes(4);
        attributes.Set(MediaEngineAttributeKeys.PlaybackHwnd.Guid, (ulong)_hwnd.ToInt64());

        _engine = _factory.CreateInstance(MediaEngineCreateFlags.None, attributes, OnMediaEngineEvent);
        _engine.Loop = true;
        _engine.AutoPlay = false;
        _engine.Volume = _volume;
        _engine.Muted = _muted;
    }

    public void Play(string path)
    {
        if (_engine == null || _hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("壁纸层还没有挂载。");
        }

        // 每次切换都重建引擎，状态干净，连续快速点不同视频也不会互相干扰。
        CreateEngine();
        _retrying = false;
        _source = path;

        _engine!.SetSource(path).CheckError();
        _engine.Load();
        _engine.Play().CheckError();
        Log.Write("开始播放: " + path);
    }

    public void Pause()
    {
        _engine?.Pause().CheckError();
    }

    /// <summary>当前播放位置（秒），用于判断画面是不是卡住了。</summary>
    public double CurrentTime => _engine?.CurrentTime ?? 0;

    /// <summary>音量 0~1。</summary>
    public void SetVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        if (_engine != null)
        {
            _engine.Volume = _volume;
        }
    }

    public void SetMuted(bool muted)
    {
        _muted = muted;
        if (_engine != null)
        {
            _engine.Muted = _muted;
        }
    }

    private void OnMediaEngineEvent(MediaEngineEvent mediaEvent, nuint param1, int param2)
    {
        if (mediaEvent != MediaEngineEvent.TimeUpdate)
        {
            Log.Write($"[MF] {mediaEvent}");
        }

        if (mediaEvent == MediaEngineEvent.Error && !_disposing)
        {
            PlaybackError?.Invoke("播放失败，请换一个 H.264 编码的 MP4 试试。");
            ScheduleRetry();
        }
    }

    /// <summary>引擎刚启动时偶发的加载错误，隔半秒自动重试一次。</summary>
    private void ScheduleRetry()
    {
        if (_retrying || _disposing || _source == null)
        {
            return;
        }

        _retrying = true;
        string source = _source;

        _ = Task.Delay(600).ContinueWith(_ =>
        {
            void Retry()
            {
                try
                {
                    if (_disposing || _engine == null || _source != source)
                    {
                        return;
                    }

                    CreateEngine();
                    _engine!.SetSource(source).CheckError();
                    _engine.Load();
                    _engine.Play().CheckError();
                    Log.Write("视频播放已自动重试: " + source);
                }
                catch (Exception ex)
                {
                    Log.Write("视频重试失败: " + ex.Message);
                }
                finally
                {
                    _retrying = false;
                }
            }

            if (_sync != null)
            {
                _sync.Post(_ => Retry(), null);
            }
            else
            {
                Retry();
            }
        });
    }

    /// <summary>把播放器当前状态写进日志，便于排查渲染问题。</summary>
    public string GetDiagnostics()
    {
        if (_engine == null)
        {
            return "引擎未创建";
        }

        return $"state={_engine.ReadyState} time={_engine.CurrentTime:F2}s paused={_engine.IsPaused} " +
               $"size={_engine.NativeVideoSize.Width}x{_engine.NativeVideoSize.Height}";
    }

    public void Dispose()
    {
        _disposing = true;

        if (_engine != null)
        {
            try { _engine.Shutdown(); } catch { /* 关闭阶段忽略 */ }
            _engine.Dispose();
            _engine = null;
        }

        _factory?.Dispose();
        _factory = null;
        _hwnd = IntPtr.Zero;
        _source = null;
    }
}
