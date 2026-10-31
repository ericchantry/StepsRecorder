using System.ComponentModel;
using System.IO;
using OpenSteps.Capture;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;
using WinForms = System.Windows.Forms;

namespace OpenSteps.App;

/// <summary>
/// Full-session video and the timestamped keyboard/mouse timeline that sits alongside it.
/// </summary>
public partial class MainWindow
{
    private readonly FocusedInputInspector _focusInspector = new();
    private readonly EnvironmentInfoCollector _environmentCollector = new();
    private readonly TypedTextBuffer _pendingTypedText = new();
    private RecordingClock _clock = new();
    private ScreenVideoRecorder? _videoRecorder;
    private RecordingSegment? _currentSegment;
    private RecordingPolicy _policy = RecordingPolicy.None;
    private WinForms.ToolStripMenuItem? _trayTypedTextItem;
    private RecordingReviewWindow? _reviewWindow;

    public bool RecordVideo
    {
        get => _settings.RecordVideo && !_policy.DisableVideoRecording;
        set
        {
            if (_policy.DisableVideoRecording || _settings.RecordVideo == value)
            {
                return;
            }

            _settings.RecordVideo = value;
            RaiseRecordingOptionsChanged();
            if (!_loadingSettings)
            {
                _ = SaveSettingsAsync();
            }
        }
    }

    public bool CaptureTypedText
    {
        get => _settings.CaptureTypedText && !_policy.DisableTypedTextCapture;
        set
        {
            if (_policy.DisableTypedTextCapture || _settings.CaptureTypedText == value)
            {
                return;
            }

            _settings.CaptureTypedText = value;
            if (value && _currentSegment is not null)
            {
                _currentSegment.TypedTextCaptured = true;
            }

            RaiseRecordingOptionsChanged();
            if (!_loadingSettings)
            {
                _ = SaveSettingsAsync();
            }
        }
    }

    public bool IsVideoOptionEnabled => !_policy.DisableVideoRecording && !_isRecording;

    public bool IsTypedTextOptionEnabled => !_policy.DisableTypedTextCapture;

    public string VideoOptionToolTip => _policy.DisableVideoRecording
        ? "Video recording has been turned off by your IT department."
        : "Record a video of the whole session (all monitors). Every click and keystroke links to its moment in the video.";

    public string TypedTextOptionToolTip => _policy.DisableTypedTextCapture
        ? "Recording typed text has been turned off by your IT department. Key names and shortcuts are still recorded."
        : "Record the characters you type. Password and other sensitive fields are always masked. Can be switched off from the tray icon while recording.";

    private bool IsTimelineActive => _isRecording && !_isPaused && _currentSegment is not null && _screenshotCaptureTargetStep is null;

    internal RecordingSession CurrentSession => _session;

    private void RaiseRecordingOptionsChanged()
    {
        foreach (var name in new[]
                 {
                     nameof(RecordVideo), nameof(CaptureTypedText), nameof(IsVideoOptionEnabled),
                     nameof(IsTypedTextOptionEnabled), nameof(VideoOptionToolTip), nameof(TypedTextOptionToolTip)
                 })
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        if (_trayTypedTextItem is not null)
        {
            _trayTypedTextItem.Checked = CaptureTypedText;
            _trayTypedTextItem.Enabled = IsTypedTextOptionEnabled;
        }
    }

    private void RefreshPolicy()
    {
        _policy = RecordingPolicyReader.Read();
        RaiseRecordingOptionsChanged();
    }

