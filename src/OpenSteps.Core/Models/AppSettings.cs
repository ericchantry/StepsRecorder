namespace OpenSteps.Core.Models;

public sealed class AppSettings
{
    public ScreenshotMode ScreenshotMode { get; set; } = ScreenshotMode.FullDesktop;

    /// <summary>Record a full-session MP4 alongside the screenshots.</summary>
    public bool RecordVideo { get; set; } = true;

    /// <summary>Store the characters typed (password fields are always masked).</summary>
    public bool CaptureTypedText { get; set; } = true;

    public int VideoFramesPerSecond { get; set; } = 10;
}
