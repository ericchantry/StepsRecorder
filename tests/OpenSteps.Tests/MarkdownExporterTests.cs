using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class MarkdownExporterTests
{
    [Fact]
    public void MarkdownBuilder_HandlesManualStepWithoutScreenshot()
    {
        var session = new RecordingSession { Title = "Manual Guide" };
        session.Steps.Add(new RecordedStep
        {
            ActionType = StepActionType.Manual,
            GeneratedTitle = "Manual step",
            UserTitle = "New step",
            UserDescription = "User description here."
        });

        var markdown = new MarkdownBuilder().BuildMarkdown(session);

        Assert.Contains("## Step 1: New step", markdown);
        Assert.Contains("User description here.", markdown);
        Assert.DoesNotContain("![Step 1]", markdown);
    }

    [Fact]
    public void MarkdownBuilder_UsesEditedTitleOverGeneratedTitle()
    {
        var session = new RecordingSession { Title = "Titles" };
        session.Steps.Add(new RecordedStep { GeneratedTitle = "Generated", UserTitle = "Edited" });

        var markdown = new MarkdownBuilder().BuildMarkdown(session);

        Assert.Contains("## Step 1: Edited", markdown);
        Assert.DoesNotContain("## Step 1: Generated", markdown);
    }

    [Fact]
    public void MarkdownBuilder_RespectsCurrentStepOrder()
    {
        var session = new RecordingSession { Title = "Order Test" };
        session.Steps.Add(new RecordedStep { GeneratedTitle = "Second" });
        session.Steps.Add(new RecordedStep { GeneratedTitle = "First" });
        session.Steps.Reverse();

        var markdown = new MarkdownBuilder().BuildMarkdown(session);

        Assert.True(markdown.IndexOf("## Step 1: First", StringComparison.Ordinal) < markdown.IndexOf("## Step 2: Second", StringComparison.Ordinal));
    }

    [Fact]
    public void MarkdownBuilder_DoesNotEmitImageLinkForMissingScreenshot()
    {
        var session = new RecordingSession { Title = "Missing Screenshot" };
        session.Steps.Add(new RecordedStep
        {
            GeneratedTitle = "Missing",
            ScreenshotPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.png")
        });

        var markdown = new MarkdownBuilder().BuildMarkdown(session);

        Assert.Contains("## Step 1: Missing", markdown);
        Assert.DoesNotContain("images/step-001.png", markdown);
    }

    [Fact]
    public async Task ExportAsync_WritesGuideAndSequentialImagesInPortableFolder()
    {
        var temp = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(temp, "source");
        Directory.CreateDirectory(source);
        var image = Path.Combine(source, "capture.png");
        await File.WriteAllBytesAsync(image, [1, 2, 3]);

        var session = new RecordingSession { Title = "Test Guide" };
        session.Steps.Add(new RecordedStep
        {
            GeneratedTitle = "Click the 'File' menu",
            UserTitle = "Open File menu",
            UserDescription = "Choose the main menu.",
            ScreenshotPath = image
        });

        var result = await new MarkdownExporter().ExportAsync(session, temp);

        Assert.True(File.Exists(result.MarkdownPath));
        Assert.Equal(Path.Combine(temp, "Test Guide"), result.ExportFolder);
        Assert.True(File.Exists(Path.Combine(result.ExportFolder, "images", "step-001.png")));
        var markdown = await File.ReadAllTextAsync(result.MarkdownPath);
        Assert.Contains("# Test Guide", markdown);
        Assert.Contains("## Step 1: Open File menu", markdown);
        Assert.Contains("![Step 1](images/step-001.png)", markdown);
        Assert.DoesNotContain("AppData", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportAsync_CanWriteMarkdownAndHtmlFiles()
    {
        var temp = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"));
        var session = new RecordingSession { Title = "Format Test" };
        session.Steps.Add(new RecordedStep
        {
            GeneratedTitle = "Generated",
            UserTitle = "Export formats",
            UserDescription = "Check the outputs."
        });

        var result = await new MarkdownExporter().ExportAsync(
            session,
            temp,
            GuideExportFormat.Markdown | GuideExportFormat.Html);

        Assert.True(File.Exists(result.MarkdownPath));
        Assert.True(File.Exists(result.HtmlPath));
        Assert.Contains(result.MarkdownPath, result.OutputPaths);
        Assert.Contains(result.HtmlPath, result.OutputPaths);
        Assert.Contains("<h2>Step 1: Export formats</h2>", await File.ReadAllTextAsync(result.HtmlPath));
    }

    [Fact]
    public async Task ExportAsync_CreatesUniqueFolderWhenTitleExists()
    {
        var temp = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "Conflict Test"));

        var session = new RecordingSession { Title = "Conflict Test" };
        session.Steps.Add(new RecordedStep { GeneratedTitle = "Click" });

        var result = await new MarkdownExporter().ExportAsync(session, temp);

        Assert.Equal(Path.Combine(temp, "Conflict Test (1)"), result.ExportFolder);
        Assert.True(File.Exists(result.MarkdownPath));
    }

    [Fact]
    public async Task ExportAsync_WritesImagesInCurrentStepOrder()
    {
        var temp = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(temp, "source");
        Directory.CreateDirectory(source);

        var firstImage = Path.Combine(source, "first.png");
        var secondImage = Path.Combine(source, "second.png");
        await File.WriteAllBytesAsync(firstImage, [1]);
        await File.WriteAllBytesAsync(secondImage, [2]);

        var session = new RecordingSession { Title = "Image Order Test" };
        session.Steps.Add(new RecordedStep { GeneratedTitle = "First", ScreenshotPath = firstImage });
        session.Steps.Add(new RecordedStep { GeneratedTitle = "Second", ScreenshotPath = secondImage });

        var result = await new MarkdownExporter().ExportAsync(session, temp);

        Assert.Equal([1], await File.ReadAllBytesAsync(Path.Combine(result.ExportFolder, "images", "step-001.png")));
        Assert.Equal([2], await File.ReadAllBytesAsync(Path.Combine(result.ExportFolder, "images", "step-002.png")));
    }

    [Fact]
    public async Task ExportAsync_UsesEditedScreenshotWhenAvailable()
    {
        var temp = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(temp, "source");
        Directory.CreateDirectory(source);

        var original = Path.Combine(source, "original.png");
        var edited = Path.Combine(source, "edited.png");
        await File.WriteAllBytesAsync(original, [1]);
        await File.WriteAllBytesAsync(edited, [9]);

        var session = new RecordingSession { Title = "Edited Export Test" };
        session.Steps.Add(new RecordedStep { GeneratedTitle = "Step", ScreenshotPath = original, EditedScreenshotPath = edited });

        var result = await new MarkdownExporter().ExportAsync(session, temp);

        Assert.Equal([9], await File.ReadAllBytesAsync(Path.Combine(result.ExportFolder, "images", "step-001.png")));
    }

    [Fact]
    public async Task ExportAsync_SkipsMissingScreenshotAndReturnsWarning()
    {
        var temp = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(temp, "source");
        Directory.CreateDirectory(source);

        var original = Path.Combine(source, "missing.png");
        var session = new RecordingSession { Title = "Missing Export Test" };
        session.Steps.Add(new RecordedStep { GeneratedTitle = "Step", ScreenshotPath = original });

        var result = await new MarkdownExporter().ExportAsync(session, temp);
        var markdown = await File.ReadAllTextAsync(result.MarkdownPath);

        Assert.Contains("Step 1 screenshot was missing", Assert.Single(result.Warnings));
        Assert.DoesNotContain("images/step-001.png", markdown);
    }

    [Fact]
    public void GetUniqueExportFolder_UsesTitleAndNumericSuffix()
    {
        var temp = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "Title"));

        var folder = new MarkdownExporter().GetUniqueExportFolder(temp, "Title");

        Assert.Equal(Path.Combine(temp, "Title (1)"), folder);
    }

    [Fact]
    public void ToSafeFolderName_RemovesInvalidFilenameCharacters()
    {
        var safe = new MarkdownExporter().ToSafeFolderName("Bad<>:\"/\\|?* Name");

        Assert.DoesNotContain('<', safe);
        Assert.DoesNotContain('>', safe);
        Assert.Contains("Bad", safe);
        Assert.Contains("Name", safe);
    }
}

