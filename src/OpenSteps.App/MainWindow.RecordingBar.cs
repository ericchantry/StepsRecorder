using System.IO;
using System.Windows;
using OpenSteps.Core.Models;

namespace OpenSteps.App;

/// <summary>The on-screen Pause / Stop / Cancel bar shown while recording.</summary>
public partial class MainWindow
{
    private RecordingBarWindow? _recordingBar;
    private HashSet<Guid> _stepIdsBeforeRecording = [];
    private bool _sessionWasEmptyBeforeRecording;

    internal void TogglePauseFromRecordingBar()
    {
        if (!_isRecording || _screenshotCaptureTargetStep is not null)
        {
            return;
        }

        _isPaused = !_isPaused;
        SyncClockWithPause();
        RefreshRecordingStatus();
    }

    internal async Task StopRecordingFromRecordingBarAsync()
    {
        if (_isRecording)
        {
            await StopRecordingAsync();
        }
    }

    /// <summary>Stops recording and throws away everything captured since Record was pressed, after confirming.</summary>
    internal async Task CancelRecordingFromRecordingBarAsync()
    {
        if (!_isRecording)
        {
            return;
        }

        if (_screenshotCaptureTargetStep is not null)
        {
            // Waiting for a click to re-capture one step's screenshot: just leave that mode.
            _screenshotCaptureTargetStep = null;
            _isRecording = false;
            _isPaused = false;
            _recordingStatusTimer.Stop();
            StopHooks();
            HideRecordingTray();
            ShowSessionEditor();
            return;
        }

        var wasPaused = _isPaused;
        if (!wasPaused)
        {
            _isPaused = true;
            SyncClockWithPause();
            RefreshRecordingStatus();
        }

        var answer = System.Windows.MessageBox.Show(
            _recordingBar!,
            "Discard this recording?\n\nThe steps, keystrokes and video captured since you pressed Record will be deleted. This cannot be undone.",
            "OpenSteps",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            if (!wasPaused && _isRecording)
            {
                _isPaused = false;
                SyncClockWithPause();
                RefreshRecordingStatus();
            }

            return;
        }

        _isRecording = false;
        _isPaused = false;
        _typingTimer.Stop();
        _pendingTypingStep = null;
        _pendingTypingKeyCount = 0;
        _pendingTypedText.Clear();
        _recordingStatusTimer.Stop();
        StopHooks();
        HideRecordingTray();
        await DiscardTimelineSegmentAsync();
        await RemoveStepsAddedDuringRecordingAsync();

        if (_sessionWasEmptyBeforeRecording && Steps.Count == 0)
        {
            DeleteSessionFolder(GetSessionDirectory());
            ResetSession();
            Show();
            Activate();
            RefreshRecordingStatus();
            return;
        }

        ShowSessionEditor();
        await SaveSessionAsync(showMessage: false);
    }

    private void ShowRecordingBar()
    {
        _recordingBar ??= new RecordingBarWindow(this);
        RefreshRecordingStatus();
        _recordingBar.ShowBar();
    }

    private void HideRecordingBar()
    {
        _recordingBar?.Hide();
    }

    private void UpdateRecordingBar(TimeSpan elapsed)
    {
        _recordingBar?.UpdateStatus(_isPaused, elapsed, Steps.Count, canPause: _screenshotCaptureTargetStep is null);
    }

    private void RememberStepsBeforeRecording()
    {
        _stepIdsBeforeRecording = Steps.Select(step => step.Id).ToHashSet();
        _sessionWasEmptyBeforeRecording = Steps.Count == 0;
    }

    private async Task RemoveStepsAddedDuringRecordingAsync()
    {
        await _captureLock.WaitAsync();
        try
        {
            var added = Steps.Where(step => !_stepIdsBeforeRecording.Contains(step.Id)).ToList();
            foreach (var step in added)
            {
                Steps.Remove(step);
            }

            // Keyboard steps can reuse an earlier step's screenshot, so only delete files nothing else uses.
            var stillUsed = Steps
                .SelectMany(step => new[] { step.ScreenshotPath, step.EditedScreenshotPath })
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sessionDirectory = Path.GetFullPath(GetSessionDirectory());
            foreach (var path in added.SelectMany(step => new[] { step.ScreenshotPath, step.EditedScreenshotPath }))
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(path);
                if (!stillUsed.Contains(fullPath) && fullPath.StartsWith(sessionDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(fullPath);
                }
            }

            RenumberSteps();
        }
        finally
        {
            _captureLock.Release();
        }
    }

    private void DeleteSessionFolder(string sessionDirectory)
    {
        try
        {
            if (sessionDirectory.StartsWith(_sessionStore.RootDirectory, StringComparison.OrdinalIgnoreCase) && Directory.Exists(sessionDirectory))
            {
                Directory.Delete(sessionDirectory, recursive: true);
            }
        }
        catch
        {
            // A leftover empty folder is harmless.
        }
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort; the session no longer references the file.
        }
    }
}
