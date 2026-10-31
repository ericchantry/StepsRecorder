using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class ScreenshotModeLabelsTests
{
    [Theory]
    [InlineData(ScreenshotMode.FullDesktop, "Full Desktop")]
    [InlineData(ScreenshotMode.ActiveWindow, "Active Window")]
    [InlineData(ScreenshotMode.Monitor, "Single Monitor")]
    [InlineData(ScreenshotMode.Window, "Single Window")]
    public void GetDisplayText_ReturnsReadableModeName(ScreenshotMode mode, string expected)
    {
        Assert.Equal(expected, ScreenshotModeLabels.GetDisplayText(mode));
    }

    [Theory]
    [InlineData(0, 3, true, 1920, 1200, false, "Left monitor · 1920×1200")]
    [InlineData(1, 3, true, 3440, 1440, true, "Middle monitor · 3440×1440 · main")]
    [InlineData(2, 3, true, 2560, 1440, false, "Right monitor · 2560×1440")]
    [InlineData(1, 2, true, 1920, 1080, false, "Right monitor · 1920×1080")]
    [InlineData(1, 2, false, 1920, 1080, false, "Monitor 2 · 1920×1080")]
    [InlineData(3, 4, true, 1280, 1024, false, "Monitor 4 · 1280×1024")]
    public void GetMonitorLabel_NamesMonitorsByPosition(int position, int count, bool sideBySide, int width, int height, bool primary, string expected)
    {
        Assert.Equal(expected, ScreenshotModeLabels.GetMonitorLabel(position, count, sideBySide, width, height, primary));
    }

    [Fact]
    public void AreSideBySide_DetectsStackedMonitors()
    {
        Assert.True(ScreenshotModeLabels.AreSideBySide([(-1920, 0), (0, 3440), (3440, 6000)]));
        Assert.False(ScreenshotModeLabels.AreSideBySide([(0, 1920), (0, 1920)]));
    }
}
