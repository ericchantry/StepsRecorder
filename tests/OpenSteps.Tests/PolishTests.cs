using System.Drawing;
using System.Drawing.Imaging;
using OpenSteps.Capture;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class KeyboardHookRulesTests
{
    [Theory]
    [InlineData(true, false, true, false, false, true)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(true, true, true, false, false, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(true, false, true, false, true, false)]
    public void ShouldInstall_OnlyWhileRecordingWithKeyboardCaptureAllowed(bool recording, bool paused, bool enabled, bool policy, bool replacingScreenshot, bool expected)
    {
        Assert.Equal(expected, KeyboardHookRules.ShouldInstall(recording, paused, enabled, policy, replacingScreenshot));
    }
}

public sealed class SessionLibraryTests
{
    private static readonly SessionSummary[] Guides =
    [
        new() { Title = "Onboard a new supplier", Tags = ["finance", "onboarding"] },
        new() { Title = "Reset a password", Tags = ["IT"] },
        new() { Title = "Finance month-end close", Tags = [] }
    ];

    [Fact]
    public void Filter_EmptyQueryReturnsEverything()
    {
        Assert.Equal(3, SessionLibrary.Filter(Guides, "  ").Count);
        Assert.Equal(3, SessionLibrary.Filter(Guides, null).Count);
    }

    [Fact]
    public void Filter_MatchesTitleOrTagIgnoringCase()
    {
        Assert.Equal(["Onboard a new supplier", "Finance month-end close"], SessionLibrary.Filter(Guides, "FINANCE").Select(g => g.Title));
    }

    [Fact]
    public void Filter_HashMatchesTagsOnly()
    {
        Assert.Equal(["Onboard a new supplier"], SessionLibrary.Filter(Guides, "#fin").Select(g => g.Title));
        Assert.Empty(SessionLibrary.Filter(Guides, "#password"));
    }

    [Fact]
    public void Filter_EveryWordMustMatch()
    {
        Assert.Equal(["Onboard a new supplier"], SessionLibrary.Filter(Guides, "supplier #onboarding").Select(g => g.Title));
        Assert.Empty(SessionLibrary.Filter(Guides, "supplier #IT"));
    }

    [Fact]
    public void ParseTags_TrimsDeduplicatesAndDropsHash()
    {
        Assert.Equal(["onboarding", "HR", "new starter"], SessionLibrary.ParseTags(" onboarding, #HR; hr ,, new   starter "));
        Assert.Empty(SessionLibrary.ParseTags("  , ;"));
    }

    [Fact]
    public void ParseTags_LimitsLength()
    {
        Assert.Equal(SessionLibrary.MaxTagLength, SessionLibrary.ParseTags(new string('x', 80))[0].Length);
    }

    [Fact]
    public async Task Store_SavesTagsAndListsThem()
    {
        var root = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"));
        var store = new SessionStore(root);
        var session = new RecordingSession { Title = "Tagged" };
        await store.SaveSessionAsync(session);

        await store.SetTagsAsync(session.Id, ["finance", "Finance", " #onboarding "]);

        var summary = Assert.Single(await store.ListSessionsAsync());
        Assert.Equal(["finance", "onboarding"], summary.Tags);
        Assert.True(summary.HasTags);
        Assert.Equal(["finance", "onboarding"], (await store.LoadSessionAsync(session.Id))!.Tags);
    }
}

public sealed class RecordingRecoveryTests
{
    [Fact]
    public void Marker_RoundTripsAndClears()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"), "recording.json");
        var marker = new RecordingMarker(path);
        Assert.Null(marker.Read());

        var id = Guid.NewGuid();
        marker.Write(id, 1234);
        var info = marker.Read();
        Assert.Equal(id, info!.SessionId);
        Assert.Equal(1234, info.ProcessId);

        marker.Clear();
        Assert.Null(marker.Read());
        marker.Clear();
    }

    [Fact]
    public void Marker_IgnoresDamagedFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"), "recording.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        Assert.Null(new RecordingMarker(path).Read());
    }

    [Fact]
    public void MarkInterruptedSegments_GivesUnfinishedRunsALengthAndAReason()
    {
        var session = new RecordingSession();
        session.Segments.Add(new RecordingSegment { Number = 1, DurationSeconds = 12.5, VideoRelativePath = "videos/recording-1.mp4" });
        session.Segments.Add(new RecordingSegment { Number = 2, DurationSeconds = 0, VideoRelativePath = "videos/recording-2.mp4" });
        session.InputEvents.Add(new InputEvent { Segment = 2, OffsetSeconds = 7.25 });
        session.Steps.Add(new RecordedStep { VideoSegment = 2, VideoOffsetSeconds = 9.5 });

        Assert.True(RecordingRecovery.MarkInterruptedSegments(session));

        Assert.Equal(12.5, session.Segments[0].DurationSeconds);
        Assert.Null(session.Segments[0].VideoError);
        Assert.Equal(9.5, session.Segments[1].DurationSeconds);
        Assert.Null(session.Segments[1].VideoRelativePath);
        Assert.Equal(RecordingRecovery.InterruptedVideoMessage, session.Segments[1].VideoError);
        Assert.False(RecordingRecovery.MarkInterruptedSegments(session));
    }
}

