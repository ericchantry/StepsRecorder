using System.Diagnostics;
using System.Drawing;

namespace OpenSteps.Capture;

public sealed record CapturableWindow(IntPtr Handle, string Title, string? ProcessName, Rectangle Bounds, bool IsMinimized);

/// <summary>
/// Lists the top-level app windows a user would recognise (the ones in the taskbar / Alt+Tab),
/// most recently used first, and resolves which of them is under a screen point.
/// </summary>
public static class WindowEnumerator
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow"
    };

    public static IReadOnlyList<CapturableWindow> GetCapturableWindows(uint? excludeProcessId = null)
    {
        var windows = new List<CapturableWindow>();
        var processNames = new Dictionary<uint, string?>();
        NativeMethods.EnumWindowsProc callback = (hwnd, _) =>
        {
            try
            {
                if (TryDescribe(hwnd, excludeProcessId, processNames) is { } window)
                {
                    windows.Add(window);
                }
            }
            catch
            {
                // Skip windows that disappear or refuse queries mid-enumeration.
            }

            return true;
        };

        NativeMethods.EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return windows;
    }

    /// <summary>True when the window still exists and is one the user could pick.</summary>
    public static bool IsCapturable(IntPtr hwnd, uint? excludeProcessId = null)
    {
        return TryDescribe(hwnd, excludeProcessId, []) is not null;
    }

    /// <summary>Current bounds of a window, or null if it is closed or minimized (nothing to show).</summary>
    public static Rectangle? GetVisibleBounds(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || NativeMethods.IsIconic(hwnd))
        {
            return null;
        }

        try
        {
            var bounds = new WindowBoundsService().GetWindowBounds(hwnd);
            return Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The top-level window that owns <paramref name="hwnd"/> (so dialogs map to their app window).</summary>
    public static IntPtr GetRootOwner(IntPtr hwnd)
    {
        var root = NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOTOWNER);
        return root == IntPtr.Zero ? hwnd : root;
    }

    /// <summary>Brings a minimized window back so it can be recorded.</summary>
    public static void RestoreIfMinimized(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && NativeMethods.IsIconic(hwnd))
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }
    }

    public static string GetLabel(CapturableWindow window, int maxLength = 52)
    {
        var title = window.Title.Trim();
        if (title.Length > maxLength)
        {
            title = title[..(maxLength - 1)].TrimEnd() + "…";
        }

        return window.IsMinimized ? $"{title} (minimized)" : title;
    }

    private static CapturableWindow? TryDescribe(IntPtr hwnd, uint? excludeProcessId, Dictionary<uint, string?> processNames)
    {
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero)
        {
            return null;
        }

        var exStyle = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return null;
        }

        if (NativeMethods.DwmGetWindowAttributeInt(hwnd, NativeMethods.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return null;
        }

        var titleLength = NativeMethods.GetWindowTextLength(hwnd);
        if (titleLength <= 0)
        {
            return null;
        }

        Span<char> classBuffer = stackalloc char[256];
        var classLength = NativeMethods.GetClassName(hwnd, classBuffer, classBuffer.Length);
        if (classLength > 0 && IgnoredClasses.Contains(new string(classBuffer[..classLength])))
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (excludeProcessId is { } excluded && processId == excluded)
        {
            return null;
        }

        var titleBuffer = new char[Math.Min(titleLength + 1, 1024)];
        var length = NativeMethods.GetWindowText(hwnd, titleBuffer, titleBuffer.Length);
        if (length <= 0)
        {
            return null;
        }

        if (!processNames.TryGetValue(processId, out var processName))
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

            processNames[processId] = processName;
        }

        var minimized = NativeMethods.IsIconic(hwnd);
        Rectangle bounds = Rectangle.Empty;
        if (!minimized && NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width <= 1 || bounds.Height <= 1)
            {
                return null;
            }
        }

        return new CapturableWindow(hwnd, new string(titleBuffer, 0, length), processName, bounds, minimized);
    }
}
