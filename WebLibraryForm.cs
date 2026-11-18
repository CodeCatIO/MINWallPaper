using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MINWallPaper.Music;
using MINWallPaper.Native;
using MINWallPaper.Playback;
using MINWallPaper.Storage;
using MINWallPaper.Widgets;

namespace MINWallPaper;

/// <summary>
/// 用 WebView2 承载界面：HTML/CSS 负责布局，天然适配窗口缩放；
/// C# 只负责壁纸、组件、音乐这些系统能力，两边用 JSON 消息通信。
/// </summary>
internal sealed class WebLibraryForm : Form
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly WebView2 _web = new();
    private readonly System.Windows.Forms.Timer _playerTimer;
    private readonly System.Windows.Forms.Timer _resizeTimer;
    private bool _ready;
    private bool _disposed;
    private bool? _lastMaximized;
    private bool _moving;
    private string _resizeEdge = "";
    private Point _resizeCursorStart;
    private Rectangle _resizeBoundsStart;

    public WebLibraryForm()
    {
        Text = "MINWallPaper";
        Icon = AppIcon.Value;
        FormBorderStyle = FormBorderStyle.None;
        ClientSize = new Size(1180, 740);
        MinimumSize = new Size(880, 600);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(10, 10, 12);
        DoubleBuffered = true;

        _web.Dock = DockStyle.Fill;
        _web.DefaultBackgroundColor = Color.FromArgb(10, 10, 12);
        Controls.Add(_web);

        Load += async (_, _) => await InitializeAsync();

        WallpaperLibrary.Instance.Changed += OnDataChanged;
        WallpaperLibrary.Instance.Status += OnStatus;
        MusicLibrary.Instance.Changed += OnDataChanged;
        MusicLibrary.Instance.Scanned += OnDataChanged;
        WallpaperHost.Instance.StateChanged += OnDataChanged;
        PreviewFrames.Ready += OnPreviewReady;
        MusicPlayer.Instance.StateChanged += OnPlayerChanged;
        MusicArtwork.Ready += OnPlayerChanged;
        WidgetService.Instance.Changed += OnWidgetsChanged;

        // 歌词高亮和进度条按 250ms 跟随播放；暂停时降到 1s，避免无意义刷新。
        _playerTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _playerTimer.Tick += (_, _) =>
        {
            if (!_ready || _disposed || MusicPlayer.Instance.CurrentPath == null)
            {
                if (_playerTimer.Interval != 1000)
                {
                    _playerTimer.Interval = 1000;
                }

                return;
            }

            Post(new { type = "player", player = BuildPlayer() });

            int interval = MusicPlayer.Instance.IsPlaying ? 250 : 1000;

            if (_playerTimer.Interval != interval)
            {
                _playerTimer.Interval = interval;
            }
        };
        _playerTimer.Start();

        // 自己处理贴边缩放：窗口不带系统可缩放边框，顶部就不会出现一条白边。
        _resizeTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _resizeTimer.Tick += (_, _) => ResizeStep();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        try
        {
            int preference = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(
                Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

            // 系统会给可缩放的无边框窗口描一圈浅色边，改成和背景一样的深色，顶部就不会有白线。
            int darkBorder = 0x00101010; // COLORREF: 0x00BBGGRR
            NativeMethods.DwmSetWindowAttribute(
                Handle, NativeMethods.DWMWA_BORDER_COLOR, ref darkBorder, sizeof(int));
        }
        catch
        {
            // 旧系统没有这个属性，保持直角即可。
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PostWindowState();
    }

    private async Task InitializeAsync()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                await InitializeOnceAsync();
                return;
            }
            catch (Exception ex)
            {
                Log.Write($"WebView2 初始化失败({attempt + 1}/2): " + ex);

                if (attempt == 1)
                {
                    MessageBox.Show(this,
                        "界面组件初始化失败，请确认系统已安装 WebView2 Runtime。\n\n" + ex.Message,
                        "MINWallPaper", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    await Task.Delay(800);
                }
            }
        }
    }

    private async Task InitializeOnceAsync()
    {
        string cache = Path.Combine(AppPaths.DataDir, "webview2");
            Directory.CreateDirectory(cache);

            var options = new CoreWebView2EnvironmentOptions
            {
                // 关掉后台联网、组件更新、扩展这些用不到的能力，减少常驻内存和磁盘活动。
                // 预览视频强制走软解，把显卡硬解会话留给桌面动态壁纸，避免两边抢解码器。
                // 整个界面用软件渲染，避免和桌面动态壁纸抢显卡资源导致黑闪。
                AdditionalBrowserArguments =
                    "--disable-background-networking --disable-component-update --disable-sync " +
                    "--no-first-run --disable-extensions --disable-gpu"
            };

            CoreWebView2Environment environment =
                await CoreWebView2Environment.CreateAsync(null, cache, options);
            await _web.EnsureCoreWebView2Async(environment);

            CoreWebView2 core = _web.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(
                "minwall.local", AppPaths.BaseDir, CoreWebView2HostResourceAccessKind.Allow);

            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsPinchZoomEnabled = false;

            core.WebMessageReceived += OnWebMessage;
            core.NavigationCompleted += (_, _) =>
            {
                _ready = true;
                PushState();
            };

            core.Navigate("https://minwall.local/Web/index.html");
            Log.Write("Web UI 已加载");
    }

    private void OnDataChanged()
    {
        if (_disposed)
        {
            return;
        }

        PushState();
    }

    private void OnStatus(string text)
    {
        if (_disposed || !_ready)
        {
            return;
        }

        Post(new { type = "status", text });
    }

    private void Post(object payload)
    {
        if (_disposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke((Action)(() => Post(payload)));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        try
        {
            _web.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(payload, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Write("推送界面消息失败: " + ex.Message);
        }
    }

    private void PushState(string? reason = null)
    {
        if (!_ready || _disposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke((Action)(() => PushState(reason)));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        WallpaperHost host = WallpaperHost.Instance;
        WallpaperLibrary library = WallpaperLibrary.Instance;

        object? assignment = null;
        MusicAssignment? saved = MusicAssignments.Get(host.CurrentPath);
        if (saved != null)
        {
            assignment = new { kind = saved.Kind, value = saved.Value };
        }

        var payload = new
        {
            type = "state",
            reason,
            status = (string?)null,
            current = new
            {
                path = host.CurrentPath,
                kind = host.CurrentKind == WallpaperKind.Video ? "video"
                    : host.CurrentKind == WallpaperKind.Image ? "image" : null,
                volume = host.Volume,
                muted = host.Muted
            },
            wallpapers = library.Items.Select(item => new
            {
                path = item.Path,
                name = item.Name,
                kind = item.Kind == WallpaperKind.Video ? "video" : "image",
                sizeBytes = item.SizeBytes,
                favorite = item.Favorite,
                thumb = item.ThumbnailPath == null ? null : ToUrl(item.ThumbnailPath),
                media = ToUrl(item.Path)
            }).ToList(),
            widgets = BuildWidgets(),
            rain = WidgetService.Instance.RainEnabled,            music = new
            {
                scanning = MusicLibrary.Instance.IsScanning,
                files = MusicLibrary.Instance.Files.Select(file => new
                {
                    path = file.Path,
                    title = file.Title,
                    artist = ArtistOf(file.Path),
                    sizeBytes = file.SizeBytes,
                    duration = file.Duration.TotalSeconds,
                    url = ToUrl(file.Path)
                }).ToList(),
                playlists = MusicLibrary.Instance.Playlists.Select(playlist => new
                {
                    id = playlist.Id,
                    name = playlist.Name,
                    tracks = playlist.Tracks.Select(track => new
                    {
                        path = track.Path,
                        title = track.Title,
                        url = ToUrl(track.Path)
                    }).ToList()
                }).ToList()
            },
            player = BuildPlayer(),
            window = new
            {
                maximized = WindowState == FormWindowState.Maximized
            },
            assignment
        };

        Post(payload);
    }

    private static List<object> BuildWidgets()
    {
        var list = new List<object>();

        foreach (WidgetConfig config in WidgetService.Instance.Configs)
        {
            string? preview = null;
            string? file = WidgetPreview.Ensure(config);

            if (file != null)
            {
                preview = ToUrl(file) + "?v=" + File.GetLastWriteTimeUtc(file).Ticks;
            }

            list.Add(new
            {
                id = config.Id,
                type = config.Type,
                name = config.Name,
                enabled = config.Enabled,
                style = config.Style,
                styleName = WidgetConfig.StyleName(config.Type, config.Style),
                anchor = config.Anchor,
                offsetX = config.OffsetX,
                offsetY = config.OffsetY,
                posX = config.PosX,
                posY = config.PosY,
                width = config.Width,
                height = config.Height,
                radius = config.Radius,
                textColor = config.TextColor,
                accentColor = config.AccentColor,
                accentColor2 = config.AccentColor2,
                bgColor = config.BgColor,
                bgAlpha = config.BgAlpha,
                fontFamily = config.FontFamily,
                fontSize = config.FontSize,
                textAlpha = config.TextAlpha,
                goldText = config.GoldText,
                signatureColor = config.SignatureColor,
                signatureAlpha = config.SignatureAlpha,
                clockFormat = config.ClockFormat,
                dateFormat = config.DateFormat,
                showWeek = config.ShowWeek,
                content = config.Content,
                images = config.Images,
                carouselSeconds = config.CarouselSeconds,
                sensitivity = config.Sensitivity,
                barCount = config.BarCount,
                preview
            });
        }

        return list;
    }

    /// <summary>只推组件数据，编辑时免得整份壁纸/音乐状态一起重发。</summary>
    private void PostWidgets()
    {
        Post(new
        {
            type = "widgets",
            widgets = BuildWidgets(),
            rain = WidgetService.Instance.RainEnabled
        });
    }

    private static object BuildPlayer()
    {
        MusicPlayer player = MusicPlayer.Instance;
        string? art = null;
        string? lyrics = null;

        if (player.CurrentPath != null)
        {
            if (MusicArtwork.Get(player.CurrentPath) is string artwork)
            {
                art = ToUrl(artwork);
            }

            string lrc = Path.ChangeExtension(player.CurrentPath, ".lrc");
            if (File.Exists(lrc))
            {
                lyrics = ToUrl(lrc);
            }
        }

        return new
        {
            path = player.CurrentPath,
            title = player.CurrentTitle,
            playing = player.IsPlaying,
            position = player.Position,
            duration = player.Duration,
            mode = ModeName(player.Mode),
            volume = player.Volume,
            muted = player.Muted,
            queueName = player.QueueName,
            art,
            lyrics
        };
    }

    private void PostWindowState(bool force = false)
    {
        if (_disposed || !_ready)
        {
            return;
        }

        bool maximized = WindowState == FormWindowState.Maximized;
        if (!force && _lastMaximized == maximized)
        {
            return;
        }

        _lastMaximized = maximized;
        Post(new { type = "window", maximized });
    }

    private void DragWindow()
    {
        if (WindowState != FormWindowState.Normal)
        {
            return;
        }

        _moving = true;
        _resizeEdge = "";
        _resizeCursorStart = Cursor.Position;
        _resizeBoundsStart = Bounds;
        _resizeTimer.Start();
    }

    private void StartResize(string edge)
    {
        if (WindowState != FormWindowState.Normal || string.IsNullOrEmpty(edge))
        {
            return;
        }

        _resizeEdge = edge;
        _moving = false;
        _resizeCursorStart = Cursor.Position;
        _resizeBoundsStart = Bounds;
        _resizeTimer.Start();
    }

    private void ResizeStep()
    {
        if ((Control.MouseButtons & MouseButtons.Left) == 0)
        {
            _resizeTimer.Stop();
            _moving = false;
            _resizeEdge = "";
            return;
        }

        Point cursor = Cursor.Position;
        int dx = cursor.X - _resizeCursorStart.X;
        int dy = cursor.Y - _resizeCursorStart.Y;

        if (_moving)
        {
            Bounds = new Rectangle(
                _resizeBoundsStart.Left + dx,
                _resizeBoundsStart.Top + dy,
                _resizeBoundsStart.Width,
                _resizeBoundsStart.Height);
            return;
        }

        int left = _resizeBoundsStart.Left;
        int top = _resizeBoundsStart.Top;
        int right = _resizeBoundsStart.Right;
        int bottom = _resizeBoundsStart.Bottom;

        if (_resizeEdge.Contains("left")) left += dx;
        if (_resizeEdge.Contains("right")) right += dx;
        if (_resizeEdge.Contains("top")) top += dy;
        if (_resizeEdge.Contains("bottom")) bottom += dy;

        int minWidth = MinimumSize.Width;
        int minHeight = MinimumSize.Height;

        if (right - left < minWidth)
        {
            if (_resizeEdge.Contains("left")) left = right - minWidth;
            else right = left + minWidth;
        }

        if (bottom - top < minHeight)
        {
            if (_resizeEdge.Contains("top")) top = bottom - minHeight;
            else bottom = top + minHeight;
        }

        Bounds = new Rectangle(left, top, right - left, bottom - top);
    }

    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
        }
        else
        {
            // 无边框窗口最大化时默认会盖住任务栏，限制在工作区内。
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }

        PostWindowState(true);
    }

    private static string ModeName(MusicLoopMode mode) => mode switch
    {
        MusicLoopMode.Single => "single",
        MusicLoopMode.Shuffle => "shuffle",
        _ => "list"
    };

    private static MusicLoopMode ParseMode(string value) => value switch
    {
        "single" => MusicLoopMode.Single,
        "shuffle" => MusicLoopMode.Shuffle,
        _ => MusicLoopMode.List
    };

    private void OnPlayerChanged()
    {
        if (_disposed || !_ready)
        {
            return;
        }

        Post(new { type = "player", player = BuildPlayer() });
    }

    private void OnWidgetsChanged()
    {
        if (_disposed || !_ready)
        {
            return;
        }

        PostWidgets();
    }

    private static string ToUrl(string fullPath)
    {
        string relative = Path.GetRelativePath(AppPaths.BaseDir, fullPath).Replace('\\', '/');
        return "https://minwall.local/" +
               string.Join("/", relative.Split('/').Select(Uri.EscapeDataString));
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = document.RootElement;

            string cmd = GetString(root, "cmd");

            switch (cmd)
            {
                case "init":
                    PushState();
                    break;

                case "play":
                    PlayWallpaper(GetString(root, "path"));
                    break;

                case "getPreview":
                    RequestPreview(GetString(root, "path"));
                    break;

                case "favorite":
                    ToggleFavorite(GetString(root, "path"));
                    break;

                case "playerToggle":
                    MusicPlayer.Instance.Toggle();
                    break;

                case "playerNext":
                    MusicPlayer.Instance.Next();
                    break;

                case "playerPrev":
                    MusicPlayer.Instance.Previous();
                    break;

                case "playerMode":
                    MusicPlayer.Instance.SetMode(ParseMode(GetString(root, "mode")));
                    break;

                case "playerSeek":
                    MusicPlayer.Instance.Seek(GetDouble(root, "value"));
                    break;

                case "playerVolume":
                    MusicPlayer.Instance.SetVolume(GetDouble(root, "value"));
                    break;

                case "playerMute":
                    MusicPlayer.Instance.SetMuted(
                        root.TryGetProperty("value", out JsonElement playerMute) &&
                        playerMute.ValueKind == JsonValueKind.True);
                    break;

                case "winDrag":
                    DragWindow();
                    break;

                case "winMin":
                    WindowState = FormWindowState.Minimized;
                    break;

                case "winMax":
                    ToggleMaximize();
                    break;

                case "winClose":
                    Close();
                    break;

                case "winResize":
                    StartResize(GetString(root, "edge"));
                    break;

                case "playLocal":
                    PlayLocal(GetInt(root, "index"));
                    break;

                case "playArtist":
                    PlayArtist(GetString(root, "artist"), GetString(root, "path"));
                    break;

                case "playPlaylist":
                    PlayPlaylist(GetString(root, "id"));
                    break;

                case "assignPlaylist":
                    AssignPlaylist(GetString(root, "id"));
                    break;

                case "assignTrack":
                    AssignTrack(GetString(root, "path"));
                    break;

                case "addWidget":
                    WidgetService.Instance.Add(GetString(root, "kind"));
                    PostWidgets();
                    break;

                case "updateWidget":
                    if (root.TryGetProperty("widget", out JsonElement widgetElement))
                    {
                        WidgetService.Instance.Update(WidgetConfig.FromJson(widgetElement.GetRawText()));
                    }

                    PostWidgets();
                    break;

                case "beginWidgetDrag":
                    WidgetService.Instance.SetDragMode(GetString(root, "id"));
                    break;

                case "endWidgetDrag":
                    WidgetService.Instance.SetDragMode(null);
                    break;

                case "deleteWidget":
                    WidgetService.Instance.Remove(GetString(root, "id"));
                    PostWidgets();
                    break;

                case "setWidgetEnabled":
                    WidgetService.Instance.SetEnabled(
                        GetString(root, "id"),
                        root.TryGetProperty("enabled", out JsonElement widgetEnabled) && widgetEnabled.GetBoolean());
                    PostWidgets();
                    break;

                case "setRain":
                    WidgetService.Instance.SetRainEnabled(
                        root.TryGetProperty("enabled", out JsonElement rainEnabled) && rainEnabled.GetBoolean());
                    PostWidgets();
                    break;

                case "pickWidgetImages":
                    PickWidgetImages(GetString(root, "id"));
                    break;

                case "clearWidgetImages":
                    WidgetService.Instance.SetImages(GetString(root, "id"), Array.Empty<string>());
                    break;

                case "rescanWallpapers":
                    WallpaperLibrary.Instance.Rescan();
                    break;

                case "openWallpaperFolder":
                    OpenFolder(WallpaperLibrary.Instance.Root);
                    break;

                case "createPlaylist":
                    string name = GetString(root, "name");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        MusicLibrary.Instance.CreatePlaylist(name.Trim());
                    }

                    break;

                case "deletePlaylist":
                    MusicPlaylist? playlist = MusicLibrary.Instance.Playlists
                        .FirstOrDefault(p => p.Id == GetString(root, "id"));
                    if (playlist != null)
                    {
                        MusicLibrary.Instance.RemovePlaylist(playlist);
                    }

                    break;

                case "addTrack":
                    AddTrack(GetString(root, "playlistId"), GetString(root, "path"));
                    break;

                case "removeTrack":
                    RemoveTrack(GetString(root, "playlistId"), GetString(root, "path"));
                    break;

                case "moveTrack":
                    MoveTrack(GetString(root, "playlistId"), GetInt(root, "from"), GetInt(root, "to"));
                    break;

                case "rescanMusic":
                    MusicLibrary.Instance.Rescan();
                    break;

                case "openMusicFolder":
                    OpenFolder(MusicLibrary.Instance.Root);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write("处理界面消息失败: " + ex);
        }
    }

    private static void PlayLocal(int index)
    {
        IReadOnlyList<MusicFile> files = MusicLibrary.Instance.Files;
        if (index < 0 || index >= files.Count)
        {
            return;
        }

        MusicPlayer.Instance.PlayQueue(
            files.Select(f => f.Path).ToList(), index,
            MusicPlayer.Instance.Mode, "本地音乐");
    }

    /// <summary>按歌曲所在文件夹归类；目前只区分周杰伦、许嵩，其余都进「其他」。</summary>
    private static string ArtistOf(string path)
    {
        string folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        return folder is "周杰伦" or "许嵩" ? folder : "其他";
    }

    private static void PlayArtist(string artist, string? path)
    {
        var files = MusicLibrary.Instance.Files
            .Where(f => ArtistOf(f.Path) == artist)
            .ToList();

        if (files.Count == 0)
        {
            return;
        }

        int index = 0;
        if (!string.IsNullOrEmpty(path))
        {
            int hit = files.FindIndex(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (hit >= 0)
            {
                index = hit;
            }
        }

        MusicPlayer.Instance.PlayQueue(
            files.Select(f => f.Path).ToList(), index,
            MusicPlayer.Instance.Mode, artist);
    }

    private static void PlayPlaylist(string playlistId)
    {
        MusicPlaylist? playlist = MusicLibrary.Instance.Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (playlist is { Tracks.Count: > 0 })
        {
            MusicPlayer.Instance.PlayQueue(
                playlist.Tracks.Select(t => t.Path).ToList(), 0,
                MusicPlayer.Instance.Mode, playlist.Name);
        }
    }

    private void AssignPlaylist(string playlistId)
    {
        string? wallpaper = WallpaperHost.Instance.CurrentPath;
        MusicPlaylist? playlist = MusicLibrary.Instance.Playlists.FirstOrDefault(p => p.Id == playlistId);

        if (wallpaper == null || playlist == null)
        {
            return;
        }

        MusicAssignments.Set(wallpaper, "playlist", playlist.Id);

        if (playlist.Tracks.Count > 0)
        {
            MusicPlayer.Instance.PlayQueue(
                playlist.Tracks.Select(t => t.Path).ToList(), 0,
                MusicLoopMode.List, playlist.Name);
        }

        PushState();
    }

    private void AssignTrack(string path)
    {
        string? wallpaper = WallpaperHost.Instance.CurrentPath;

        if (wallpaper == null || !File.Exists(path))
        {
            return;
        }

        MusicAssignments.Set(wallpaper, "track", path);
        MusicPlayer.Instance.PlaySingle(path);
        PushState();
    }

    private static double GetDouble(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.TryGetDouble(out double result)
            ? result
            : 0;
    }

    private void RequestPreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string[]? frames = PreviewFrames.GetFrames(path);
        if (frames != null)
        {
            PostPreview(path, frames);
        }
    }

    private void OnPreviewReady(string path)
    {
        string[]? frames = PreviewFrames.GetFrames(path);
        if (frames != null)
        {
            PostPreview(path, frames);
        }
    }

    private void PostPreview(string path, string[] frames)
    {
        Post(new
        {
            type = "preview",
            path,
            frames = frames.Select(ToUrl).ToArray()
        });
    }

    private void PlayWallpaper(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            WallpaperHost.Instance.Play(path);
        }
        catch (Exception ex)
        {
            Log.Write("设置壁纸失败: " + ex);
            Post(new { type = "status", text = "设置壁纸失败：" + ex.Message });
        }
    }

    private void ToggleFavorite(string path)
    {
        WallpaperItem? item = WallpaperLibrary.Instance.Items
            .FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));

        if (item != null)
        {
            WallpaperLibrary.Instance.ToggleFavorite(item);
            PushState("favorite");
        }
    }

    private static void AddTrack(string playlistId, string path)
    {
        MusicPlaylist? playlist = MusicLibrary.Instance.Playlists.FirstOrDefault(p => p.Id == playlistId);
        MusicFile? file = MusicLibrary.Instance.Files
            .FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

        if (playlist != null && file != null)
        {
            MusicLibrary.Instance.AddTrack(playlist, file.Path);
        }
    }

    private static void MoveTrack(string playlistId, int from, int to)
    {
        MusicPlaylist? playlist = MusicLibrary.Instance.Playlists.FirstOrDefault(p => p.Id == playlistId);
        if (playlist != null)
        {
            MusicLibrary.Instance.MoveTrack(playlist, from, to);
        }
    }

    private static void RemoveTrack(string playlistId, string path)
    {
        MusicPlaylist? playlist = MusicLibrary.Instance.Playlists.FirstOrDefault(p => p.Id == playlistId);
        MusicTrack? track = playlist?.Tracks
            .FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));

        if (playlist != null && track != null)
        {
            MusicLibrary.Instance.RemoveTrack(playlist, track);
        }
    }

    private void PickWidgetImages(string id)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择组件图片",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            WidgetService.Instance.SetImages(id, dialog.FileNames);
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write("打开文件夹失败: " + ex.Message);
        }
    }

    private static int GetInt(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.TryGetInt32(out int result)
            ? result
            : -1;
    }

    private static string GetString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    // 关闭面板就整个销毁 WebView2，进程会完整退出，常驻内存回到托盘主体的大小。
    // 下次从托盘打开时会重新创建本窗口，界面重新加载一次。

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            WallpaperLibrary.Instance.Changed -= OnDataChanged;
            WallpaperLibrary.Instance.Status -= OnStatus;
            MusicLibrary.Instance.Changed -= OnDataChanged;
            MusicLibrary.Instance.Scanned -= OnDataChanged;
            WallpaperHost.Instance.StateChanged -= OnDataChanged;
            PreviewFrames.Ready -= OnPreviewReady;
            MusicPlayer.Instance.StateChanged -= OnPlayerChanged;
            MusicArtwork.Ready -= OnPlayerChanged;
            WidgetService.Instance.Changed -= OnWidgetsChanged;
            _playerTimer.Stop();
            _playerTimer.Dispose();
            _resizeTimer.Stop();
            _resizeTimer.Dispose();
        }

        base.Dispose(disposing);
    }
}
