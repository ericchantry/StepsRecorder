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
    /// Monitor label for the capture picker, named by where the monitor sits so it is recognisable
    /// ("Left monitor · 1920×1200", "Middle monitor · 3440×1440 · main"). Windows' own display numbers
    /// (\\.\DISPLAY5) often skip numbers on docks and don't match Display settings, so they aren't used.
    /// </summary>
    /// <param name="position">0-based position after sorting monitors left to right.</param>
    /// <param name="count">Number of monitors.</param>
    /// <param name="sideBySide">True when the monitors are arranged in one horizontal row.</param>
    public static string GetMonitorLabel(int position, int count, bool sideBySide, int width, int height, bool isPrimary)
    {
        var name = (sideBySide, count, position) switch
        {
            (true, 2, 0) => "Left monitor",
            (true, 2, 1) => "Right monitor",
            (true, 3, 0) => "Left monitor",
            (true, 3, 1) => "Middle monitor",
            (true, 3, 2) => "Right monitor",
            _ => $"Monitor {position + 1}"
        };

        return $"{name} · {width}×{height}{(isPrimary ? " · main" : string.Empty)}";
    }

    /// <summary>True when every monitor (sorted by left edge) starts at or after the previous one's right edge.</summary>
    public static bool AreSideBySide(IReadOnlyList<(int Left, int Right)> sortedByLeft)
    {
        for (var i = 1; i < sortedByLeft.Count; i++)
        {
            if (sortedByLeft[i].Left < sortedByLeft[i - 1].Right - 1)
            {
                return false;
            }
        }

        return true;
    }
}
