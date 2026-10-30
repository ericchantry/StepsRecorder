using System.Diagnostics;
using OpenSteps.Core.Models;

namespace OpenSteps.Capture;

public sealed class ActiveWindowService
{
    private readonly Dictionary<uint, string?> _processNameCache = [];

    public ActiveWindowInfo Capture()
    {
        var handle = NativeMethods.GetForegroundWindow();
        return Capture(handle);
    }

    public IntPtr GetForegroundWindowHandle()
    {
        return NativeMethods.GetForegroundWindow();
    }

    public ActiveWindowInfo Capture(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return new ActiveWindowInfo(IntPtr.Zero, null, null, null, null);
        }

        var title = GetTitle(handle);
        string? processName = null;
        string? executablePath = null;

        try
        {
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;

            try
            {
                executablePath = process.MainModule?.FileName;
            }
            catch
            {
                executablePath = null;
            }
        }
        catch
        {
            processName = null;
        }

        ScreenBounds? bounds = null;
        if (NativeMethods.GetWindowRect(handle, out var rect))
        {
            bounds = new ScreenBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        return new ActiveWindowInfo(handle, title, processName, executablePath, bounds);
    }

    /// <summary>Cheap title + process name lookup for timeline events (process names are cached per PID).</summary>
    public (string? Title, string? ProcessName) DescribeWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return (null, null);
        }

        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (!_processNameCache.TryGetValue(processId, out var processName))
        {
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch
            {
                processName = null;
            }

            if (_processNameCache.Count > 500)
            {
                _processNameCache.Clear();
            }

            _processNameCache[processId] = processName;
        }

        return (GetTitle(handle), processName);
    }

    /// <summary>Top-level window under a screen point.</summary>
    public IntPtr GetRootWindowAt(int x, int y)
    {
        var hit = NativeMethods.WindowFromPoint(new NativeMethods.POINT { X = x, Y = y });
        if (hit == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var root = NativeMethods.GetAncestor(hit, NativeMethods.GA_ROOT);
        return root == IntPtr.Zero ? hit : root;
    }

    private static string? GetTitle(IntPtr handle)
    {
        Span<char> buffer = stackalloc char[512];
        var length = NativeMethods.GetWindowText(handle, buffer, buffer.Length);
        return length <= 0 ? null : new string(buffer[..length]);
    }
}
