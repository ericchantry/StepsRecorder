using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class StepEditingMoveResizeTests
{
    [Fact]
    public void Move_ShiftsBoxAndKeepsItsSize()
    {
        var box = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Rectangle, X1 = 10, Y1 = 20, X2 = 110, Y2 = 70 };

        ScreenshotEditing.Move(box, 30, -5, 1000, 600);

        Assert.Equal((40, 15, 140, 65), (box.X1, box.Y1, box.X2, box.Y2));
    }

    [Fact]
    public void Move_CalloutKeepsItsRadius()
    {
        var callout = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Marker, X1 = 100, Y1 = 100, X2 = 14, Y2 = 14, Text = "1" };

        ScreenshotEditing.Move(callout, 25, 25, 1000, 600);

        Assert.Equal((125, 125, 14), (callout.X1, callout.Y1, callout.X2));
    }

    [Fact]
    public void Move_StopsWhenCentreWouldLeaveTheImage()
    {
        var box = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Rectangle, X1 = 900, Y1 = 500, X2 = 1000, Y2 = 600 };

        ScreenshotEditing.Move(box, 500, 500, 1000, 600);

        var (left, top, right, bottom) = ScreenshotEditing.GetBounds(box);
        Assert.Equal(1000, (left + right) / 2);
        Assert.Equal(600, (top + bottom) / 2);
    }

    [Fact]
    public void Resize_BoxCornerMovesAndOppositeCornerStays()
    {
        var box = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Rectangle, X1 = 100, Y1 = 100, X2 = 200, Y2 = 180 };

        ScreenshotEditing.Resize(box, EditHandle.TopLeft, 60, 70, 1000, 600);

        Assert.Equal((60, 70, 200, 180), ScreenshotEditing.GetBounds(box));
    }

    [Fact]
    public void Resize_BoxCannotBeTurnedInsideOut()
    {
        var box = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Rectangle, X1 = 100, Y1 = 100, X2 = 200, Y2 = 180 };

        ScreenshotEditing.Resize(box, EditHandle.BottomRight, 20, 20, 1000, 600);

        var (left, top, right, bottom) = ScreenshotEditing.GetBounds(box);
        Assert.True(right > left && bottom > top);
        Assert.Equal((100, 100), (left, top));
    }

    [Fact]
    public void Resize_ArrowMovesOnlyTheDraggedEnd()
    {
        var arrow = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Arrow, X1 = 10, Y1 = 10, X2 = 100, Y2 = 100 };

        ScreenshotEditing.Resize(arrow, EditHandle.End, 300, 40, 1000, 600);

        Assert.Equal((10, 10, 300, 40), (arrow.X1, arrow.Y1, arrow.X2, arrow.Y2));
        Assert.Equal(2, ScreenshotEditing.GetHandles(arrow).Count);
    }

    [Fact]
    public void Resize_TextLabelKeepsItsProportions()
    {
        var label = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Text, X1 = 100, Y1 = 100, X2 = 300, Y2 = 140, Text = "Click here" };

        ScreenshotEditing.Resize(label, EditHandle.BottomRight, 0, 180, 1000, 600);

        Assert.Equal((100, 100, 500, 180), ScreenshotEditing.GetBounds(label));
    }

    [Fact]
    public void HitTestHandle_FindsCornerWithinTolerance()
    {
        var box = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Rectangle, X1 = 100, Y1 = 100, X2 = 200, Y2 = 180 };

        Assert.Equal(EditHandle.BottomRight, ScreenshotEditing.HitTestHandle(box, 203, 177, 6));
        Assert.Equal(EditHandle.None, ScreenshotEditing.HitTestHandle(box, 150, 140, 6));
    }

    [Fact]
    public void HitTestAnnotation_FindsTextLabels()
    {
        var annotations = new List<ScreenshotAnnotation>
        {
            new() { Type = ScreenshotAnnotationType.Text, X1 = 100, Y1 = 100, X2 = 300, Y2 = 140, Text = "Here" }
        };

        Assert.Equal(0, ScreenshotEditing.HitTestAnnotation(annotations, 200, 120, 4));
    }

    [Fact]
    public void Regions_MoveAndResizeInsideTheImage()
    {
        var region = new RedactionRegion(900, 500, 100, 100, RedactionMode.Pixelate);

        Assert.Equal(new RedactionRegion(900, 500, 100, 100, RedactionMode.Pixelate), ScreenshotEditing.MoveRegion(region, 50, 50, 1000, 600));
        Assert.Equal(new RedactionRegion(850, 480, 100, 100, RedactionMode.Pixelate), ScreenshotEditing.MoveRegion(region, -50, -20, 1000, 600));
        Assert.Equal(new RedactionRegion(900, 500, 60, 40, RedactionMode.Pixelate), ScreenshotEditing.ResizeRegion(region, EditHandle.BottomRight, 960, 540, 1000, 600));
        Assert.Equal(EditHandle.TopLeft, ScreenshotEditing.HitTestRegionHandle(region, 902, 498, 5));
    }
}

