using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class ScreenshotModeLabelsTests
{
    [Theory]
    [InlineData(ScreenshotMode.FullDesktop, "Full Desktop")]
    [InlineData(ScreenshotMode.ActiveWindow, "Active Window")]
    [InlineData(ScreenshotMode.Monitor, "Single Monitor")]
    public void GetDisplayText_ReturnsReadableModeName(ScreenshotMode mode, string expected)
    {
        Assert.Equal(expected, ScreenshotModeLabels.GetDisplayText(mode));
    }

    [Theory]
    [InlineData("\\\\.\\DISPLAY2", 1, 1920, 1080, false, "Display 2 · 1920×1080")]
    [InlineData("\\\\.\\DISPLAY1", 3, 2560, 1440, true, "Display 1 · 2560×1440 · main")]
    [InlineData(null, 3, 1280, 1024, false, "Display 3 · 1280×1024")]
    public void GetMonitorLabel_UsesWindowsDisplayNumber(string? deviceName, int fallback, int width, int height, bool primary, string expected)
    {
        Assert.Equal(expected, ScreenshotModeLabels.GetMonitorLabel(deviceName, fallback, width, height, primary));
    }
}