    /// <summary>Starts a new recording segment: environment snapshot, video encoder, then the shared clock.</summary>
    private async Task BeginTimelineSegmentAsync()
    {
        RefreshPolicy();
        RememberStepsBeforeRecording();
        try
        {
            _session.Environment = _environmentCollector.Collect();
        }
        catch
        {
            // Environment details are best effort.
        }

        var segment = new RecordingSegment
        {
            Number = _session.Segments.Count == 0 ? 1 : _session.Segments.Max(s => s.Number) + 1,
            StartedAt = DateTimeOffset.Now,
            TypedTextCaptured = CaptureTypedText
        };
        _session.Segments.Add(segment);
        _currentSegment = segment;
        _clock = new RecordingClock();

        var captureTarget = CreateVideoCaptureTarget();
        segment.CaptureArea = captureTarget.Description;
        if (RecordVideo)
        {
            var path = Path.Combine(GetSessionDirectory(), "videos", $"recording-{segment.Number}.mp4");
            var recorder = new ScreenVideoRecorder(_clock, _settings.VideoFramesPerSecond, captureTarget);
            try
            {
                RecordingStatusText.Text = "Starting video…";
                await recorder.StartAsync(path);
                _videoRecorder = recorder;
                segment.VideoPath = path;
            }
            catch (Exception ex)
            {
                segment.VideoError = ex.Message;
                await recorder.DisposeAsync();
            }
        }

        _clock.Start();
        RaiseRecordingOptionsChanged();
    }

    /// <summary>Stops the clock and finalises the video file for the current segment.</summary>
    private async Task EndTimelineSegmentAsync()
    {
        if (_currentSegment is not { } segment)
        {
            return;
        }

        _clock.Stop();
        segment.DurationSeconds = Math.Round(_clock.Elapsed.TotalSeconds, 2);
        if (_videoRecorder is { } recorder)
        {
            _videoRecorder = null;
            var result = await recorder.StopAsync();
            if (!result.Succeeded)
            {
                segment.VideoError = result.Error ?? "The video could not be saved.";
            }

            if (!File.Exists(result.Path))
            {
                segment.VideoPath = null;
            }
        }

        _currentSegment = null;
        RaiseRecordingOptionsChanged();

        if (segment.VideoError is { } error)
        {
            WinForms.MessageBox.Show(
                $"The steps and input timeline were saved, but the video could not be recorded:\n{error}",
                "OpenSteps",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Warning);
        }
    }

    /// <summary>Throws away the current segment: stops the encoder, deletes its video and its input events.</summary>
    private async Task DiscardTimelineSegmentAsync()
    {
        if (_currentSegment is not { } segment)
        {
            return;
        }

        _clock.Stop();
        if (_videoRecorder is { } recorder)
        {
            _videoRecorder = null;
            await recorder.StopAsync();
        }

        TryDeleteFile(segment.VideoPath);
        _session.Segments.Remove(segment);
        _session.InputEvents.RemoveAll(inputEvent => inputEvent.Segment == segment.Number);
        _currentSegment = null;
        RaiseRecordingOptionsChanged();
    }

    /// <summary>Finalises an in-progress video synchronously so the MP4 is not left unreadable when the app closes.</summary>
    private void StopVideoForShutdown()
    {
        if (_videoRecorder is not { } recorder)
        {
            return;
        }

        _videoRecorder = null;
        _clock.Stop();
        try
        {
            recorder.StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Shutdown must not be blocked.
        }
    }

    private void SyncClockWithPause()
    {
        if (_isPaused)
        {
            _clock.Pause();
        }
        else
        {
            _clock.Resume();
        }
    }

    private double? OffsetAt(DateTimeOffset timestamp)
    {
        return _currentSegment is null ? null : _clock.ElapsedAt(timestamp).TotalSeconds;
    }

    private void StampStep(RecordedStep step, double? offset)
    {
        if (_currentSegment is not { } segment || offset is not { } value || _screenshotCaptureTargetStep is not null)
        {
            return;
        }

        step.VideoSegment = segment.Number;
        step.VideoOffsetSeconds = Math.Round(value, 2);
    }

