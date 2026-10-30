namespace OpenSteps.Core.Models;

public enum ScreenshotMode
{
    /// <summary>All monitors.</summary>
    FullDesktop,

    /// <summary>The window being worked in. The video follows the monitor that window is on.</summary>
    ActiveWindow,

    /// <summary>One chosen monitor (<see cref="AppSettings.CaptureMonitorDeviceName"/>). Activity on other monitors is not recorded.</summary>
    Monitor
}