public sealed class RecordingExportTests
{
    [Fact]
    public async Task ExportAsync_IncludesVideoTimelineEnvironmentAndZip()
    {
        var temp = Path.Combine(Path.GetTempPath(), "OpenStepsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var video = Path.Combine(temp, "source.mp4");
        await File.WriteAllBytesAsync(video, [1, 2, 3]);

        var session = new RecordingSession { Title = "Repro" };
        session.Segments.Add(new RecordingSegment { Number = 1, VideoPath = video, DurationSeconds = 20, TypedTextCaptured = true });
        session.Environment = new EnvironmentInfo { OperatingSystem = "Windows 11 Pro 24H2" };
        session.InputEvents.Add(new InputEvent { Segment = 1, OffsetSeconds = 3, Kind = InputEventKind.KeyText, Text = "<b>", WindowTitle = "Notepad" });
        session.InputEvents.Add(new InputEvent { Segment = 1, OffsetSeconds = 5, Kind = InputEventKind.Shortcut, Key = "Ctrl+S", WindowTitle = "Notepad" });
        session.Steps.Add(new RecordedStep { GeneratedTitle = "Press Ctrl+S", VideoSegment = 1, VideoOffsetSeconds = 5 });

        var result = await new MarkdownExporter().ExportAsync(
            session,
            temp,
            GuideExportFormat.Markdown | GuideExportFormat.Html,
            new GuideExportOptions(CreateZip: true));

        Assert.True(File.Exists(Path.Combine(result.ExportFolder, "videos", "recording-1.mp4")));
        var html = await File.ReadAllTextAsync(result.HtmlPath);
        Assert.Contains("<video id=\"video-1\" data-seg=\"1\" src=\"videos/recording-1.mp4\"", html);
        Assert.Contains("data-seg=\"1\" data-t=\"5\"", html);
        Assert.Contains(System.Text.Encodings.Web.HtmlEncoder.Default.Encode("Pressed Ctrl+S"), html);
        Assert.Contains(System.Text.Encodings.Web.HtmlEncoder.Default.Encode("Typed '<b>'"), html);
        Assert.DoesNotContain("'<b>'", html);
        Assert.Contains("Windows 11 Pro 24H2", html);

        var markdown = await File.ReadAllTextAsync(result.MarkdownPath);
        Assert.Contains("| 0:05.0 | Pressed Ctrl+S | Notepad |", markdown);
        Assert.Contains("[Watch the video](videos/recording-1.mp4)", markdown);
        Assert.Contains("_Video: [recording 1](videos/recording-1.mp4) at 0:05.0_", markdown);

        Assert.True(File.Exists(result.ZipPath));
        using var zip = System.IO.Compression.ZipFile.OpenRead(result.ZipPath);
        Assert.Contains(zip.Entries, entry => entry.FullName.EndsWith("videos/recording-1.mp4", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExportAsync_CanLeaveOutVideoAndInputLog()
    {
        var temp = Path.Combine(Path.GetTempPath(), "OpenStepsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var video = Path.Combine(temp, "source.mp4");
        await File.WriteAllBytesAsync(video, [1]);
        var session = new RecordingSession { Title = "Doc only" };
        session.Segments.Add(new RecordingSegment { Number = 1, VideoPath = video });
        session.InputEvents.Add(new InputEvent { Segment = 1, OffsetSeconds = 1, Kind = InputEventKind.Shortcut, Key = "Ctrl+C" });
        session.Steps.Add(new RecordedStep { GeneratedTitle = "Copy", VideoSegment = 1, VideoOffsetSeconds = 1 });

        var result = await new MarkdownExporter().ExportAsync(
            session,
            temp,
            GuideExportFormat.Html,
            new GuideExportOptions(IncludeVideo: false, IncludeInputLog: false));

        Assert.False(Directory.Exists(Path.Combine(result.ExportFolder, "videos")));
        var html = await File.ReadAllTextAsync(result.HtmlPath);
        Assert.DoesNotContain("<video", html);
        Assert.DoesNotContain(System.Text.Encodings.Web.HtmlEncoder.Default.Encode("Pressed Ctrl+C"), html);
        Assert.DoesNotContain("<script>", html);
    }
}
