namespace OpenSteps.Core.Models;

public enum ScreenshotMode
{
    /// <summary>All monitors.</summary>
    FullDesktop,

    /// <summary>The window being worked in. The video follows the monitor that window is on.</summary>
    ActiveWindow,

    /// <summary>One chosen monitor (<see cref="AppSettings.CaptureMonitorDeviceName"/>). Activity on other monitors is not recorded.</summary>
    Monitor,

    /// <summary>
    /// One chosen app window. Screenshots and video show only that window's area on screen, following it when
    /// it moves or resizes. Clicks and typing in other apps are still recorded, but their screenshots show the chosen window.
    /// </summary>
    Window
}
