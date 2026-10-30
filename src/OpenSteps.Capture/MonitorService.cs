using System.Runtime.InteropServices;

namespace OpenSteps.Capture;

public sealed class MonitorService
{
    public DisplayMonitorInfo GetMonitorFromPoint(int screenX, int screenY)
    {
        var point = new NativeMethods.POINT { X = screenX, Y = screenY };
        var handle = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to find a monitor for the screen point.");
        }

        var all = GetAllMonitors();
        return all.FirstOrDefault(monitor => monitor.MonitorHandle == handle)
            ?? ReadMonitor(handle, all.Count + 1);
    }

    /// <summary>The monitor with this Windows device name (\\.\DISPLAYn), or the primary monitor if it is no longer connected.</summary>
    public DisplayMonitorInfo FindMonitor(string? deviceName)
    {
        var all = GetAllMonitors();
        return all.FirstOrDefault(monitor => string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(monitor => monitor.IsPrimary)
            ?? all.First();
    }

    /// <summary>The monitor that most of a window is on.</summary>
    public DisplayMonitorInfo? GetMonitorForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        var handle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return handle == IntPtr.Zero ? null : GetAllMonitors().FirstOrDefault(monitor => monitor.MonitorHandle == handle);
    }

    /// <summary>Bounds of the monitor most of a window is on, without enumerating every monitor (cheap enough per video frame).</summary>
    public static System.Drawing.Rectangle? GetMonitorBoundsForWindow(IntPtr hwnd)
    {
        var handle = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var info = new NativeMethods.MONITORINFOEX
        {
            Size = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            DeviceName = string.Empty
        };

        return NativeMethods.GetMonitorInfo(handle, ref info)
            ? System.Drawing.Rectangle.FromLTRB(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom)
            : null;
    }

    public IReadOnlyList<DisplayMonitorInfo> GetAllMonitors()
    {
        var monitors = new List<DisplayMonitorInfo>();
        NativeMethods.MonitorEnumProc callback = EnumerateMonitor;

        bool EnumerateMonitor(IntPtr handle, IntPtr hdcMonitor, ref NativeMethods.RECT monitorRect, IntPtr data)
        {
            monitors.Add(ReadMonitor(handle, monitors.Count + 1));
            return true;
        }

        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            throw new InvalidOperationException("Unable to enumerate display monitors.");
        }

        return monitors;
    }

    private static DisplayMonitorInfo ReadMonitor(IntPtr handle, int index)
    {
        var info = new NativeMethods.MONITORINFOEX
        {
            Size = Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            DeviceName = string.Empty
        };

        if (!NativeMethods.GetMonitorInfo(handle, ref info))
        {
            throw new InvalidOperationException("Unable to read monitor information.");
        }

        uint? dpiX = null;
        uint? dpiY = null;
        try
        {
            if (NativeMethods.GetDpiForMonitor(handle, NativeMethods.MDT_EFFECTIVE_DPI, out var x, out var y) == 0)
            {
                dpiX = x;
                dpiY = y;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return new DisplayMonitorInfo(
            handle,
            string.IsNullOrWhiteSpace(info.DeviceName) ? null : info.DeviceName,
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right,
            info.Monitor.Bottom,
            info.Work.Left,
            info.Work.Top,
            info.Work.Right,
            info.Work.Bottom,
            (info.Flags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
            index,
            dpiX,
            dpiY);
    }
}