    private void AddInputEvent(InputEvent inputEvent, double? offset, IntPtr window)
    {
        if (!IsTimelineActive || offset is not { } value)
        {
            return;
        }

        var (title, process) = _activeWindowService.DescribeWindow(window);
        inputEvent.Segment = _currentSegment!.Number;
        inputEvent.OffsetSeconds = Math.Round(value, 3);
        inputEvent.Timestamp = DateTimeOffset.Now;
        inputEvent.WindowTitle = title;
        inputEvent.ProcessName = process;
        _session.InputEvents.Add(inputEvent);
    }

    private void AddKeyboardEvent(InputEventKind kind, string? key, string? text, bool masked, double? offset)
    {
        AddInputEvent(new InputEvent { Kind = kind, Key = key, Text = masked ? null : text, Masked = masked }, offset, _activeWindowService.GetForegroundWindowHandle());
    }

    private void AddClickEvent(StepActionType actionType, int x, int y, double? offset, IntPtr rootWindow)
    {
        var kind = actionType switch
        {
            StepActionType.RightClick => InputEventKind.RightClick,
            StepActionType.DoubleClick => InputEventKind.DoubleClick,
            _ => InputEventKind.Click
        };

        AddInputEvent(new InputEvent { Kind = kind, X = x, Y = y }, offset, rootWindow);
    }

    private void MouseHook_ButtonPressed(int x, int y)
    {
        if (IsTimelineActive
            && _clickTargetClassifier.ClassifyPoint(x, y, GetOpenStepsWindowHandles()).Classification != ClickClassification.OpenStepsWindow)
        {
            _videoRecorder?.MarkClick(x, y);
            _focusInspector.Invalidate();
        }
    }

    private void MouseHook_PointerActivityCaptured(object? sender, PointerActivityEventArgs e)
    {
        if (!IsTimelineActive)
        {
            return;
        }

        var offset = OffsetAt(e.Timestamp);
        Dispatcher.BeginInvoke(() =>
        {
            if (IsOutsideCaptureMonitor(e.X, e.Y))
            {
                return;
            }

            var target = _clickTargetClassifier.ClassifyPoint(e.X, e.Y, GetOpenStepsWindowHandles());
            if (target.Classification == ClickClassification.OpenStepsWindow)
            {
                return;
            }

            var window = target.RootHwnd != IntPtr.Zero ? target.RootHwnd : _activeWindowService.GetRootWindowAt(e.X, e.Y);
            AddInputEvent(
                new InputEvent { Kind = e.Kind, X = e.X, Y = e.Y, Delta = e.Delta, EndX = e.EndX, EndY = e.EndY },
                offset,
                window);
        });
    }

    private void ToggleTypedTextFromTray()
    {
        Dispatcher.Invoke(() => CaptureTypedText = !CaptureTypedText);
    }

    private string GetSessionDirectory()
    {
        var sessionDirectory = string.IsNullOrWhiteSpace(_session.SessionDirectory)
            ? _session.OutputDirectory
            : _session.SessionDirectory;
        Directory.CreateDirectory(sessionDirectory);
        return sessionDirectory;
    }

    internal void OpenRecordingReviewFromEditor(RecordedStep? step)
    {
        SyncSessionOrder(refreshStepViews: false);
        if (_session.Segments.Count == 0 && _session.InputEvents.Count == 0)
        {
            WinForms.MessageBox.Show(
                "This guide has no video or input timeline yet. Record steps with OpenSteps to capture them.",
                "OpenSteps",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Information);
            return;
        }

        if (_reviewWindow is null || !_reviewWindow.IsLoaded)
        {
            _reviewWindow = new RecordingReviewWindow(_session) { Owner = _editorWindow };
            _reviewWindow.Closed += (_, _) => _reviewWindow = null;
            _reviewWindow.Show();
        }
        else
        {
            _reviewWindow.Activate();
        }

        if (step is { VideoSegment: { } segment, VideoOffsetSeconds: { } offset })
        {
            _reviewWindow.SeekTo(segment, offset);
        }
    }
}
