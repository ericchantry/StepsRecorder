using System.ComponentModel;
using OpenSteps.Capture;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.App;

/// <summary>
/// The "Capture" choice on the recorder bar: all monitors, one monitor, or the active window.
/// It decides what screenshots and the video cover, and in single-monitor mode it also
/// ignores clicks and typing on the other monitors.
/// </summary>
public partial class MainWindow
{
    private string? _captureMonitorDeviceName;
    private System.Drawing.Rectangle? _captureMonitorBounds;
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

            _selectedCaptureOption = value;
            _captureMonitorDeviceName = value.MonitorDeviceName;
            _settings.CaptureMonitorDeviceName = value.MonitorDeviceName;
            _screenshotService.SelectedMonitorDeviceName = value.MonitorDeviceName;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedCaptureOption)));

            var modeChanged = ScreenshotMode != value.Mode;
            ScreenshotMode = value.Mode;
            if (!modeChanged)
            {
                // Same mode, different monitor: the setter above did not save or refresh.
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
            monitors = _monitorService.GetAllMonitors()
                .OrderBy(monitor => monitor.BoundsLeft)
                .ThenBy(monitor => monitor.BoundsTop)
                .ToList();
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
                var monitor = monitors[i];
                var label = ScreenshotModeLabels.GetMonitorLabel(monitor.DeviceName, i + 1, monitor.Width, monitor.Height, monitor.IsPrimary);
                options.Add(new ScreenshotModeOption(label, ScreenshotMode.Monitor, monitor.DeviceName));
            }
        }

        options.Add(new ScreenshotModeOption("Active window", ScreenshotMode.ActiveWindow));

        var wanted = ScreenshotMode == ScreenshotMode.Monitor
            ? options.FirstOrDefault(o => o.Mode == ScreenshotMode.Monitor && string.Equals(o.MonitorDeviceName, _captureMonitorDeviceName, StringComparison.OrdinalIgnoreCase))
                ?? options.FirstOrDefault(o => o.Mode == ScreenshotMode.Monitor && monitors.FirstOrDefault(m => m.IsPrimary)?.DeviceName == o.MonitorDeviceName)
            : options.FirstOrDefault(o => o.Mode == ScreenshotMode);
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
        return ScreenshotMode == ScreenshotMode.Monitor && _selectedCaptureOption?.Mode == ScreenshotMode.Monitor
            ? _selectedCaptureOption.DisplayName.Split(" · ")[0]
            : ScreenshotModeLabels.GetDisplayText(ScreenshotMode);
    }

    /// <summary>Video target matching the capture choice. Called when a recording segment starts.</summary>
    private VideoCaptureTarget CreateVideoCaptureTarget()
    {
        _captureMonitorBounds = null;
        switch (ScreenshotMode)
        {
            case ScreenshotMode.Monitor:
                var monitor = _monitorService.FindMonitor(_captureMonitorDeviceName);
                _captureMonitorBounds = System.Drawing.Rectangle.FromLTRB(monitor.BoundsLeft, monitor.BoundsTop, monitor.BoundsRight, monitor.BoundsBottom);
                return VideoCaptureTarget.SingleMonitor(monitor, ScreenshotModeLabels.GetMonitorLabel(monitor.DeviceName, monitor.Index, monitor.Width, monitor.Height, monitor.IsPrimary));
            case ScreenshotMode.ActiveWindow:
                return VideoCaptureTarget.FollowActiveWindowMonitor(_monitorService);
            default:
                return VideoCaptureTarget.AllMonitors();
        }
    }

    /// <summary>In single-monitor mode, true for screen points on any other monitor.</summary>
    private bool IsOutsideCaptureMonitor(int x, int y)
    {
        if (ScreenshotMode != ScreenshotMode.Monitor)
        {
            return false;
        }

        var bounds = _captureMonitorBounds ?? MonitorBoundsFor(_monitorService.FindMonitor(_captureMonitorDeviceName));
        return !bounds.Contains(x, y);
    }

    /// <summary>In single-monitor mode, true when the window receiving keystrokes is on another monitor.</summary>
    private bool IsForegroundOutsideCaptureMonitor()
    {
        if (ScreenshotMode != ScreenshotMode.Monitor)
        {
            return false;
        }

        var foreground = _activeWindowService.GetForegroundWindowHandle();
        var windowMonitor = MonitorService.GetMonitorBoundsForWindow(foreground);
        var bounds = _captureMonitorBounds ?? MonitorBoundsFor(_monitorService.FindMonitor(_captureMonitorDeviceName));
        return windowMonitor is { } value && value != bounds;
    }

    private static System.Drawing.Rectangle MonitorBoundsFor(DisplayMonitorInfo monitor)
    {
        return System.Drawing.Rectangle.FromLTRB(monitor.BoundsLeft, monitor.BoundsTop, monitor.BoundsRight, monitor.BoundsBottom);
    }
}
