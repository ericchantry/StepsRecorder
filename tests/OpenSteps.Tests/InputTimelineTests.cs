using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class RecordingClockTests
{
    private DateTimeOffset _now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Elapsed_ExcludesPausedTime()
    {
        var clock = new RecordingClock(() => _now);
        clock.Start();
        _now = _now.AddSeconds(10);
        clock.Pause();
        _now = _now.AddSeconds(30);
        Assert.Equal(10, clock.Elapsed.TotalSeconds, 3);

        clock.Resume();
        _now = _now.AddSeconds(5);
        Assert.Equal(15, clock.Elapsed.TotalSeconds, 3);
    }

    [Fact]
    public void ElapsedAt_MapsPastInstantsAndClampsBeforeStart()
    {
        var start = _now;
        var clock = new RecordingClock(() => _now);
        clock.Start();
        _now = _now.AddSeconds(8);

        Assert.Equal(3, clock.ElapsedAt(start.AddSeconds(3)).TotalSeconds, 3);
        Assert.Equal(TimeSpan.Zero, clock.ElapsedAt(start.AddSeconds(-5)));
    }

    [Fact]
    public void Stop_FreezesElapsed()
    {
        var clock = new RecordingClock(() => _now);
        clock.Start();
        _now = _now.AddSeconds(4);
        clock.Stop();
        _now = _now.AddSeconds(100);

        Assert.Equal(4, clock.Elapsed.TotalSeconds, 3);
        Assert.False(clock.IsRunning);
    }
}

public sealed class KeyNamesTests
{
    [Theory]
    [InlineData(0x46, KeyModifiers.Ctrl, false, KeyCategory.Shortcut, "Ctrl+F")]
    [InlineData(0x4E, KeyModifiers.Ctrl | KeyModifiers.Shift, false, KeyCategory.Shortcut, "Ctrl+Shift+N")]
    [InlineData(0x52, KeyModifiers.Win, false, KeyCategory.Shortcut, "Win+R")]
    [InlineData(0x09, KeyModifiers.Alt, false, KeyCategory.Shortcut, "Alt+Tab")]
    [InlineData(0x74, KeyModifiers.None, false, KeyCategory.SpecialKey, "F5")]
    [InlineData(0x09, KeyModifiers.Shift, false, KeyCategory.SpecialKey, "Shift+Tab")]
    [InlineData(0x0D, KeyModifiers.None, false, KeyCategory.SpecialKey, "Enter")]
    [InlineData(0x41, KeyModifiers.Shift, true, KeyCategory.Text, "A")]
    [InlineData(0x20, KeyModifiers.None, true, KeyCategory.Text, "Space")]
    [InlineData(0x24, KeyModifiers.None, false, KeyCategory.TimelineOnly, "Home")]
    [InlineData(0x22, KeyModifiers.Shift, false, KeyCategory.TimelineOnly, "Shift+Page Down")]
    public void Classify_NamesKeysAndCombos(int vk, KeyModifiers modifiers, bool producesText, KeyCategory category, string name)
    {
        var result = KeyNames.Classify(vk, modifiers, producesText);

        Assert.NotNull(result);
        Assert.Equal(category, result.Category);
        Assert.Equal(name, result.Name);
    }

    [Fact]
    public void Classify_TreatsAltGrCharacterAsText()
    {
        var result = KeyNames.Classify(0x45, KeyModifiers.Ctrl | KeyModifiers.Alt, producesText: true);

        Assert.Equal(KeyCategory.Text, result?.Category);
    }

    [Theory]
    [InlineData(0x10)]
    [InlineData(0xA2)]
    [InlineData(0x5B)]
    public void Classify_IgnoresModifierOnlyPresses(int vk)
    {
        Assert.Null(KeyNames.Classify(vk, KeyModifiers.None, producesText: false));
    }
}

