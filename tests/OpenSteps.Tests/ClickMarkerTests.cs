using System.Drawing;
using System.Drawing.Imaging;
using OpenSteps.Capture;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class ClickMarkerTests
{
    [Theory]
    [InlineData(StepActionType.Click, "Left", ClickMarkerKind.Left)]
    [InlineData(StepActionType.RightClick, "Right", ClickMarkerKind.Right)]
    [InlineData(StepActionType.DoubleClick, "Left", ClickMarkerKind.Double)]
    [InlineData(StepActionType.Click, "Middle", ClickMarkerKind.Middle)]
    [InlineData(StepActionType.Click, null, ClickMarkerKind.Left)]
    public void For_MapsStepActionToMarker(StepActionType action, string? button, ClickMarkerKind expected)
    {
        Assert.Equal(expected, ClickMarkers.For(action, button));
    }

    [Fact]
    public void Labels_NameEachAction()
    {
        Assert.Equal("Click", ClickMarkers.GetLabel(ClickMarkerKind.Left));
        Assert.Equal("Right-click", ClickMarkers.GetLabel(ClickMarkerKind.Right));
        Assert.Equal("Double-click", ClickMarkers.GetLabel(ClickMarkerKind.Double));
        Assert.Equal("2×", ClickMarkers.GetShortLabel(ClickMarkerKind.Double));
    }

    [Fact]
    public void IsDoubleClick_RequiresSecondPressWithinTimeAndDistance()
    {
        var start = DateTimeOffset.UtcNow;
        var first = (100, 100, start);
        var time = TimeSpan.FromMilliseconds(500);

        Assert.True(ClickMarkers.IsDoubleClick(first, 102, 101, start.AddMilliseconds(300), time, 4, 4));
        Assert.False(ClickMarkers.IsDoubleClick(first, 102, 101, start.AddMilliseconds(600), time, 4, 4));
        Assert.False(ClickMarkers.IsDoubleClick(first, 110, 100, start.AddMilliseconds(200), time, 4, 4));
        Assert.False(ClickMarkers.IsDoubleClick(null, 100, 100, start, time, 4, 4));
    }

    [Fact]
    public void ScreenshotMarker_UsesTheColourOfTheClickKind()
    {
        using var bitmap = new Bitmap(200, 200, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            CaptureOverlayRenderer.DrawScreenshotMarker(graphics, 100, 100, ClickMarkerKind.Right, bitmap.Size);
        }

        var centre = bitmap.GetPixel(100, 100);
        var blue = CaptureOverlayRenderer.GetColor(ClickMarkerKind.Right);
        Assert.True(Math.Abs(centre.B - blue.B) < 40 && Math.Abs(centre.R - blue.R) < 40, $"Centre pixel was {centre}");
    }

    [Fact]
    public void ScreenshotMarker_BadgeStaysInsideImageNearTheEdge()
    {
        using var bitmap = new Bitmap(120, 80, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);

        var exception = Record.Exception(() => CaptureOverlayRenderer.DrawScreenshotMarker(graphics, 115, 75, ClickMarkerKind.Double, bitmap.Size));
        Assert.Null(exception);
    }

    [Fact]
    public void VideoClick_DrawsAtEveryStageOfTheAnimation()
    {
        using var bitmap = new Bitmap(100, 100, PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        foreach (var progress in new[] { 0.0, 0.5, 1.0, 1.5 })
        {
            CaptureOverlayRenderer.DrawVideoClick(graphics, 50, 50, ClickMarkerKind.Left, progress, bitmap.Size);
        }

        CaptureOverlayRenderer.DrawCursorHalo(graphics, 10, 10);
    }

    [Fact]
    public void CoverSensitiveAreas_PaintsOverTheWholeField()
    {
        using var bitmap = new Bitmap(300, 100, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.White);
            CaptureOverlayRenderer.CoverSensitiveAreas(graphics, [new ScreenBounds(20, 30, 200, 30)]);
        }

        foreach (var (x, y) in new[] { (20, 30), (219, 59), (25, 35) })
        {
            var pixel = bitmap.GetPixel(x, y);
            Assert.True(pixel.R < 80 && pixel.G < 80 && pixel.B < 80, $"Pixel ({x},{y}) was {pixel}");
        }

        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(19, 30).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(220, 45).ToArgb());
    }
}
