namespace MINWallPaper;

/// <summary>图片壁纸的填充方式，对应 Windows 桌面壁纸的几种排布。</summary>
internal enum ImageFillMode
{
    /// <summary>等比完整显示，多余部分留边。</summary>
    Fit,

    /// <summary>等比铺满，超出部分裁掉。</summary>
    Fill,

    /// <summary>拉伸铺满，不保持比例。</summary>
    Stretch,

    /// <summary>按原尺寸平铺。</summary>
    Tile
}
