using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows.Automation;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Capture;

/// <summary>
/// Finds password fields, and fields whose names look like secrets (PIN, card number, SSN…), in the windows being
/// recorded, so their pixels can be covered before a screenshot is saved or a video frame is encoded.
///
/// Two sources are combined: the focused element (checked often, because that is where typing appears) and a
/// UI Automation search of the foreground window (and the chosen window in Single Window mode) for password and
/// edit fields. Field positions are stored relative to their window, so a window that moves keeps its cover.
/// Only positions, names and the password flag are read; field contents are never read.
/// </summary>
public sealed class SecureFieldScanner : IDisposable
{
    private const int Padding = 3;
    private static readonly TimeSpan FocusInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MinimumScanInterval = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan FreshEnough = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan KeepCachedFor = TimeSpan.FromSeconds(15);

    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private readonly ConcurrentDictionary<IntPtr, WindowFields> _windowFields = new();
    private readonly ConcurrentDictionary<IntPtr, Task<WindowFields?>> _scansInFlight = new();
    private volatile FocusedField? _focus;
    private Thread? _thread;
    private volatile bool _running;
    private TimeSpan _scanInterval = MinimumScanInterval;

    /// <summary>The window chosen in Single Window mode; scanned even when it isn't in front.</summary>
    public IntPtr ExtraWindow { get; set; }

    /// <summary>Starts watching in the background (used while a video is recording).</summary>
    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _thread = new Thread(WatchLoop) { IsBackground = true, Name = "OpenSteps secure-field watcher" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread = null;
        _focus = null;
        _windowFields.Clear();
    }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// Sensitive areas as of the last background check, moved along with their windows. Cheap: no UI Automation
    /// calls, so it can run for every video frame.
    /// </summary>
    public IReadOnlyList<ScreenBounds> GetCurrentAreas()
    {
        var areas = new List<ScreenBounds>();
        var foreground = NativeMethods.GetForegroundWindow();
        foreach (var window in new[] { foreground, ExtraWindow }.Where(w => w != IntPtr.Zero).Distinct())
        {
            if (_windowFields.TryGetValue(window, out var fields) && DateTime.UtcNow - fields.ScannedAt < KeepCachedFor)
            {
                AddTranslated(areas, window, fields.WindowRect, fields.Fields);
            }
        }

        if (_focus is { } focus)
        {
            AddTranslated(areas, focus.Window, focus.WindowRect, [focus.Field]);
        }

        return areas;
    }

    /// <summary>
    /// A fresh check for a screenshot: the focused element, plus a search of the foreground window and
    /// <paramref name="extraWindow"/>. A search that takes longer than <paramref name="timeout"/> falls back to
    /// the most recent result for that window.
    /// </summary>
    public IReadOnlyList<ScreenBounds> ScanNow(IntPtr extraWindow, TimeSpan timeout)
    {
        var areas = new List<ScreenBounds>();
        var foreground = NativeMethods.GetForegroundWindow();
        if (CheckFocus(foreground) is { } focus)
        {
            AddTranslated(areas, focus.Window, focus.WindowRect, [focus.Field]);
        }

        foreach (var window in new[] { foreground, extraWindow, ExtraWindow }.Where(w => w != IntPtr.Zero).Distinct())
        {
            if (!IsScannable(window))
            {
                continue;
            }

            WindowFields? fields = null;
            if (_windowFields.TryGetValue(window, out var cached) && DateTime.UtcNow - cached.ScannedAt < FreshEnough)
            {
                fields = cached;
            }
            else
            {
                var scan = _scansInFlight.GetOrAdd(window, w => Task.Run(() => ScanAndStore(w)));
                try
                {
                    fields = scan.Wait(timeout) ? scan.Result : null;
                }
                catch (AggregateException)
                {
                    fields = null;
                }

                if (fields is null && _windowFields.TryGetValue(window, out var older) && DateTime.UtcNow - older.ScannedAt < KeepCachedFor)
                {
                    fields = older;
                }
            }

            if (fields is not null)
            {
                AddTranslated(areas, window, fields.WindowRect, fields.Fields);
            }
        }

        return areas;
    }

    private void WatchLoop()
    {
        var lastScan = new Dictionary<IntPtr, DateTime>();
        while (_running)
        {
            try
            {
                var foreground = NativeMethods.GetForegroundWindow();
                _focus = CheckFocus(foreground);

                foreach (var window in new[] { foreground, ExtraWindow }.Where(w => w != IntPtr.Zero).Distinct())
                {
                    if (!_running || !IsScannable(window))
                    {
                        continue;
                    }

                    if (!lastScan.TryGetValue(window, out var at) || DateTime.UtcNow - at >= _scanInterval)
                    {
                        var stopwatch = Stopwatch.StartNew();
                        ScanAndStore(window);
                        lastScan[window] = DateTime.UtcNow;

                        // Back off in apps with very large UI trees so the watcher never becomes a burden.
                        var spent = stopwatch.Elapsed;
                        _scanInterval = spent * 4 > MinimumScanInterval ? spent * 4 : MinimumScanInterval;
                    }
                }
            }
            catch
            {
                // UI Automation can fail for individual apps; keep watching.
            }

            Thread.Sleep(FocusInterval);
        }
    }

