using System.ComponentModel;
using OpenSteps.Capture;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.App;

/// <summary>
/// The "Capture" choice on the recorder bar: all monitors, one monitor, the active window, one chosen window,
/// or a region drawn on screen. It decides what screenshots and the video cover, and in single-monitor and
/// region modes it also ignores clicks and typing outside that area.
/// </summary>
public partial class MainWindow
{
    private const string WindowOptionPrefix = "Window · ";
    private IntPtr _captureWindowHandle;
    private bool _captureWindowLost;
    private bool _pickingWindow;
    private ScreenshotModeOption? _optionBeforePick;
    private string? _captureMonitorDeviceName;
    private System.Drawing.Rectangle? _captureMonitorBounds;
    private System.Windows.Forms.Form? _regionOutline;
    private ScreenshotModeOption? _selectedCaptureOption;
    private bool _refreshingCaptureOptions;

    public ScreenshotModeOption? SelectedCaptureOption
    {
        get => _selectedCaptureOption;
        set
        {
            if (value is null || _refreshingCaptureOptions || Equals(_selectedCaptureOption, value))
            {
                return;
            }

            if (value.IsPicker)
            {
                // "Pick a window…" is an action, not a choice. The box shows it while picking (the capture mode
                // itself does not change), and the previous choice is put back if picking is cancelled.
                if (!_pickingWindow && !_isRecording)
                {
                    _optionBeforePick = _selectedCaptureOption;
                    _selectedCaptureOption = value;
                    _ = value.Mode == ScreenshotMode.Region ? PickRegionAsync() : PickWindowAsync();
                }

                return;
            }

            _selectedCaptureOption = value;

            // Set again from the new choice when the next recording segment starts.
            _captureMonitorBounds = null;
            if (value.Mode == ScreenshotMode.Window)
            {
                _captureWindowHandle = value.WindowHandle;
                _settings.CaptureWindowTitle = value.WindowTitle;
                _settings.CaptureWindowProcessName = value.WindowProcessName;
            }

            if (value.Mode == ScreenshotMode.Region && value.Region is { } region)
            {
                _settings.CaptureRegion = region;
            }

            _screenshotService.SelectedWindowHandle = value.Mode == ScreenshotMode.Window ? value.WindowHandle : IntPtr.Zero;
            _screenshotService.SelectedRegion = value.Mode == ScreenshotMode.Region && value.Region is { } selectedRegion ? ToRectangle(selectedRegion) : null;
            _captureMonitorDeviceName = value.MonitorDeviceName;
            _settings.CaptureMonitorDeviceName = value.MonitorDeviceName;
            _screenshotService.SelectedMonitorDeviceName = value.MonitorDeviceName;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCaptureOption)));

            var modeChanged = ScreenshotMode != value.Mode;
            ScreenshotMode = value.Mode;
            if (!modeChanged)
            {
                // Same mode, different monitor or window: the setter above did not save or refresh.
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScreenshotModeDisplayText)));
                RefreshRecordingStatus();
                if (!_loadingSettings)
                {
                    _ = SaveSettingsAsync();
                }
            }
        }
    }

    /// <summary>Rebuilds the capture choices from the connected monitors, keeping the current choice when it still exists.</summary>
    private void RefreshCaptureOptions()
    {
        IReadOnlyList<DisplayMonitorInfo> monitors;
        try
        {
            monitors = SortMonitors(_monitorService.GetAllMonitors());
        }
        catch
        {
            monitors = [];
        }

        var options = new List<ScreenshotModeOption> { new("All monitors", ScreenshotMode.FullDesktop) };
        if (monitors.Count > 1)
        {
            for (var i = 0; i < monitors.Count; i++)
            {
                options.Add(new ScreenshotModeOption(GetMonitorLabel(monitors, i), ScreenshotMode.Monitor, monitors[i].DeviceName));
            }
        }

        options.Add(new ScreenshotModeOption("Active window", ScreenshotMode.ActiveWindow));

        var savedRegion = GetSavedRegion();
        if (savedRegion is { } region)
        {
            options.Add(new ScreenshotModeOption(ScreenshotModeLabels.GetRegionLabel(region.Width, region.Height), ScreenshotMode.Region, Region: region));
        }

        options.Add(new ScreenshotModeOption(savedRegion is null ? "Select a region…" : "Select a new region…", ScreenshotMode.Region, IsPicker: true));

        IReadOnlyList<CapturableWindow> windows;
        try
        {
            windows = WindowEnumerator.GetCapturableWindows((uint)Environment.ProcessId);
        }
        catch
        {
            windows = [];
        }

        foreach (var window in windows)
        {
            options.Add(CreateWindowOption(window));
        }

        options.Add(new ScreenshotModeOption("Pick a window by clicking it…", ScreenshotMode.Window, IsPicker: true));

        _captureWindowLost = false;
        ScreenshotModeOption? wanted;
        if (ScreenshotMode == ScreenshotMode.Monitor)
        {
            wanted = options.FirstOrDefault(o => o.Mode == ScreenshotMode.Monitor && string.Equals(o.MonitorDeviceName, _captureMonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                ?? options.FirstOrDefault(o => o.Mode == ScreenshotMode.Monitor && monitors.FirstOrDefault(m => m.IsPrimary)?.DeviceName == o.MonitorDeviceName);
        }
        else if (ScreenshotMode == ScreenshotMode.Window)
        {
            wanted = FindWindowOption(options, windows);
            _captureWindowLost = wanted is null;
        }
        else
        {
            wanted = options.FirstOrDefault(o => o.Mode == ScreenshotMode && !o.IsPicker);
        }

        wanted ??= options[0];

        _refreshingCaptureOptions = true;
        try
        {
            ScreenshotModeOptions.Clear();
            foreach (var option in options)
            {
                ScreenshotModeOptions.Add(option);
            }
        }
        finally
        {
            _refreshingCaptureOptions = false;
        }

        _selectedCaptureOption = null;
        SelectedCaptureOption = wanted;
    }

    private void CaptureModeCombo_DropDownOpened(object? sender, EventArgs e)
    {
        if (!_isRecording)
        {
            RefreshCaptureOptions();
        }
    }

    private string GetCaptureAreaLabel()
    {
        return ScreenshotMode switch
        {
            ScreenshotMode.Monitor when _selectedCaptureOption?.Mode == ScreenshotMode.Monitor => _selectedCaptureOption.DisplayName.Split(" · ")[0],
            ScreenshotMode.Window => "Window",
            ScreenshotMode.Region => "Region",
            _ => ScreenshotModeLabels.GetDisplayText(ScreenshotMode)
        };
    }

    /// <summary>Puts the choice from before "Pick a window…" back in the Capture box.</summary>
    private void RestoreCaptureComboSelection()
    {
        if (_selectedCaptureOption?.IsPicker == true)
        {
            _selectedCaptureOption = _optionBeforePick ?? ScreenshotModeOptions.FirstOrDefault();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCaptureOption)));
        }

        _optionBeforePick = null;
    }

    private static ScreenshotModeOption CreateWindowOption(CapturableWindow window)
    {
        return new ScreenshotModeOption(
            WindowOptionPrefix + WindowEnumerator.GetLabel(window),
            ScreenshotMode.Window,
            WindowHandle: window.Handle,
            WindowTitle: window.Title,
            WindowProcessName: window.ProcessName);
    }

    /// <summary>
    /// The chosen window: the same window if it is still open, otherwise (after a restart) a window with the
    /// saved title, or the only window of the saved app. Null when it can't be found.
    /// </summary>
    private ScreenshotModeOption? FindWindowOption(List<ScreenshotModeOption> options, IReadOnlyList<CapturableWindow> windows)
    {
        var windowOptions = options.Where(o => o.Mode == ScreenshotMode.Window && !o.IsPicker).ToList();
        if (_captureWindowHandle != IntPtr.Zero && windowOptions.FirstOrDefault(o => o.WindowHandle == _captureWindowHandle) is { } same)
        {
            return same;
        }

        var title = _settings.CaptureWindowTitle;
        var process = _settings.CaptureWindowProcessName;
        if (!string.IsNullOrWhiteSpace(title) && windowOptions.FirstOrDefault(o => o.WindowTitle == title) is { } byTitle)
        {
            return byTitle;
        }

        var sameApp = windowOptions.Where(o => !string.IsNullOrWhiteSpace(process) && string.Equals(o.WindowProcessName, process, StringComparison.OrdinalIgnoreCase)).ToList();
        return sameApp.Count == 1 ? sameApp[0] : null;
    }

    /// <summary>Asks the user to click the window to record, then selects it and outlines it briefly.</summary>
    private async Task PickWindowAsync()
    {
        _pickingWindow = true;
        var picked = IntPtr.Zero;
        var prompt = new WindowPickPrompt { Owner = this };
        var promptClosed = false;
        prompt.Closed += (_, _) => promptClosed = true;
        try
        {
            prompt.Show();
            var ownProcessId = (uint)Environment.ProcessId;
            var deadline = DateTime.UtcNow.AddSeconds(60);

            // Only a window the user activates after picking starts counts, so whatever happened to be in
            // front already is never picked by accident.
            var startingForeground = WindowEnumerator.GetRootOwner(_activeWindowService.GetForegroundWindowHandle());
            while (!prompt.IsCancelled && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
                var foreground = WindowEnumerator.GetRootOwner(_activeWindowService.GetForegroundWindowHandle());
                if (foreground != IntPtr.Zero && foreground != startingForeground && WindowEnumerator.IsCapturable(foreground, ownProcessId))
                {
                    picked = foreground;
                    break;
                }

                if (foreground != startingForeground && foreground != IntPtr.Zero)
                {
                    // Focus moved somewhere that can't be recorded (e.g. the desktop); wait for the next window.
                    startingForeground = foreground;
                }
            }

            if (picked == IntPtr.Zero)
            {
                return;
            }

            ScreenshotMode = ScreenshotMode.Window;
            _captureWindowHandle = picked;
            _settings.CaptureWindowTitle = null;
            _settings.CaptureWindowProcessName = null;
            RefreshCaptureOptions();
            if (WindowEnumerator.GetVisibleBounds(picked) is { } bounds)
            {
                WindowHighlight.Flash(bounds);
            }
        }
        finally
        {
            _pickingWindow = false;
            if (!promptClosed)
            {
                prompt.Close();
            }

            if (picked == IntPtr.Zero)
            {
                RestoreCaptureComboSelection();
            }
        }
    }

    /// <summary>Lets the user draw the area to record, then selects it and outlines it briefly.</summary>
    private async Task PickRegionAsync()
    {
        _pickingWindow = true;
        ScreenBounds? picked = null;
        var wasVisible = IsVisible;
        try
        {
            // Get the recorder bar out of the way so the area under it can be selected.
            if (wasVisible)
            {
                Hide();
                await Task.Delay(200);
            }

            picked = RegionSelector.Select(GetSavedRegion());
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"The region could not be selected:\n{ex.Message}", "OpenSteps", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            _pickingWindow = false;
            if (wasVisible)
            {
                Show();
                Activate();
            }
        }

        if (picked is not { } region)
        {
            RestoreCaptureComboSelection();
            return;
        }

        _settings.CaptureRegion = region;
        ScreenshotMode = ScreenshotMode.Region;
        RefreshCaptureOptions();
        WindowHighlight.Flash(ToRectangle(region));
    }

    /// <summary>The saved region, trimmed to the monitors connected now; null if there is none or it is off screen.</summary>
    private ScreenBounds? GetSavedRegion()
    {
        if (_settings.CaptureRegion is not { } region)
        {
            return null;
        }

        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        return CaptureRegionMath.ClampToScreen(region, new ScreenBounds(screen.X, screen.Y, screen.Width, screen.Height));
    }

    /// <summary>
    /// Before recording in region mode: make sure the region is still on screen. Returns false (after telling the
    /// user) when it isn't, e.g. because the monitor it was on has been disconnected.
    /// </summary>
    private bool PrepareCaptureRegion()
    {
        if (_screenshotCaptureTargetStep is not null || ScreenshotMode != ScreenshotMode.Region)
        {
            return true;
        }

        if (GetSavedRegion() is not { } region)
        {
            System.Windows.MessageBox.Show(
                this,
                "The screen region chosen for recording is no longer on screen.\n\nUse \"Select a region…\" in the Capture list to draw a new one.",
                "OpenSteps",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return false;
        }

        _screenshotService.SelectedRegion = ToRectangle(region);
        return true;
    }

    /// <summary>While recording a region, keeps a dashed outline just outside it (not visible in the recording).</summary>
    private void ShowCaptureRegionOutline()
    {
        HideCaptureRegionOutline();
        if (ScreenshotMode == ScreenshotMode.Region && GetSavedRegion() is { } region)
        {
            _regionOutline = WindowHighlight.ShowOutline(ToRectangle(region));
        }
    }

    private void HideCaptureRegionOutline()
    {
        if (_regionOutline is { } outline)
        {
            _regionOutline = null;
            try
            {
                outline.Close();
                outline.Dispose();
            }
            catch
            {
                // The outline is only a visual hint.
            }
        }
    }

    /// <summary>
    /// Before recording in window mode: make sure the chosen window still exists and isn't minimized.
    /// Returns false (after telling the user) when it has been closed.
    /// </summary>
    private bool PrepareCaptureWindow()
    {
        if (_screenshotCaptureTargetStep is not null || (ScreenshotMode != ScreenshotMode.Window && !_captureWindowLost))
        {
            return true;
        }

        if (_captureWindowLost || _captureWindowHandle == IntPtr.Zero || !WindowEnumerator.IsCapturable(_captureWindowHandle, (uint)Environment.ProcessId))
        {
            var title = _settings.CaptureWindowTitle ?? "the chosen window";
            System.Windows.MessageBox.Show(
                this,
                $"The window chosen for recording ({title}) is no longer open.\n\nChoose another window, or another option, in the Capture list.",
                "OpenSteps",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return false;
        }

        WindowEnumerator.RestoreIfMinimized(_captureWindowHandle);
        if (WindowEnumerator.GetVisibleBounds(_captureWindowHandle) is { } bounds)
        {
            WindowHighlight.Flash(bounds);
        }

        return true;
    }

    /// <summary>Video target matching the capture choice. Called when a recording segment starts.</summary>
    private VideoCaptureTarget CreateVideoCaptureTarget()
    {
        _captureMonitorBounds = null;
        switch (ScreenshotMode)
        {
            case ScreenshotMode.Monitor:
                var monitor = _monitorService.FindMonitor(_captureMonitorDeviceName);
                var sorted = SortMonitors(_monitorService.GetAllMonitors());
                var position = sorted.FindIndex(m => m.MonitorHandle == monitor.MonitorHandle);
                _captureMonitorBounds = MonitorBoundsFor(monitor);
                return VideoCaptureTarget.SingleMonitor(monitor, position >= 0 ? GetMonitorLabel(sorted, position) : monitor.DeviceName ?? "Selected monitor");
            case ScreenshotMode.ActiveWindow:
                return VideoCaptureTarget.FollowActiveWindowMonitor(_monitorService);
            case ScreenshotMode.Window when _captureWindowHandle != IntPtr.Zero:
                return VideoCaptureTarget.SingleWindow(_captureWindowHandle, _selectedCaptureOption?.DisplayName ?? "Window");
            case ScreenshotMode.Region when GetSavedRegion() is { } region:
                _captureMonitorBounds = ToRectangle(region);
                return VideoCaptureTarget.Region(ToRectangle(region), ScreenshotModeLabels.GetRegionLabel(region.Width, region.Height));
            default:
                return VideoCaptureTarget.AllMonitors();
        }
    }

    /// <summary>In single-monitor mode, true for screen points on any other monitor; in region mode, for points outside the region.</summary>
    private bool IsOutsideCaptureMonitor(int x, int y)
    {
        if (ScreenshotMode == ScreenshotMode.Region)
        {
            var region = _captureMonitorBounds ?? (GetSavedRegion() is { } saved ? ToRectangle(saved) : null);
            return region is { } area && !area.Contains(x, y);
        }

        if (ScreenshotMode != ScreenshotMode.Monitor)
        {
            return false;
        }

        var bounds = _captureMonitorBounds ?? MonitorBoundsFor(_monitorService.FindMonitor(_captureMonitorDeviceName));
        return !bounds.Contains(x, y);
    }

    /// <summary>
    /// In single-monitor mode, true when the window receiving keystrokes is on another monitor;
    /// in region mode, when that window doesn't overlap the region.
    /// </summary>
    private bool IsForegroundOutsideCaptureMonitor()
    {
        if (ScreenshotMode == ScreenshotMode.Region)
        {
            var region = _captureMonitorBounds ?? (GetSavedRegion() is { } saved ? ToRectangle(saved) : null);
            var foregroundWindow = _activeWindowService.GetForegroundWindowHandle();
            return region is { } area
                && WindowEnumerator.GetVisibleBounds(foregroundWindow) is { } windowBounds
                && !windowBounds.IntersectsWith(area);
        }

        if (ScreenshotMode != ScreenshotMode.Monitor)
        {
            return false;
        }

        var foreground = _activeWindowService.GetForegroundWindowHandle();
        var windowMonitor = MonitorService.GetMonitorBoundsForWindow(foreground);
        var bounds = _captureMonitorBounds ?? MonitorBoundsFor(_monitorService.FindMonitor(_captureMonitorDeviceName));
        return windowMonitor is { } value && value != bounds;
    }

    private static List<DisplayMonitorInfo> SortMonitors(IEnumerable<DisplayMonitorInfo> monitors)
    {
        return monitors.OrderBy(monitor => monitor.BoundsLeft).ThenBy(monitor => monitor.BoundsTop).ToList();
    }

    private static string GetMonitorLabel(IReadOnlyList<DisplayMonitorInfo> sorted, int position)
    {
        var sideBySide = ScreenshotModeLabels.AreSideBySide(sorted.Select(m => (m.BoundsLeft, m.BoundsRight)).ToList());
        var monitor = sorted[position];
        return ScreenshotModeLabels.GetMonitorLabel(position, sorted.Count, sideBySide, monitor.Width, monitor.Height, monitor.IsPrimary);
    }

    private static System.Drawing.Rectangle ToRectangle(ScreenBounds bounds)
    {
        return new System.Drawing.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private static System.Drawing.Rectangle MonitorBoundsFor(DisplayMonitorInfo monitor)
    {
        return System.Drawing.Rectangle.FromLTRB(monitor.BoundsLeft, monitor.BoundsTop, monitor.BoundsRight, monitor.BoundsBottom);
    }
}