public sealed class StepListOperationTests
{
    private static List<RecordedStep> Steps(params string[] titles)
    {
        var steps = titles.Select(title => new RecordedStep { UserTitle = title, GeneratedTitle = title }).ToList();
        StepEditorOperations.RecalculateIndexes(steps);
        return steps;
    }

    [Fact]
    public void Duplicate_AddsIndependentCopyBelowWithItsOwnImages()
    {
        var steps = Steps("One", "Two");
        steps[0].ScreenshotPath = @"C:\s\images\step-001.png";
        steps[0].EditedScreenshotPath = @"C:\s\images\step-001-annotated.png";
        steps[0].Annotations.Add(new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Arrow, X2 = 5 });
        var copies = 0;

        var copy = StepEditorOperations.DuplicateBelow(steps, steps[0], source => $@"C:\s\images\copy-{++copies}.png");

        Assert.Equal(["One", "One", "Two"], steps.Select(s => s.DisplayTitle));
        Assert.Equal([1, 2, 3], steps.Select(s => s.Index));
        Assert.NotEqual(steps[0].Id, copy.Id);
        Assert.Equal(@"C:\s\images\copy-1.png", copy.ScreenshotPath);
        Assert.Equal("images/copy-1.png", copy.ScreenshotRelativePath);
        Assert.Equal(@"C:\s\images\copy-2.png", copy.EditedScreenshotPath);

        copy.Annotations[0].X2 = 99;
        Assert.Equal(5, steps[0].Annotations[0].X2);
    }

    [Fact]
    public void Merge_FoldsNextStepIntoDescriptionAndRemovesIt()
    {
        var steps = Steps("Open the \"File\" menu", "Choose \"Save As\"", "Click \"Save\"");
        steps[0].UserDescription = "Top left of the window.";
        steps[1].UserDescription = "A dialog opens.";

        var removed = StepEditorOperations.MergeWithNext(steps, steps[0]);

        Assert.Equal("Choose \"Save As\"", removed!.DisplayTitle);
        Assert.Equal(2, steps.Count);
        Assert.Equal("Open the \"File\" menu", steps[0].DisplayTitle);
        Assert.Equal(["Top left of the window.", "Choose \"Save As\"", "A dialog opens."], steps[0].UserDescription!.ReplaceLineEndings("\n").Split('\n'));
        Assert.Equal([1, 2], steps.Select(s => s.Index));
    }

    [Fact]
    public void Merge_LastStepHasNothingToMerge()
    {
        var steps = Steps("One", "Two");

        Assert.Null(StepEditorOperations.MergeWithNext(steps, steps[1]));
        Assert.Equal(2, steps.Count);
    }

    [Fact]
    public void Split_MovesLaterParagraphsToNewStepWithUneditedScreenshot()
    {
        var steps = Steps("Fill in the form");
        steps[0].ScreenshotPath = @"C:\s\images\step-001.png";
        steps[0].EditedScreenshotPath = @"C:\s\images\step-001-annotated.png";
        steps[0].ScreenshotEditFormat = 1;
        steps[0].ScreenshotCrop = new ScreenBounds(0, 0, 50, 50);
        steps[0].UserDescription = "Enter the customer name.\n\nThen choose the country.\nAnd the region.";

        var second = StepEditorOperations.SplitBelow(steps, steps[0], _ => @"C:\s\images\copy-1.png");

        Assert.Equal(2, steps.Count);
        Assert.Equal("Enter the customer name.", steps[0].UserDescription);
        Assert.Equal("Fill in the form (continued)", second.DisplayTitle);
        Assert.StartsWith("Then choose the country.", second.UserDescription);
        Assert.Equal(@"C:\s\images\copy-1.png", second.ScreenshotPath);
        Assert.Null(second.EditedScreenshotPath);
        Assert.False(second.HasScreenshotEdits);
        Assert.NotNull(steps[0].ScreenshotCrop);
    }

    [Theory]
    [InlineData(null, "", "")]
    [InlineData("Only one line", "Only one line", "")]
    [InlineData("First\nSecond\nThird", "First", "Second\nThird")]
    [InlineData("Para one\nstill one\n\nPara two", "Para one\nstill one", "Para two")]
    public void SplitDescription_BreaksAtFirstParagraphOrLine(string? description, string keep, string move)
    {
        var result = StepEditorOperations.SplitDescription(description);

        Assert.Equal(keep, result.Keep.ReplaceLineEndings("\n"));
        Assert.Equal(move, result.Move.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void RemoveSelected_DeletesEveryTickedStepAndRenumbers()
    {
        var steps = Steps("One", "Two", "Three", "Four");
        steps[1].IsSelected = true;
        steps[3].IsSelected = true;

        var removed = StepEditorOperations.RemoveSelected(steps);

        Assert.Equal(2, removed.Count);
        Assert.Equal(["One", "Three"], steps.Select(s => s.DisplayTitle));
        Assert.Equal([1, 2], steps.Select(s => s.Index));
    }
}
