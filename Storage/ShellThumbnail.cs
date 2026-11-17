using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MINWallPaper.Storage;

/// <summary>
/// 借助 Windows 自带的缩略图服务取封面，视频会取首帧。
/// 比自己去解一帧省事，也能复用系统缓存。
/// </summary>
internal static class ShellThumbnail
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;

        public NativeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [Flags]
    private enum SIIGBF
    {
        ResizeToFit = 0x00,
        BiggerSizeOk = 0x01,
        MemoryOnly = 0x02,
        IconOnly = 0x04,
        ThumbnailOnly = 0x08,
        InCacheOnly = 0x10
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>把缩略图保存成 jpg，成功返回 true。</summary>
    public static bool Save(string sourcePath, string targetPath, int width, int height,
        bool allowIconFallback = true)
    {
        IntPtr hbitmap = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(sourcePath, IntPtr.Zero,
                typeof(IShellItemImageFactory).GUID, out object item);

            var factory = (IShellItemImageFactory)item;
            var flags = SIIGBF.ResizeToFit | SIIGBF.BiggerSizeOk | SIIGBF.ThumbnailOnly;

            int hr = factory.GetImage(new NativeSize(width, height), flags, out hbitmap);
            if (hr != 0 || hbitmap == IntPtr.Zero)
            {
                if (!allowIconFallback)
                {
                    return false;
                }

                // 有些格式没有缩略图提供者，退一步取文件图标。
                hr = factory.GetImage(new NativeSize(width, height), SIIGBF.IconOnly, out hbitmap);
                if (hr != 0 || hbitmap == IntPtr.Zero)
                {
                    return false;
                }
            }

            using var bitmap = Image.FromHbitmap(hbitmap);
            bitmap.Save(targetPath, ImageFormat.Jpeg);
            return true;
        }
        catch (Exception ex)
        {
            Log.Write("缩略图失败 " + sourcePath + " : " + ex.Message);
            return false;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero)
            {
                DeleteObject(hbitmap);
            }
        }
    }
}
