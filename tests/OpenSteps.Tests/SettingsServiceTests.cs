using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task SaveAndLoadAsync_PreservesScreenshotModeAsStringEnum()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        var service = new SettingsService(path);

        await service.SaveAsync(new AppSettings { ScreenshotMode = ScreenshotMode.ActiveWindow });

        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"screenshotMode\": \"ActiveWindow\"", json);

        var loaded = await service.LoadAsync();
        Assert.Equal(ScreenshotMode.ActiveWindow, loaded.ScreenshotMode);
    }

    [Fact]
    public async Task LoadAsync_ReturnsFullDesktopDefaultWhenSettingsFileIsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"), "settings.json");

        var settings = await new SettingsService(path).LoadAsync();

        Assert.Equal(ScreenshotMode.FullDesktop, settings.ScreenshotMode);
    }

    [Fact]
    public async Task SaveAndLoadAsync_PreservesCaptureRegionAndCursorHighlight()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        var service = new SettingsService(path);

        await service.SaveAsync(new AppSettings
        {
            ScreenshotMode = ScreenshotMode.Region,
            CaptureRegion = new ScreenBounds(-1920, 40, 1280, 720),
            HighlightCursor = false
        });

        var loaded = await service.LoadAsync();
        Assert.Equal(ScreenshotMode.Region, loaded.ScreenshotMode);
        Assert.Equal(new ScreenBounds(-1920, 40, 1280, 720), loaded.CaptureRegion);
        Assert.False(loaded.HighlightCursor);
    }

    [Fact]
    public void NewSettings_HighlightCursorByDefault()
    {
        Assert.True(new AppSettings().HighlightCursor);
        Assert.Null(new AppSettings().CaptureRegion);
    }
}
