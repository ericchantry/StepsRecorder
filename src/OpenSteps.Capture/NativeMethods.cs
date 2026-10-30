using System.Runtime.InteropServices;

namespace OpenSteps.Capture;

internal static partial class NativeMethods
{
    internal const int WH_MOUSE_LL = 14;
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_LBUTTONDOWN = 0x0201;
    internal const int WM_LBUTTONUP = 0x0202;
    internal const int WM_RBUTTONDOWN = 0x0204;
    internal const int WM_MBUTTONDOWN = 0x0207;
    internal const int WM_MOUSEWHEEL = 0x020A;
    internal const int WM_MOUSEHWHEEL = 0x020E;
    internal const uint LLKHF_INJECTED = 0x10;
    internal const uint CURSOR_SHOWING = 0x00000001;
    internal const int DI_NORMAL = 0x0003;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int SM_CXDOUBLECLK = 36;
    internal const int SM_CYDOUBLECLK = 37;
    internal const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;
    internal const int MDT_EFFECTIVE_DPI = 0;
    internal const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const uint GA_ROOT = 2;

    internal delegate IntPtr LowLevelHookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial IntPtr WindowFromPoint(POINT point);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetParent(IntPtr hwnd);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetClassName(IntPtr hWnd, Span<char> className, int maxCount);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int GetWindowText(IntPtr hWnd, Span<char> text, int count);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW")]
    internal static partial IntPtr SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDoubleClickTime();

    [LibraryImport("user32.dll")]
    internal static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetThreadDpiAwarenessContext();

    [LibraryImport("user32.dll")]
    internal static partial DPI_AWARENESS GetAwarenessFromDpiAwarenessContext(IntPtr value);

    [LibraryImport("user32.dll")]
    internal static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll")]
    internal static partial short GetKeyState(int virtualKey);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetKeyboardLayout(uint threadId);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int ToUnicodeEx(uint virtualKey, uint scanCode, ReadOnlySpan<byte> keyState, Span<char> buffer, int bufferSize, uint flags, IntPtr keyboardLayout);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorInfo(ref CURSORINFO cursorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetIconInfo(IntPtr icon, out ICONINFO iconInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DrawIconEx(IntPtr hdc, int left, int top, IntPtr icon, int width, int height, int step, IntPtr brush, int flags);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr handle);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll")]
    internal static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitorHandle, ref MONITORINFOEX monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc callback, IntPtr data);

    [DllImport("Shcore.dll", SetLastError = true)]
    internal static extern int GetDpiForMonitor(IntPtr monitorHandle, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    internal delegate bool MonitorEnumProc(IntPtr monitorHandle, IntPtr hdcMonitor, ref RECT monitorRect, IntPtr data);

    internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    internal enum DPI_AWARENESS
    {
        DPI_AWARENESS_INVALID = -1,
        DPI_AWARENESS_UNAWARE = 0,
        DPI_AWARENESS_SYSTEM_AWARE = 1,
        DPI_AWARENESS_PER_MONITOR_AWARE = 2
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KBDLLHOOKSTRUCT
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CURSORINFO
    {
        public int Size;
        public uint Flags;
        public IntPtr Cursor;
        public POINT ScreenPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool IsIcon;
        public int HotspotX;
        public int HotspotY;
        public IntPtr MaskBitmap;
        public IntPtr ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MONITORINFOEX
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }
}