public sealed class StepListOperationTests
{
    private static List<RecordedStep> ThreeSteps()
    {
        var steps = new List<RecordedStep>
        {
            new() { GeneratedTitle = "Click \"File\"", UserDescription = "Opens the menu." },
            new() { GeneratedTitle = "Choose \"Save As\"" },
            new() { GeneratedTitle = "Click \"Save\"", UserDescription = "Done." }
        };
        StepEditorOperations.RecalculateIndexes(steps);
        return steps;
    }

    [Fact]
    public void Duplicate_InsertsIndependentCopyBelow()
    {
        var steps = ThreeSteps();
        var original = steps[1];
        original.ScreenshotPath = @"C:\s\images\step-002.png";
        original.EditedScreenshotPath = @"C:\s\images\step-002-edited.png";
        original.Annotations.Add(new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Marker, Text = "1" });
        original.ScreenshotCrop = new ScreenBounds(1, 2, 300, 200);
        original.IsSelected = true;

        var copy = StepEditorOperations.Duplicate(steps, original, path => path?.Replace(".png", "-copy.png"));

        Assert.Equal(4, steps.Count);
        Assert.Same(copy, steps[2]);
        Assert.Equal([1, 2, 3, 4], steps.Select(s => s.Index));
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(original.GeneratedTitle, copy.GeneratedTitle);
        Assert.Equal(original.ScreenshotCrop, copy.ScreenshotCrop);
        Assert.EndsWith("step-002-copy.png", copy.ScreenshotPath);
        Assert.EndsWith("step-002-edited-copy.png", copy.EditedScreenshotPath);
        Assert.Equal("images/step-002-copy.png", copy.ScreenshotRelativePath);
        Assert.False(copy.IsSelected);

        copy.Annotations[0].Text = "9";
        Assert.Equal("1", original.Annotations[0].Text);
        Assert.NotEqual(original.Annotations[0].Id, copy.Annotations[0].Id);
    }

    [Fact]
    public void Merge_KeepsFirstStepAndCollectsTheOthersText()
    {
        var steps = ThreeSteps();
        var first = steps[0];

        var merged = StepEditorOperations.Merge(steps, [steps[2], steps[0]]);

        Assert.Same(first, merged);
        Assert.Equal(2, steps.Count);
        Assert.Equal([1, 2], steps.Select(s => s.Index));
        Assert.Equal("Click \"File\"", merged!.DisplayTitle);
        Assert.Equal(string.Join(Environment.NewLine, "Opens the menu.", "Click \"Save\"", "Done."), merged.UserDescription);
    }

    [Fact]
    public void Merge_NeedsAtLeastTwoSteps()
    {
        var steps = ThreeSteps();

        Assert.Null(StepEditorOperations.Merge(steps, [steps[0]]));
        Assert.Equal(3, steps.Count);
    }

    [Fact]
    public void RemoveMany_RemovesAndRenumbers()
    {
        var steps = ThreeSteps();
        var last = steps[2];

        Assert.Equal(2, StepEditorOperations.RemoveMany(steps, [steps[0], steps[1], new RecordedStep()]));

        Assert.Same(last, Assert.Single(steps));
        Assert.Equal(1, last.Index);
    }

    [Fact]
    public void Selection_IsNotSaved()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new RecordedStep { IsSelected = true });

        Assert.DoesNotContain("IsSelected", json);
    }
}

public sealed class EditMoveAndResizeTests
{
    [Fact]
    public void Move_ShiftsBothEndsOfABoxOrArrow()
    {
        var box = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Rectangle, X1 = 10, Y1 = 20, X2 = 110, Y2 = 70 };

        ScreenshotEditing.Move(box, 15, -5, 1000, 800);

