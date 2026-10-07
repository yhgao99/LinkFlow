using System;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LinkFlow.Services;

public static class IconExtractor
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return Cache.GetOrAdd(path, LoadIconUncached);
    }

    private static ImageSource? LoadIconUncached(string filePath)
    {
        try
        {
            // Clean up quoted paths
            var cleanPath = filePath.Trim('"', '\'').Trim();
            if (!File.Exists(cleanPath))
                return null;

            var shinfo = new NativeMethods.SHFILEINFO();
            var res = NativeMethods.SHGetFileInfo(
                cleanPath,
                0,
                ref shinfo,
                (uint)System.Runtime.InteropServices.Marshal.SizeOf(shinfo),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON
            );

            if (res == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                return null;

            try
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions()
                );

                if (bitmapSource.CanFreeze)
                    bitmapSource.Freeze();

                return bitmapSource;
            }
            finally
            {
                NativeMethods.DestroyIcon(shinfo.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }
}
