using System;
using System.Runtime.InteropServices;

namespace LinkFlow.Services;

public static class NativeMethods
{
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

    public const int DWMWCP_ROUND = 2; // 16px Win11 rounded corners
    public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic blur
    public const int DWMSBT_MAINWINDOW = 2; // Mica

    [DllImport("dwmapi.dll", PreserveSig = true)]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_LARGEICON = 0x000000000;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr ShellExecute(IntPtr hwnd, string lpOperation, string lpFile, string lpParameters, string lpDirectory, int nShowCmd);

    public const int WM_SETICON = 0x0080;
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;

    public const uint IMAGE_ICON = 1;
    public const uint LR_LOADFROMFILE = 0x0010;
    public const int SM_CXICON = 11;
    public const int SM_CYICON = 12;
    public const int SM_CXSMICON = 49;
    public const int SM_CYSMICON = 50;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("shell32.dll", SetLastError = true)]
    public static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public static void ApplyWindowStylesAndIcon(System.Windows.Window window)
    {
        try
        {
            var iconUri = new Uri("pack://application:,,,/app_icon.png");
            window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
        }
        catch { }

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int cornerPref = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));
        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        try
        {
            string? icoPath = null;
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidate1 = System.IO.Path.Combine(baseDir, "app_icon.ico");
            var candidate2 = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LinkFlow",
                "app_icon.ico");
            var candidate3 = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FluentPicker",
                "app_icon.ico");

            if (System.IO.File.Exists(candidate1))
                icoPath = candidate1;
            else if (System.IO.File.Exists(candidate2))
                icoPath = candidate2;
            else if (System.IO.File.Exists(candidate3))
                icoPath = candidate3;

            if (!string.IsNullOrEmpty(icoPath))
            {
                int cxSm = GetSystemMetrics(SM_CXSMICON);
                int cySm = GetSystemMetrics(SM_CYSMICON);
                int cxLg = GetSystemMetrics(SM_CXICON);
                int cyLg = GetSystemMetrics(SM_CYICON);

                if (cxSm <= 0) cxSm = 16;
                if (cySm <= 0) cySm = 16;
                if (cxLg <= 0) cxLg = 32;
                if (cyLg <= 0) cyLg = 32;

                var hSmall = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, cxSm, cySm, LR_LOADFROMFILE);
                var hBig = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, cxLg, cyLg, LR_LOADFROMFILE);

                if (hSmall != IntPtr.Zero)
                    SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, hSmall);
                if (hBig != IntPtr.Zero)
                    SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, hBig);
            }
            else
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                {
                    var ico = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (ico != null)
                    {
                        SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, ico.Handle);
                        SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, ico.Handle);
                    }
                }
            }
        }
        catch { }
    }
}
