# MINWallPaper

一个轻量的 Windows 桌面工具：视频壁纸、唱片机音乐库和可编辑桌面组件，全部离线运行。

单实例运行：重复打开只会唤出已运行的主面板，不会再开一个进程。

界面用 WebView2 渲染，宿主是 .NET 8 WinForms。壁纸、音乐、播放列表、封面缓存都放在程序目录的 `data` 文件夹里，不上传、不联网。

## 截图

![壁纸库](docs/screenshots/hero-library.png)

![音乐库](docs/screenshots/music.png)

![沉浸模式](docs/screenshots/immersive.png)

![桌面工具](docs/screenshots/tools.png)

## 功能

**视频壁纸**
- Media Foundation 硬解，铺在桌面图标下方（WorkerW / Progman 双路径兼容）
- 支持 mp4 / mkv / mov / avi / wmv 等系统能解码的格式
- 播放卡住时自动重启；视频窗口丢失时自动重建
- 视频层压在所有窗口最底部，不受 Windows 桌面快照影响，也不改动系统壁纸
- **播放视频时桌面图标会被视频画面盖住**（图标本身仍可点击）
- 图片壁纸交给系统壁纸接口，常驻内存接近零

**壁纸配乐**
- 每张壁纸可以绑定一个播放列表或单曲，切壁纸自动切歌
- 列表循环、单曲循环、随机播放
- 曲目搜索、音量同步，使用 NAudio 播放

**沉浸音乐库**
- 黑胶唱片、专辑封面、同名 `.lrc` 歌词逐行高亮
- 一键切换沉浸页：斜线切开当前页面，两片向左右推开
- 浅蓝 + 芥末黄配色，底部白蓝黄斜切播放条

**桌面组件**
- 时钟 10 种样式：玻璃水滴、翻页、指针、霓虹、极简、弧形、胶囊、点阵 LED、海报、描边
- 音浪 10 种样式：柱形、线波、液态玻璃、环形、点阵、极光、扫描、水墨、分段、双面镜像
- 文本 4 种样式，独立文字组件
- 系统监控：CPU（3 种样式）、内存（2 种）、GPU（3 种）、磁盘（2 种），读取真实本机数据
- 图片：单图、三图拼贴、轮播，支持本地选图
- 落雨：全屏细雨，鼠标穿透，不挡桌面图标
- 所有组件支持多实例、实时预览、拖动移动、拖边框缩放；配置保存在 `data/widgets.json`

## 下载

下载 `MINWallPaper-1.1.1-setup.exe`，双击安装即可，安装到当前用户目录，不需要管理员权限。

- 个人主页：<https://codecatio.github.io/home/#/tools/minwallpaper>
- GitHub Releases：<https://github.com/CodeCatIO/MINWallPaper/releases>
- Gitee Releases：<https://gitee.com/codecatio/minwallpaper/releases>

## 运行要求

- Windows 10 / 11 64 位
- .NET 8 Desktop Runtime
- WebView2 Runtime（Win11 自带，Win10 首次打开会提示安装）

## 从源码构建

```powershell
dotnet build -c Release
```

发布为不包含运行时的文件：

```powershell
dotnet publish -c Release --self-contained false -o publish
```

生成安装包：用 Inno Setup 6 打开并编译 [installer/MINWallPaper.iss](installer/MINWallPaper.iss)，输出在 `dist/MINWallPaper-<版本>-setup.exe`。

运行后会在 exe 旁边生成 `data` 目录：

```
data/
  wallpapers/     壁纸文件
  music/files/    音乐文件
  ...             播放列表、壁纸绑定、封面缓存
```

换电脑时直接拷贝整个 `data` 文件夹即可。

## 项目结构

```
Assets/       应用图标
installer/    Inno Setup 安装包脚本
Music/        音乐库、播放器、播放列表、封面提取、歌词
Native/       Win32 窗口样式、DWM 等原生调用
Pages/        WebView2 与前端之间的消息通道
Playback/     视频壁纸播放与看门狗
Storage/      壁纸索引、设置、音乐库索引
Web/          前端界面（单文件 index.html + 资源）
Widgets/      桌面小组件（时钟、音浪、落雨）
WallpaperHost.cs   壁纸宿主与视频层管理
WebLibraryForm.cs  主面板窗口
TrayContext.cs     托盘与启动逻辑
```

## 说明

- 仓库不包含任何壁纸、音乐或个人数据，`data/` 已在 `.gitignore` 中忽略
- 视频壁纸音轨强制静音，声音只来自音乐库
- 崩溃日志写在 `data` 目录下，方便排查

## 路线图

见 [ROADMAP.md](ROADMAP.md)。
