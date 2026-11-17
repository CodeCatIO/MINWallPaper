namespace MINWallPaper.Storage;

internal enum WallpaperKind
{
    Video,
    Image
}

/// <summary>壁纸库里的一条记录。</summary>
internal sealed class WallpaperItem
{
    public string Path { get; set; } = "";

    public string Name { get; set; } = "";

    public WallpaperKind Kind { get; set; }

    public long SizeBytes { get; set; }

    /// <summary>源文件最后修改时间，用来判断缩略图和索引是否过期。</summary>
    public long ModifiedTicks { get; set; }

    public string? ThumbnailPath { get; set; }

    public bool Favorite { get; set; }

    /// <summary>文件缺失时标记一下，界面上灰掉而不是直接删除。</summary>
    public bool Missing { get; set; }
}

/// <summary>整个壁纸库的索引文件结构。</summary>
internal sealed class LibraryIndex
{
    public string Root { get; set; } = "";

    public List<WallpaperItem> Items { get; set; } = new();
}