public sealed class SensitiveInputDetectorTests
{
    [Theory]
    [InlineData(true, null, null, true)]
    [InlineData(false, "Password", null, true)]
    [InlineData(false, "Enter your PIN", null, true)]
    [InlineData(false, null, "txtCardNumber", true)]
    [InlineData(false, "Card number", null, true)]
    [InlineData(false, "CVV", null, true)]
    [InlineData(false, "Search", "searchBox", false)]
    [InlineData(false, "Pinned items", null, false)]
    [InlineData(false, "Spinner", null, false)]
    public void IsSensitive_MatchesSecretFields(bool isPassword, string? name, string? automationId, bool expected)
    {
        Assert.Equal(expected, SensitiveInputDetector.IsSensitive(isPassword, name, automationId));
    }
}

public sealed class TypedTextBufferTests
{
    [Fact]
    public void Backspace_RemovesLastCharacter()
    {
        var buffer = new TypedTextBuffer();
        buffer.Append("helo");
        buffer.Backspace();
        buffer.Append("lo");

        Assert.Equal("hello", buffer.Text);
    }

    [Fact]
    public void Truncate_AddsEllipsis()
    {
        Assert.Equal("abcd…", TypedTextBuffer.Truncate("abcdefghij", 5));
        Assert.Equal("abc", TypedTextBuffer.Truncate("abc", 5));
    }
}

public sealed class InputTimelineBuilderTests
{
    [Fact]
    public void Build_GroupsTypingAndAppliesBackspace()
    {
        var events = new List<InputEvent>
        {
            Text(1.0, "h"),
            Text(1.1, "e"),
            Text(1.2, "x"),
            Key(1.3, "Backspace", InputEventKind.Key),
            Text(1.4, "y"),
            Key(2.0, "Enter", InputEventKind.Key)
        };

        var entries = InputTimelineBuilder.Build(events);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Typed 'hey'", entries[0].Label);
        Assert.Equal("hex⌫y", entries[0].RawKeys);
        Assert.Equal(1.0, entries[0].OffsetSeconds);
        Assert.Equal("Notepad (notepad)", entries[0].Detail);
        Assert.Equal("Pressed Enter", entries[1].Label);
    }

    [Fact]
    public void Build_SplitsTypingAfterLongPause()
    {
        var entries = InputTimelineBuilder.Build([Text(1, "a"), Text(5, "b")]);

        Assert.Equal(["Typed 'a'", "Typed 'b'"], entries.Select(e => e.Label));
    }

    [Fact]
    public void Build_NeverShowsMaskedCharacters()
    {
        var masked = new[]
        {
            new InputEvent { Kind = InputEventKind.KeyText, OffsetSeconds = 1, Masked = true },
            new InputEvent { Kind = InputEventKind.KeyText, OffsetSeconds = 1.1, Masked = true }
        };

        var entry = Assert.Single(InputTimelineBuilder.Build(masked));
        Assert.Equal("Typed hidden text (2 keys)", entry.Label);
        Assert.Null(entry.RawKeys);
    }

    [Fact]
    public void Build_ReportsKeyCountWhenTextCaptureWasOff()
    {
        var entry = Assert.Single(InputTimelineBuilder.Build([Text(1, null), Text(1.2, null), Text(1.3, null)]));

        Assert.Equal("Typed 3 keys (text not recorded)", entry.Label);
    }

    [Fact]
    public void Build_CoalescesScrollNotches()
    {
        var events = new[]
        {
            Scroll(3.0, -120),
            Scroll(3.2, -120),
            Scroll(3.4, -240),
            Scroll(3.6, 120)
        };

        var entries = InputTimelineBuilder.Build(events);

        Assert.Equal(["Scrolled down 4 notches", "Scrolled up 1 notch"], entries.Select(e => e.Label));
    }

    [Fact]
    public void Build_OrdersBySegmentThenTime()
    {
        var events = new[]
        {
            new InputEvent { Segment = 2, OffsetSeconds = 0.5, Kind = InputEventKind.Click, X = 1, Y = 2 },
            new InputEvent { Segment = 1, OffsetSeconds = 9, Kind = InputEventKind.Shortcut, Key = "Ctrl+S" }
        };

        var entries = InputTimelineBuilder.Build(events);

        Assert.Equal("Pressed Ctrl+S", entries[0].Label);
        Assert.Equal("Left click at (1, 2)", entries[1].Label);
        Assert.Equal(TimelineCategory.Mouse, entries[1].Category);
    }

