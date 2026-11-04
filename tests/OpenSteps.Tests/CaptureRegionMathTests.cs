using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class CaptureRegionMathTests
{
    [Theory]
    [InlineData(100, 50, 400, 250)]
    [InlineData(400, 250, 100, 50)]
    [InlineData(400, 50, 100, 250)]
    public void FromCorners_WorksInAnyDragDirection(int x1, int y1, int x2, int y2)
    {
        Assert.Equal(new ScreenBounds(100, 50, 300, 200), CaptureRegionMath.FromCorners(x1, y1, x2, y2));
    }

    [Theory]
    [InlineData(39, 200, false)]
    [InlineData(200, 39, false)]
    [InlineData(40, 40, true)]
    public void IsLargeEnough_RejectsAccidentalClicks(int width, int height, bool expected)
    {
        Assert.Equal(expected, CaptureRegionMath.IsLargeEnough(new ScreenBounds(0, 0, width, height)));
    }

    [Fact]
    public void ClampToScreen_TrimsRegionThatHangsOffScreen()
    {
        var screen = new ScreenBounds(-1920, 0, 3840, 1080);

        var clamped = CaptureRegionMath.ClampToScreen(new ScreenBounds(1800, 900, 400, 400), screen);

        Assert.Equal(new ScreenBounds(1800, 900, 120, 180), clamped);
    }

    [Fact]
    public void ClampToScreen_ReturnsNullWhenMonitorIsGone()
    {
        var screen = new ScreenBounds(0, 0, 1920, 1080);

        Assert.Null(CaptureRegionMath.ClampToScreen(new ScreenBounds(2000, 100, 800, 600), screen));
        Assert.Null(CaptureRegionMath.ClampToScreen(new ScreenBounds(1900, 100, 800, 600), screen));
    }

    [Fact]
    public void ToLocal_PadsClipsAndOffsetsIntoCapturedArea()
    {
        var area = new ScreenBounds(100, 100, 500, 400);
        var fields = new[]
        {
            new ScreenBounds(150, 200, 100, 20),
            new ScreenBounds(90, 90, 20, 20),
            new ScreenBounds(1000, 1000, 50, 50),
            new ScreenBounds(300, 300, 0, 10)
        };

        var local = CaptureRegionMath.ToLocal(fields, area, padding: 2);

        Assert.Equal(2, local.Count);
        Assert.Equal(new ScreenBounds(48, 98, 104, 24), local[0]);
        Assert.Equal(new ScreenBounds(0, 0, 12, 12), local[1]);
    }

    [Fact]
    public void Contains_UsesHalfOpenBounds()
    {
        var area = new ScreenBounds(10, 10, 100, 50);

        Assert.True(CaptureRegionMath.Contains(area, 10, 10));
        Assert.True(CaptureRegionMath.Contains(area, 109, 59));
        Assert.False(CaptureRegionMath.Contains(area, 110, 30));
        Assert.False(CaptureRegionMath.Contains(area, 50, 9));
    }
}
