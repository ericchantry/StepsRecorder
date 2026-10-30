using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

public static class ScreenshotModeLabels
{
    public static string GetDisplayText(ScreenshotMode mode)
    {
        return mode switch
        {
            ScreenshotMode.ActiveWindow => "Active Window",
            ScreenshotMode.Monitor => "Single Monitor",
            _ => "Full Desktop"
        };
    }

    /// <summary>
    /// Short monitor label for the capture picker, e.g. "Display 2 · 1920×1080 · main".
    /// The number comes from the Windows device name (\\.\DISPLAY2) so it matches Display settings where possible.
    /// </summary>
    public static string GetMonitorLabel(string? deviceName, int fallbackNumber, int width, int height, bool isPrimary)
    {
        var number = fallbackNumber;
        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            var digits = new string(deviceName.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            if (int.TryParse(digits, out var parsed))
            {
                number = parsed;
            }
        }

        return $"Display {number} · {width}×{height}{(isPrimary ? " · main" : string.Empty)}";
    }
}