        Assert.Equal((25d, 15d, 125d, 65d), (box.X1, box.Y1, box.X2, box.Y2));
    }

    [Fact]
    public void Move_CalloutKeepsItsRadiusAndStaysInTheImage()
    {
        var callout = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Marker, X1 = 50, Y1 = 50, X2 = 14, Y2 = 14 };

        ScreenshotEditing.Move(callout, 5000, -5000, 1000, 800);

        Assert.Equal((1000d, 0d, 14d), (callout.X1, callout.Y1, callout.X2));
    }

    [Fact]
    public void MoveHandle_ReshapesBoxFromOneCorner()
    {
        var box = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Highlight, X1 = 10, Y1 = 20, X2 = 110, Y2 = 70 };
        Assert.Equal(4, ScreenshotEditing.GetHandles(box).Count);

        ScreenshotEditing.MoveHandle(box, 2, 300, 200, 1000, 800);
        Assert.Equal((10d, 20d, 300d, 200d), (box.X1, box.Y1, box.X2, box.Y2));

        ScreenshotEditing.MoveHandle(box, 3, -40, 5000, 1000, 800);
        Assert.Equal((0d, 20d, 300d, 800d), (box.X1, box.Y1, box.X2, box.Y2));
    }

    [Fact]
    public void MoveHandle_MovesOneEndOfAnArrow()
    {
        var arrow = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Arrow, X1 = 10, Y1 = 10, X2 = 100, Y2 = 100 };
        Assert.Equal(2, ScreenshotEditing.GetHandles(arrow).Count);

        ScreenshotEditing.MoveHandle(arrow, 1, 400, 50, 1000, 800);

        Assert.Equal((10d, 10d, 400d, 50d), (arrow.X1, arrow.Y1, arrow.X2, arrow.Y2));
    }

    [Fact]
    public void CalloutsAndTextHaveNoResizeHandles()
    {
        Assert.Empty(ScreenshotEditing.GetHandles(new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Marker }));
        Assert.Empty(ScreenshotEditing.GetHandles(new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Text }));
    }

    [Fact]
    public void HitTestHandle_FindsTheNearestCorner()
    {
        var handles = ScreenshotEditing.GetHandles(new RedactionRegion(100, 100, 200, 50, RedactionMode.Pixelate));

        Assert.Equal(2, ScreenshotEditing.HitTestHandle(handles, 303, 148, 6));
        Assert.Equal(-1, ScreenshotEditing.HitTestHandle(handles, 200, 125, 6));
    }

    [Fact]
    public void Region_MovesInsideTheImageAndResizesFromACorner()
    {
        var region = new RedactionRegion(100, 100, 200, 50, RedactionMode.Pixelate);

        Assert.Equal(new RedactionRegion(800, 0, 200, 50, RedactionMode.Pixelate), ScreenshotEditing.Move(region, 5000, -5000, 1000, 800));
        Assert.Equal(new RedactionRegion(100, 100, 400, 300, RedactionMode.Pixelate), ScreenshotEditing.MoveHandle(region, 2, 500, 400, 1000, 800));
        Assert.Equal(new RedactionRegion(50, 60, 250, 90, RedactionMode.Pixelate), ScreenshotEditing.MoveHandle(region, 0, 50, 60, 1000, 800));
    }

    [Fact]
    public void TextLabel_HasBoundsAndCanBeHit()
    {
        var label = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Text, X1 = 100, Y1 = 200, X2 = 20, Text = "Start here" };

        var bounds = ScreenshotEditing.GetBounds(label);

        Assert.Equal((100d, 200d), (bounds.X, bounds.Y));
        Assert.True(bounds.Width > 100 && bounds.Height == 32);
        Assert.Equal(0, ScreenshotEditing.HitTestAnnotation([label], 150, 215, 4));
        Assert.Equal(-1, ScreenshotEditing.HitTestAnnotation([label], 150, 300, 4));
    }

    [Theory]
    [InlineData("#FACC15", true)]
    [InlineData("#FFFFFF", true)]
    [InlineData("#D92D20", false)]
    [InlineData("#111827", false)]
    [InlineData("#FF2563EB", false)]
    [InlineData("nonsense", false)]
    public void NeedsDarkText_ForLightColours(string colour, bool expected)
    {
        Assert.Equal(expected, ScreenshotEditing.NeedsDarkText(colour));
    }

    [Fact]
    public void TextLabel_IsDrawnInItsColourOnTheSavedImage()
    {
        var folder = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var original = Path.Combine(folder, "original.png");
        var output = Path.Combine(folder, "edited.png");
        using (var image = new Bitmap(400, 200, PixelFormat.Format32bppArgb))
        {
            using var graphics = Graphics.FromImage(image);
            graphics.Clear(Color.White);
            image.Save(original, ImageFormat.Png);
        }

        var label = new ScreenshotAnnotation { Type = ScreenshotAnnotationType.Text, X1 = 40, Y1 = 60, X2 = 24, Text = "Start here", Color = "#2563EB" };
        new ScreenshotRedactionService().ApplyEdits(original, output, [], [label], null);

        using var edited = new Bitmap(output);
        var plate = edited.GetPixel(46, 64);
        Assert.True(plate.B > 180 && plate.R < 90, $"Plate pixel was {plate}");
        Assert.Equal(Color.White.ToArgb(), edited.GetPixel(20, 20).ToArgb());
    }
}

public sealed class ThemeSettingsTests
{
    [Fact]
    public async Task Theme_DefaultsToWindowsAndIsSaved()
    {
        Assert.Equal(AppThemePreference.System, new AppSettings().Theme);

        var path = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        var service = new SettingsService(path);
        await service.SaveAsync(new AppSettings { Theme = AppThemePreference.Dark });

        Assert.Equal(AppThemePreference.Dark, (await service.LoadAsync()).Theme);
        Assert.Contains("\"Dark\"", await File.ReadAllTextAsync(path));
    }
}
