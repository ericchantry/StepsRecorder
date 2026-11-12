namespace OpenSteps.Core.Models;

public sealed class AppSettings
{
    public ScreenshotMode ScreenshotMode { get; set; } = ScreenshotMode.FullDesktop;

    /// <summary>Windows device name (e.g. \\.\DISPLAY2) of the monitor to record when <see cref="ScreenshotMode"/> is Monitor.</summary>
    public string? CaptureMonitorDeviceName { get; set; }

    /// <summary>Title of the window to record when <see cref="ScreenshotMode"/> is Window, used to find it again next time.</summary>
    public string? CaptureWindowTitle { get; set; }

    /// <summary>Process name of the window to record when <see cref="ScreenshotMode"/> is Window.</summary>
    public string? CaptureWindowProcessName { get; set; }

    /// <summary>Light, dark, or whatever Windows is set to.</summary>
    public AppThemePreference Theme { get; set; } = AppThemePreference.System;

    /// <summary>Screen area (physical pixels) to record when <see cref="ScreenshotMode"/> is Region.</summary>
    public ScreenBounds? CaptureRegion { get; set; }

    /// <summary>Draw a soft halo around the mouse pointer in the video so it is easy to follow.</summary>
    public bool HighlightCursor { get; set; } = true;

    /// <summary>Record a full-session MP4 alongside the screenshots.</summary>
    public bool RecordVideo { get; set; } = true;

    /// <summary>Store the characters typed (password fields are always masked).</summary>
    public bool CaptureTypedText { get; set; } = true;

    public int VideoFramesPerSecond { get; set; } = 10;
}
