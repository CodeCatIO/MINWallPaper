using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using MINWallPaper.Music;
using MINWallPaper.Native;
using MINWallPaper.Playback;
using MINWallPaper.Storage;

namespace MINWallPaper;

/// <summary>
/// 常驻的壁纸宿主。
/// 视频：MediaFoundation 硬解，窗口挂到桌面图标层下方。
/// 图片：直接交给 Windows 设置系统壁纸，不占常驻资源。
/// </summary>
internal sealed class WallpaperHost
{
    private static readonly HashSet<string> VideoExts =
        new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".wmv", ".avi", ".mov", ".mkv" };

    public static WallpaperHost Instance { get; } = new();

    private readonly VideoWallpaper _renderer = new();
    private Form? _window;
    private IntPtr _hwnd;
    private Image? _poster;

    private System.Windows.Forms.Timer? _healthTimer;
    private double _lastPosition = -1;
    private bool _recovering;

    private WallpaperHost()
    {
        _renderer.PlaybackError += message => PlaybackError?.Invoke(message);

        // 视频壁纸偶尔会因为显卡/解码器被别的程序抢占而停住，定时检查播放进度并自动重启。
        // 同时检查桌面宿主窗口是否被 Explorer 重建，必要时重新挂载视频层。
        _healthTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _healthTimer.Tick += (_, _) =>
        {
            EnsureVideoWallpaperCleared();
            CheckLayerHealth();
            CheckVideoHealth();
        };
        _healthTimer.Start();
    }

    /// <summary>播放视频时静态壁纸必须保持为空，否则会重新盖住视频层。</summary>
    private void EnsureVideoWallpaperCleared()
    {
        if (CurrentKind == WallpaperKind.Video && SystemWallpaper.IsDesktopWallpaperSet())
        {
            SystemWallpaper.ClearForVideo();
            Log.Write("检测到系统壁纸重新出现，再次清除");
        }
    }

    /// <summary>排查桌面层是否还挂在当前 WorkerW 上。</summary>
    private void CheckLayerHealth()
    {
        if (_recovering || CurrentKind != WallpaperKind.Video || _hwnd == IntPtr.Zero)
        {
            return;
        }

        IntPtr host = DesktopHost.FindHost();

        if (host == IntPtr.Zero)
        {
            return;
        }

        bool handleOk = NativeMethods.IsWindow(_hwnd);
        IntPtr parent = handleOk ? NativeMethods.GetParent(_hwnd) : IntPtr.Zero;
        bool visible = handleOk && NativeMethods.IsWindowVisible(_hwnd);

        if (handleOk && parent == host && visible)
        {
            return;
        }

        Log.Write($"壁纸层异常（handle={handleOk} parent=0x{parent:X} host=0x{host:X} visible={visible}），重新挂载");
        RecoverLayer();
    }

    /// <summary>宿主被重建或窗口丢失时，把视频层重新挂回桌面并重启渲染。</summary>
    private void RecoverLayer()
    {
        if (_recovering || CurrentPath == null || CurrentKind != WallpaperKind.Video)
        {
            return;
        }

        _recovering = true;
        string path = CurrentPath;

        try
        {
            // 不论旧句柄是否还有效，都整体重建窗口和渲染引擎：
            // 宿主切换后 EVR 的输出表面已经不可靠，复用句柄容易一直报错。
            _renderer.Dispose();
            CloseLayer();
            EnsureLayer();
            ApplyVideoPoster(path);
            _renderer.Attach(_hwnd);
            _renderer.SetMuted(true);
            _renderer.Play(path);
            _lastPosition = -1;
            Log.Write("视频壁纸层已恢复");
        }
        catch (Exception ex)
        {
            Log.Write("恢复视频壁纸失败: " + ex.Message);
        }
        finally
        {
            _recovering = false;
        }
    }

    /// <summary>手动重新加载当前壁纸，托盘菜单用。</summary>
    public void Reload()
    {
        if (CurrentPath == null)
        {
            return;
        }

        if (CurrentKind == WallpaperKind.Video)
        {
            RecoverLayer();
        }
        else
        {
            Play(CurrentPath);
        }
    }

    private void CheckVideoHealth()
    {
        if (_recovering || CurrentKind != WallpaperKind.Video || !IsPlaying || CurrentPath == null)
        {
            _lastPosition = -1;
            return;
        }

        double position = _renderer.CurrentTime;
        if (position <= 0)
        {
            _lastPosition = -1;
            return;
        }

        if (_lastPosition >= 0 && Math.Abs(position - _lastPosition) < 0.05)
        {
            Log.Write($"视频壁纸卡住，自动重启: {CurrentPath}");
            _recovering = true;

            try
            {
                Play(CurrentPath);
            }
            catch (Exception ex)
            {
                Log.Write("自动重启视频失败: " + ex.Message);
            }
            finally
            {
                _recovering = false;
                _lastPosition = -1;
            }

            return;
        }

        _lastPosition = position;
    }

    public string? CurrentPath { get; private set; }

    public WallpaperKind? CurrentKind { get; private set; }

    public bool IsPlaying { get; private set; }

    /// <summary>图片壁纸的填充方式，切换时生效。</summary>
    public ImageFillMode FillMode { get; private set; } = ImageFillMode.Fill;

    /// <summary>图片在屏幕上的垂直位置，0 顶部 / 0.5 居中 / 1 底部。</summary>
    public double ImageOffset { get; private set; } = 0.5;

    /// <summary>视频壁纸音量，0~1。</summary>
    public double Volume { get; private set; } = 0.5;

    public bool Muted { get; private set; } = true;

    public event Action<string>? PlaybackError;
    public event Action? StateChanged;

    /// <summary>设置壁纸。视频和图片都走这里，按扩展名自动分流。</summary>
    public void Play(string path)
    {
        // 先记录路径，后台的首帧兜底任务要拿它判断用户有没有切走。
        CurrentPath = path;

        if (VideoExts.Contains(Path.GetExtension(path)))
        {
            PlayVideo(path);
        }
        else
        {
            PlayImage(path);
        }

        IsPlaying = true;

        // 每张壁纸可以配自己的音乐，切壁纸时自动切过去。
        MusicAssignments.ApplyTo(path);

        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (CurrentKind != WallpaperKind.Video)
        {
            return;
        }

        _renderer.Pause();
        IsPlaying = false;
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        bool wasVideo = CurrentKind == WallpaperKind.Video;

        _renderer.Dispose();
        CloseLayer();

        if (wasVideo)
        {
            SystemWallpaper.RestoreAfterVideo();
        }

        IsPlaying = false;
        CurrentPath = null;
        CurrentKind = null;
        StateChanged?.Invoke();
    }

    public void SetFillMode(ImageFillMode mode)
    {
        FillMode = mode;

        if (CurrentKind == WallpaperKind.Image && CurrentPath != null)
        {
            SystemWallpaper.Apply(CurrentPath, mode, ImageOffset);
        }
    }

    public void SetImageOffset(double offset)
    {
        ImageOffset = Math.Clamp(offset, 0, 1);

        if (CurrentKind == WallpaperKind.Image && CurrentPath != null)
        {
            SystemWallpaper.Apply(CurrentPath, FillMode, ImageOffset);
        }
    }

    public void SetVolume(double volume)
    {
        Volume = Math.Clamp(volume, 0, 1);
        _renderer.SetVolume(Volume);
    }

    public void SetMuted(bool muted)
    {
        Muted = muted;
        _renderer.SetMuted(muted);
    }

    public string GetDiagnostics() => _renderer.GetDiagnostics();

    private void PlayVideo(string path)
    {
        // 换视频时把整个壁纸窗口和引擎一起重建，DComp 表面也一起换新的。
        // 复用旧窗口连续换源会让 MediaEngine 偶发 Error，表现为桌面停在静态首帧。
        _renderer.Dispose();
        CloseLayer();

        // 静态壁纸会盖在视频层上面，播放视频前必须把系统壁纸清掉。
        SystemWallpaper.ClearForVideo();

        EnsureLayer();
        ApplyVideoPoster(path);
        _renderer.Attach(_hwnd);

        // 视频壁纸本身的声音一律不要，桌面只放出音乐。
        _renderer.SetMuted(true);
        _renderer.Play(path);
        CurrentKind = WallpaperKind.Video;
        _lastPosition = -1;
        Log.Write("壁纸切换为视频: " + path);
    }

    private void PlayImage(string path)
    {
        // 图片不需要自己的壁纸窗口，先停掉视频并销毁窗口，再交给系统。
        _renderer.Dispose();
        CloseLayer();

        if (!SystemWallpaper.Apply(path, FillMode, ImageOffset))
        {
            throw new InvalidOperationException("设置系统壁纸失败，请检查文件是否可用。");
        }

        CurrentKind = WallpaperKind.Image;
        Log.Write("壁纸切换为系统图片: " + path);
    }

    /// <summary>创建壁纸窗口并挂到桌面图标下方，只做一次。</summary>
    private void EnsureLayer()
    {
        if (_window != null)
        {
            return;
        }

        int cx = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int cy = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        var window = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(-32000, -32000, cx, cy),
            ShowInTaskbar = false,
            BackColor = Color.Black
        };

        // 先 Show 一次拿到窗口句柄，再挂到桌面壁纸层。
        window.Show();
        IntPtr hwnd = window.Handle;

        IntPtr host = DesktopHost.Attach(hwnd);
        Log.Write($"壁纸窗口 0x{hwnd:X} 已挂到宿主 0x{host:X}");

        PrepareLayerWindow(hwnd);

        _window = window;
        _hwnd = hwnd;
    }

    /// <summary>把窗口改成桌面子窗口并铺满屏幕。</summary>
    private static void PrepareLayerWindow(IntPtr hwnd)
    {
        int cx = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int cy = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        style = (style | NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE) & ~NativeMethods.WS_POPUP;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_STYLE, style);

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE,
            exStyle | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);

        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, cx, cy,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
    }

    /// <summary>
    /// 视频窗口背后垫一张首帧封面：切显卡/切前后台的一瞬间，
    /// 万一渲染表面没来得及出新帧，露出来的是画面而不是黑屏。
    /// </summary>
    private string? ApplyVideoPoster(string path)
    {
        if (_window == null)
        {
            return null;
        }

        WallpaperItem? item = WallpaperLibrary.Instance.Items
            .FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));

        string? thumb = item?.ThumbnailPath;
        if (string.IsNullOrEmpty(thumb) || !File.Exists(thumb))
        {
            return null;
        }

        try
        {
            using var source = Image.FromFile(thumb);
            var poster = new Bitmap(source);

            _poster?.Dispose();
            _poster = poster;
            _window.BackgroundImage = poster;
            _window.BackgroundImageLayout = ImageLayout.Stretch;
            return thumb;
        }
        catch (Exception ex)
        {
            Log.Write("设置视频兜底画面失败: " + ex.Message);
            return null;
        }
    }

    private void CloseLayer()
    {
        if (_window == null)
        {
            return;
        }

        _window.BackgroundImage = null;
        _poster?.Dispose();
        _poster = null;

        NativeMethods.SetParent(_hwnd, IntPtr.Zero);
        _window.Close();
        _window.Dispose();
        _window = null;
        _hwnd = IntPtr.Zero;
    }
}
