using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class ScreenshotEditingTests
{
    [Fact]
    public void NextCalloutNumber_FollowsTheHighestNumberUsed()
    {
        var annotations = new List<ScreenshotAnnotation>
        {
            Callout("1"),
            Callout("2"),
            Callout("A"),
            new() { Type = ScreenshotAnnotationType.Rectangle }
        };

        Assert.Equal(3, ScreenshotEditing.NextCalloutNumber(annotations));
        Assert.Equal(1, ScreenshotEditing.NextCalloutNumber([]));
    }

    [Fact]
    public void RenumberCallouts_ClosesGapsAndLeavesLettersAlone()
    {
        var annotations = new List<ScreenshotAnnotation> { Callout("1"), Callout("A"), Callout("3"), Callout("4") };

        ScreenshotEditing.RenumberCallouts(annotations);

        Assert.Equal(["1", "A", "2", "3"], annotations.Select(a => a.Text));
    }

    [Fact]
    public void HitTestAnnotation_FindsTopmostEditUnderPointer()
    {
        var annotations = new List<ScreenshotAnnotation>
        {
            new() { Type = ScreenshotAnnotationType.Rectangle, X1 = 0, Y1 = 0, X2 = 100, Y2 = 100 },
            new() { Type = ScreenshotAnnotationType.Arrow, X1 = 10, Y1 = 50, X2 = 90, Y2 = 50, StrokeThickness = 3 },
            new() { Type = ScreenshotAnnotationType.Marker, X1 = 200, Y1 = 200, X2 = 15, Text = "1" }
        };

        Assert.Equal(1, ScreenshotEditing.HitTestAnnotation(annotations, 50, 52, 4));
        Assert.Equal(0, ScreenshotEditing.HitTestAnnotation(annotations, 50, 80, 4));
        Assert.Equal(2, ScreenshotEditing.HitTestAnnotation(annotations, 210, 205, 4));
        Assert.Equal(-1, ScreenshotEditing.HitTestAnnotation(annotations, 300, 300, 4));
    }

    [Fact]
    public void HitTestRedaction_FindsRegionUnderPointer()
    {
        var regions = new List<RedactionRegion> { new(0, 0, 50, 50, RedactionMode.Pixelate), new(40, 40, 50, 50, RedactionMode.RedCircle) };

        Assert.Equal(1, ScreenshotEditing.HitTestRedaction(regions, 45, 45));
        Assert.Equal(0, ScreenshotEditing.HitTestRedaction(regions, 10, 10));
        Assert.Equal(-1, ScreenshotEditing.HitTestRedaction(regions, 200, 10));
    }

    [Fact]
    public void NormalizeCrop_ClipsToImageAndIgnoresUselessCrops()
    {
        Assert.Equal(new ScreenBounds(900, 500, 100, 100), ScreenshotEditing.NormalizeCrop(new ScreenBounds(900, 500, 300, 300), 1000, 600));
        Assert.Null(ScreenshotEditing.NormalizeCrop(new ScreenBounds(0, 0, 1000, 600), 1000, 600));
        Assert.Null(ScreenshotEditing.NormalizeCrop(new ScreenBounds(10, 10, 5, 200), 1000, 600));
        Assert.Null(ScreenshotEditing.NormalizeCrop(null, 1000, 600));
    }

    [Fact]
    public void MarkSizes_GrowWithLargeScreenshots()
    {
        Assert.Equal(3, ScreenshotEditing.StrokeFor(800, 600));
        Assert.Equal(6, ScreenshotEditing.StrokeFor(3840, 2160));
        Assert.Equal(14, ScreenshotEditing.CalloutRadiusFor(800, 600));
        Assert.True(ScreenshotEditing.CalloutRadiusFor(3840, 2160) > 30);
    }

    [Fact]
    public void Clone_CopiesEveryField()
    {
        var original = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Arrow, X1 = 1, Y1 = 2, X2 = 3, Y2 = 4, Text = "x", Color = "#000000", Opacity = 0.5, StrokeThickness = 7 };

        var copy = ScreenshotEditing.Clone(original);
        copy.X1 = 99;

        Assert.Equal(1, original.X1);
        Assert.Equal(original.Id, copy.Id);
        Assert.Equal((original.Type, original.Y2, original.Text, original.Color, original.Opacity, original.StrokeThickness), (copy.Type, copy.Y2, copy.Text, copy.Color, copy.Opacity, copy.StrokeThickness));
    }

    [Fact]
    public void Step_HasEditsUntilCleared()
    {
        var step = new RecordedStep { ScreenshotEditFormat = 1 };
        Assert.False(step.HasScreenshotEdits);

        step.ScreenshotCrop = new ScreenBounds(0, 0, 50, 50);
        Assert.True(step.HasScreenshotEdits);

        step.Annotations.Add(Callout("1"));
        step.ClearScreenshotEdits();
        Assert.False(step.HasScreenshotEdits);
        Assert.Null(step.ScreenshotCrop);
        Assert.Empty(step.Annotations);
    }

    [Fact]
    public void Step_EditedByOlderVersionCountsAsEdited()
    {
        var step = new RecordedStep { ScreenshotEditFormat = 0, EditedScreenshotPath = "C:\\old\\step-001-annotated.png" };

        Assert.True(step.HasScreenshotEdits);
    }

    private static ScreenshotAnnotation Callout(string text)
    {
        return new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Marker, X2 = 14, Text = text };
    }
}