    private WindowFields? ScanAndStore(IntPtr window)
    {
        try
        {
            var fields = ScanWindow(window);
            if (fields is not null)
            {
                _windowFields[window] = fields;
            }

            return fields;
        }
        finally
        {
            _scansInFlight.TryRemove(window, out _);
        }
    }

    private static WindowFields? ScanWindow(IntPtr window)
    {
        if (!TryGetWindowRect(window, out var windowRect))
        {
            return null;
        }

        try
        {
            var root = AutomationElement.FromHandle(window);
            var cache = new CacheRequest { AutomationElementMode = AutomationElementMode.None, TreeScope = TreeScope.Element };
            cache.Add(AutomationElement.BoundingRectangleProperty);
            cache.Add(AutomationElement.IsPasswordProperty);
            cache.Add(AutomationElement.NameProperty);
            cache.Add(AutomationElement.AutomationIdProperty);
            cache.Add(AutomationElement.IsOffscreenProperty);

            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.IsPasswordProperty, true),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

            var fields = new List<ScreenBounds>();
            AutomationElementCollection found;
            using (cache.Activate())
            {
                found = root.FindAll(TreeScope.Descendants, condition);
            }

            foreach (AutomationElement element in found)
            {
                var info = element.Cached;
                if (info.IsOffscreen || !SensitiveInputDetector.IsSensitive(info.IsPassword, info.Name, info.AutomationId))
                {
                    continue;
                }

                if (ToBounds(info.BoundingRectangle) is { } bounds)
                {
                    fields.Add(bounds);
                }
            }

            return new WindowFields(windowRect, fields, DateTime.UtcNow);
        }
        catch
        {
            // ElementNotAvailable, timeouts, apps without UI Automation support.
            return null;
        }
    }

    private FocusedField? CheckFocus(IntPtr foreground)
    {
        if (!IsScannable(foreground) || !TryGetWindowRect(foreground, out var windowRect))
        {
            return null;
        }

        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null)
            {
                return null;
            }

            var current = element.Current;
            if (!SensitiveInputDetector.IsSensitive(current.IsPassword, current.Name, current.AutomationId))
            {
                return null;
            }

            return ToBounds(current.BoundingRectangle) is { } bounds ? new FocusedField(foreground, windowRect, bounds) : null;
        }
        catch
        {
            return null;
        }
    }

    private bool IsScannable(IntPtr window)
    {
        if (window == IntPtr.Zero || !NativeMethods.IsWindow(window) || NativeMethods.IsIconic(window))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return processId != _ownProcessId;
    }

    /// <summary>Adds fields recorded when <paramref name="windowRectAtScan"/> was the window's position, shifted to where it is now.</summary>
    private static void AddTranslated(List<ScreenBounds> areas, IntPtr window, ScreenBounds windowRectAtScan, IReadOnlyList<ScreenBounds> fields)
    {
        if (fields.Count == 0 || NativeMethods.IsIconic(window) || !TryGetWindowRect(window, out var now))
        {
            return;
        }

        var dx = now.X - windowRectAtScan.X;
        var dy = now.Y - windowRectAtScan.Y;
        foreach (var field in fields)
        {
            areas.Add(new ScreenBounds(field.X + dx - Padding, field.Y + dy - Padding, field.Width + Padding * 2, field.Height + Padding * 2));
        }
    }

    private static bool TryGetWindowRect(IntPtr window, out ScreenBounds bounds)
    {
        bounds = default;
        if (window == IntPtr.Zero || !NativeMethods.GetWindowRect(window, out var rect))
        {
            return false;
        }

        bounds = new ScreenBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static ScreenBounds? ToBounds(System.Windows.Rect rect)
    {
        if (rect.IsEmpty || double.IsInfinity(rect.Width) || double.IsInfinity(rect.Height) || rect.Width < 2 || rect.Height < 2)
        {
            return null;
        }

        return new ScreenBounds((int)Math.Floor(rect.X), (int)Math.Floor(rect.Y), (int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height));
    }

    private sealed record WindowFields(ScreenBounds WindowRect, IReadOnlyList<ScreenBounds> Fields, DateTime ScannedAt);

    private sealed record FocusedField(IntPtr Window, ScreenBounds WindowRect, ScreenBounds Field);
}
