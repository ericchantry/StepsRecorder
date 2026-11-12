using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class AppControlTests
{
    [Theory]
    [InlineData("quit", AppActivity.Idle, "ok", true)]
    [InlineData("quit", AppActivity.Recording, "recording", false)]
    [InlineData("quit", AppActivity.Busy, "busy", false)]
    [InlineData("status", AppActivity.Idle, "idle", false)]
    [InlineData("status", AppActivity.Recording, "recording", false)]
    [InlineData("activate", AppActivity.Recording, "ok", true)]
    [InlineData(" QUIT ", AppActivity.Idle, "ok", true)]
    [InlineData("format c:", AppActivity.Idle, "unknown", false)]
    [InlineData(null, AppActivity.Idle, "unknown", false)]
    public void Decide_NeverClosesWhileWorkCouldBeLost(string? command, AppActivity activity, string reply, bool act)
    {
        Assert.Equal((reply, act), AppControl.Decide(command, activity));
    }

    [Fact]
    public void Names_ArePerUserAndPerWindowsSession()
    {
        Assert.Equal("OpenSteps-2-S-1-5-21-1", AppControl.GetPipeName("S-1-5-21-1", 2));
        Assert.NotEqual(AppControl.GetPipeName("S-1-5-21-1", 2), AppControl.GetPipeName("S-1-5-21-2", 2));
        Assert.StartsWith(@"Local\", AppControl.GetMutexName("S-1-5-21-1"));
    }
}

public sealed class RecordingRecoveryTests
{
    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<(SessionStore Store, RecordingSession Session, string Directory, string Video)> CreateInterruptedSessionAsync(int steps)
    {
        var store = new SessionStore(CreateRoot());
        var session = new RecordingSession { Title = "Month-end close" };
        session.Segments.Add(new RecordingSegment { Number = 1, VideoRelativePath = "videos/recording-1.mp4" });
        for (var i = 0; i < steps; i++)
        {
            session.Steps.Add(new RecordedStep { GeneratedTitle = $"Step {i + 1}", VideoSegment = 1, VideoOffsetSeconds = 4.5 * (i + 1) });
        }

        await store.SaveSessionAsync(session);
        var directory = session.SessionDirectory;
        var video = Path.Combine(directory, "videos", "recording-1.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(video)!);
        await File.WriteAllBytesAsync(video, [0, 0, 0, 24]);
        RecordingRecovery.WriteMarker(directory, new RecordingMarker(session.Id, 4242, DateTimeOffset.Now, 1, "videos/recording-1.mp4"));
        return (store, session, directory, video);
    }

    [Fact]
    public async Task FindInterrupted_ReportsSessionsWhoseRecorderIsGone()
    {
        var (store, session, _, _) = await CreateInterruptedSessionAsync(steps: 3);

        var found = await RecordingRecovery.FindInterruptedAsync(store, _ => false);
        var stillRecording = await RecordingRecovery.FindInterruptedAsync(store, processId => processId == 4242);

        var interrupted = Assert.Single(found);
        Assert.Equal(session.Id, interrupted.Marker.SessionId);
        Assert.Equal("Month-end close", interrupted.Title);
        Assert.Equal(3, interrupted.StepCount);
        Assert.Empty(stillRecording);
    }

    [Fact]
    public async Task Repair_KeepsStepsDropsUnplayableVideoAndClearsMarker()
    {
        var (store, session, directory, video) = await CreateInterruptedSessionAsync(steps: 2);
        var interrupted = Assert.Single(await RecordingRecovery.FindInterruptedAsync(store, _ => false));

        var repaired = await RecordingRecovery.RepairAsync(store, interrupted);

        Assert.NotNull(repaired);
        Assert.Equal(2, repaired.Steps.Count);
        Assert.False(File.Exists(video));
        Assert.False(File.Exists(Path.Combine(directory, RecordingRecovery.MarkerFileName)));
        var reloaded = await store.LoadSessionAsync(session.Id);
        var segment = Assert.Single(reloaded!.Segments);
        Assert.Null(segment.VideoRelativePath);
        Assert.Equal(RecordingRecovery.InterruptedVideoMessage, segment.VideoError);
        Assert.Equal(9, segment.DurationSeconds);
        Assert.Empty(await RecordingRecovery.FindInterruptedAsync(store, _ => false));
    }

    [Fact]
    public async Task Repair_RemovesFolderWhenNothingWasRecordedYet()
    {
        var (store, _, directory, _) = await CreateInterruptedSessionAsync(steps: 0);
        var interrupted = Assert.Single(await RecordingRecovery.FindInterruptedAsync(store, _ => false));

        Assert.Null(await RecordingRecovery.RepairAsync(store, interrupted));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Repair_KeepsEarlierPartsWhenOnlyTheNewPartHadNoSteps()
    {
        var store = new SessionStore(CreateRoot());
        var session = new RecordingSession { Title = "Two parts" };
        session.Segments.Add(new RecordingSegment { Number = 1, DurationSeconds = 30 });
        session.Segments.Add(new RecordingSegment { Number = 2 });
        await store.SaveSessionAsync(session);
        RecordingRecovery.WriteMarker(session.SessionDirectory, new RecordingMarker(session.Id, 1, DateTimeOffset.Now, 2, null));

        var repaired = await RecordingRecovery.RepairAsync(store, Assert.Single(await RecordingRecovery.FindInterruptedAsync(store, _ => false)));

        Assert.NotNull(repaired);
        Assert.True(Directory.Exists(session.SessionDirectory));
        Assert.Equal(RecordingRecovery.InterruptedVideoMessage, repaired.Segments[1].VideoError);
        Assert.Null(repaired.Segments[0].VideoError);
    }

    [Fact]
    public async Task FindInterrupted_DropsUnreadableMarkers()
    {
        var store = new SessionStore(CreateRoot());
        var directory = Path.Combine(store.RootDirectory, "broken");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, RecordingRecovery.MarkerFileName), "not json");

        Assert.Empty(await RecordingRecovery.FindInterruptedAsync(store, _ => false));
        Assert.False(File.Exists(Path.Combine(directory, RecordingRecovery.MarkerFileName)));
    }
}

public sealed class ThemeAndLibraryTests
{
    [Theory]
    [InlineData(AppTheme.System, false, false, ResolvedTheme.Light)]
    [InlineData(AppTheme.System, true, false, ResolvedTheme.Dark)]
    [InlineData(AppTheme.Light, true, false, ResolvedTheme.Light)]
    [InlineData(AppTheme.Dark, false, false, ResolvedTheme.Dark)]
    [InlineData(AppTheme.Light, false, true, ResolvedTheme.HighContrast)]
    [InlineData(AppTheme.Dark, true, true, ResolvedTheme.HighContrast)]
    public void Resolve_ContrastThemeAlwaysWins(AppTheme chosen, bool windowsDark, bool highContrast, ResolvedTheme expected)
    {
        Assert.Equal(expected, ThemeResolver.Resolve(chosen, windowsDark, highContrast));
    }

    [Fact]
    public void ParseTags_CleansSplitsAndDeduplicates()
    {
        Assert.Equal(["billing", "month end", "Onboarding"], SessionLibrary.ParseTags(" #billing, month   end ; Onboarding,, BILLING "));
        Assert.Empty(SessionLibrary.ParseTags(null));
        Assert.Equal(SessionLibrary.MaxTags, SessionLibrary.ParseTags(string.Join(",", Enumerable.Range(1, 40))).Count);
        Assert.Equal(SessionLibrary.MaxTagLength, SessionLibrary.ParseTags(new string('x', 80))[0].Length);
    }

    [Fact]
    public void Filter_MatchesEveryWordInTitleOrTags()
    {
        var sessions = new[]
        {
            new SessionSummary { Title = "Month-end close in Munis", Tags = ["finance", "billing"] },
            new SessionSummary { Title = "Reset a password", Tags = ["helpdesk"] },
            new SessionSummary { Title = "Billing dispute repro", Tags = [] }
        };

        Assert.Equal(3, SessionLibrary.Filter(sessions, "  ").Count());
        Assert.Equal(["Month-end close in Munis", "Billing dispute repro"], SessionLibrary.Filter(sessions, "billing").Select(s => s.Title));
        Assert.Equal(["Month-end close in Munis"], SessionLibrary.Filter(sessions, "#billing").Select(s => s.Title));
        Assert.Equal(["Month-end close in Munis"], SessionLibrary.Filter(sessions, "close FINANCE").Select(s => s.Title));
        Assert.Empty(SessionLibrary.Filter(sessions, "payroll"));
    }

    [Fact]
    public async Task Tags_AreSavedWithTheSessionAndListed()
    {
        var store = new SessionStore(Path.Combine(Path.GetTempPath(), "OpenSteps.Tests", Guid.NewGuid().ToString("N")));
        var session = new RecordingSession { Title = "Tagged" };
        await store.SaveSessionAsync(session);

        await store.SetSessionTagsAsync(session.Id, ["billing", "Billing", " month end "]);
        await store.RenameSessionAsync(session.Id, "Renamed");

        var summary = Assert.Single(await store.ListSessionsAsync());
        Assert.Equal("Renamed", summary.Title);
        Assert.Equal(["billing", "month end"], summary.Tags);
        Assert.True(summary.HasTags);
    }
}

/// <summary>Checks on the app's XAML that would otherwise only show up when a window is opened.</summary>
public sealed class XamlConventionTests
{
    private static string AppDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OpenSteps.sln")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return Path.Combine(directory.FullName, "src", "OpenSteps.App");
        }
    }

    private static IEnumerable<string> BrushKeys(string themeFile)
    {
        return System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(AppDirectory, "Themes", themeFile)), "x:Key=\"(\\w+)\"")
            .Select(match => match.Groups[1].Value)
            .Order();
    }

    [Fact]
    public void EveryThemeDefinesTheSameBrushes()
    {
        var light = BrushKeys("Light.xaml").ToList();

        Assert.NotEmpty(light);
        Assert.Equal(light, BrushKeys("Dark.xaml"));
        Assert.Equal(light, BrushKeys("HighContrast.xaml"));
    }

    [Fact]
    public void EveryBrushUsedInXamlExistsInTheThemes()
    {
        var defined = BrushKeys("Light.xaml").ToHashSet();
        foreach (var file in Directory.EnumerateFiles(AppDirectory, "*.xaml", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"\{DynamicResource (\w+Brush)\}"))
            {
                Assert.True(defined.Contains(match.Groups[1].Value), $"{Path.GetFileName(file)} uses {match.Groups[1].Value}, which no theme defines.");
            }

            // A StaticResource brush would not change with the theme.
            Assert.DoesNotMatch(@"\{StaticResource \w+Brush\}", text);
        }
    }

    [Fact]
    public void InputsAndIconButtonsHaveScreenReaderNames()
    {
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(AppDirectory, "*.xaml", SearchOption.TopDirectoryOnly))
        {
            var text = File.ReadAllText(file);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"<(TextBox|ComboBox|Slider|ListBox)\b([^>]*)>"))
            {
                if (!match.Groups[2].Value.Contains("AutomationProperties.Name"))
                {
                    var line = text[..match.Index].Count(c => c == '\n') + 1;
                    problems.Add($"{Path.GetFileName(file)}:{line} <{match.Groups[1].Value}> has no AutomationProperties.Name");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