    private static InputEvent Text(double offset, string? text)
    {
        return new InputEvent { Kind = InputEventKind.KeyText, OffsetSeconds = offset, Text = text, WindowTitle = "Notepad", ProcessName = "notepad" };
    }

    private static InputEvent Key(double offset, string key, InputEventKind kind)
    {
        return new InputEvent { Kind = kind, OffsetSeconds = offset, Key = key, WindowTitle = "Notepad", ProcessName = "notepad" };
    }

    private static InputEvent Scroll(double offset, int delta)
    {
        return new InputEvent { Kind = InputEventKind.Scroll, OffsetSeconds = offset, Delta = delta, WindowTitle = "Edge" };
    }
}

public sealed class TimeFormatAndPolicyTests
{
    [Theory]
    [InlineData(0, "0:00.0")]
    [InlineData(12.46, "0:12.4")]
    [InlineData(75.0, "1:15.0")]
    [InlineData(3725.5, "1:02:05.5")]
    public void Format_UsesMinutesSecondsTenths(double seconds, string expected)
    {
        Assert.Equal(expected, TimeFormat.Format(seconds));
    }

    [Fact]
    public void Policy_ReadsDisableFlags()
    {
        var values = new Dictionary<string, int?> { [RecordingPolicy.DisableTypedTextCaptureValue] = 1 };

        var policy = RecordingPolicy.FromValues(name => values.GetValueOrDefault(name));

        Assert.True(policy.DisableTypedTextCapture);
        Assert.False(policy.DisableVideoRecording);
    }

    [Fact]
    public void StepTitle_IncludesTypedText()
    {
        var generator = new StepTitleGenerator();
        var step = new RecordedStep { ActionType = StepActionType.TextEntry, TypedText = "invoice 1042", InputTargetName = "Search" };

        Assert.Equal("Type 'invoice 1042' into the 'Search' field", generator.Generate(step));
    }

    [Fact]
    public void StepTitle_HidesSensitiveTypedText()
    {
        var generator = new StepTitleGenerator();
        var step = new RecordedStep { ActionType = StepActionType.TextEntry, TypedText = "hunter2", IsSensitiveInput = true };

        Assert.Equal("Enter hidden text", generator.Generate(step));
    }

    [Fact]
    public async Task SessionStore_RoundTripsSegmentsAndInputEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), "OpenStepsTests", Guid.NewGuid().ToString("N"));
        var store = new SessionStore(root);
        var session = new RecordingSession { Title = "Video session" };
        var sessionDirectory = Path.Combine(root, session.Id.ToString("N"));
        var video = Path.Combine(sessionDirectory, "videos", "recording-1.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(video)!);
        await File.WriteAllBytesAsync(video, [0, 0, 0, 0]);

        session.Segments.Add(new RecordingSegment { Number = 1, VideoPath = video, DurationSeconds = 12.5, TypedTextCaptured = true });
        session.InputEvents.Add(new InputEvent { Segment = 1, OffsetSeconds = 2.5, Kind = InputEventKind.Shortcut, Key = "Ctrl+F" });
        session.Environment = new EnvironmentInfo { OperatingSystem = "Windows 11" };
        session.Steps.Add(new RecordedStep { GeneratedTitle = "Click", VideoSegment = 1, VideoOffsetSeconds = 1.25 });

        await store.SaveSessionAsync(session);
        var loaded = await store.LoadSessionAsync(session.Id);

        Assert.NotNull(loaded);
        var segment = Assert.Single(loaded.Segments);
        Assert.Equal("videos/recording-1.mp4", segment.VideoRelativePath);
        Assert.True(segment.HasVideo);
        Assert.Equal("Ctrl+F", Assert.Single(loaded.InputEvents).Key);
        Assert.Equal("Windows 11", loaded.Environment?.OperatingSystem);
        Assert.Equal(1.25, loaded.Steps[0].VideoOffsetSeconds);
    }
}
