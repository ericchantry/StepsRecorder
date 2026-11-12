using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;
using WinForms = System.Windows.Forms;

namespace OpenSteps.App;

/// <summary>
/// Keeping work safe: answering the single-instance / installer control pipe, saving during a recording so a crash
/// loses at most the last moment, recovering interrupted recordings on the next start, and the theme choice.
/// </summary>
public partial class MainWindow
{
    private DispatcherTimer? _recordingAutosaveTimer;

    // ---- Control pipe: second launch and installer -------------------------------------------------------------

    /// <summary>Handles one command from another process (see <see cref="AppControl"/>) and returns the reply.</summary>
    internal Task<string> HandleControlCommandAsync(string command)
    {
        var (reply, act) = AppControl.Decide(command, GetActivity());
        if (act)
        {
            if (string.Equals(command.Trim(), AppControl.Activate, StringComparison.OrdinalIgnoreCase))
            {
                BringToFront();
            }
            else
            {
                // Reply first, then close, so the caller hears "ok" before the pipe goes away.
                Dispatcher.BeginInvoke(DispatcherPriority.Background, async () => await QuitGracefullyAsync());
            }
        }

        return Task.FromResult(reply);
    }

    private AppActivity GetActivity()
    {
        if (_isRecording)
        {
            return AppActivity.Recording;
        }

        var dialogOpen = _pickingWindow
            || ComponentDispatcher.IsThreadModal
            || System.Windows.Application.Current.Windows.OfType<StepEditWindow>().Any();
        return dialogOpen ? AppActivity.Busy : AppActivity.Idle;
    }

    private void BringToFront()
    {
        if (_isRecording)
        {
            // The recording bar is already on screen; don't put the recorder into its own recording.
            return;
        }

        Window target = _editorWindow is { IsVisible: true } editor ? editor : this;
        if (!target.IsVisible)
        {
            target.Show();
        }

        if (target.WindowState == WindowState.Minimized)
        {
            target.WindowState = WindowState.Normal;
        }

        target.Activate();
        target.Topmost = true;
        target.Topmost = target == this;
    }

    private async Task QuitGracefullyAsync()
    {
        try
        {
            if (Steps.Count > 0)
            {
                await SaveSessionAsync(showMessage: false, refreshStepViews: false);
            }
        }
        catch
        {
            // Closing for an upgrade must not be blocked by a save problem; the editor autosaves as you type.
        }

        System.Windows.Application.Current.Shutdown();
    }

    // ---- Saving while recording --------------------------------------------------------------------------------

    private void InitializeReliability()
    {
        _recordingAutosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _recordingAutosaveTimer.Tick += async (_, _) =>
        {
            _recordingAutosaveTimer.Stop();
            if (!_isRecording || Steps.Count == 0)
            {
                return;
            }

            try
            {
                await SaveSessionAsync(showMessage: false, refreshStepViews: false);
            }
            catch
            {
                // Tried again after the next step.
            }
        };
        Steps.CollectionChanged += (_, e) =>
        {
            if (_isRecording && e.Action != NotifyCollectionChangedAction.Reset)
            {
                _recordingAutosaveTimer.Stop();
                _recordingAutosaveTimer.Start();
            }
        };
    }

    /// <summary>Marks the session folder as "being recorded" so an interrupted recording can be found on the next start.</summary>
    private void WriteRecordingMarker(RecordingSegment segment, string? videoPath)
    {
        try
        {
            var directory = GetSessionDirectory();
            var relativeVideo = videoPath is null ? null : Path.GetRelativePath(directory, videoPath).Replace('\\', '/');
            RecordingRecovery.WriteMarker(directory, new RecordingMarker(_session.Id, Environment.ProcessId, DateTimeOffset.Now, segment.Number, relativeVideo));
        }
        catch
        {
            // Recovery is a safety net; recording goes ahead without it.
        }
    }

    private void ClearRecordingMarker()
    {
        var directory = string.IsNullOrWhiteSpace(_session.SessionDirectory) ? _session.OutputDirectory : _session.SessionDirectory;
        RecordingRecovery.ClearMarker(directory);
    }

    /// <summary>
    /// Called when something unexpected goes wrong on the UI thread. Ends any recording the normal way (which closes
    /// the video so it stays playable) and saves. Returns true when a recording was stopped and saved.
    /// </summary>
    internal async Task<bool> EmergencySaveAsync()
    {
        if (_isRecording)
        {
            try
            {
                await StopRecordingAsync();
                return true;
            }
            catch
            {
                _isRecording = false;
                StopHooks();
                StopVideoForShutdown();
                HideRecordingTray();
            }
        }

        if (Steps.Count > 0)
        {
            await SaveSessionAsync(showMessage: false, refreshStepViews: false);
        }

        return false;
    }

    /// <summary>Last-moment save when the process is about to die. May run on any thread, so it only writes the file.</summary>
    internal void SaveSessionBeforeCrash()
    {
        if (_session.Steps.Count > 0)
        {
            Task.Run(() => _sessionStore.SaveAsync(_session)).Wait(TimeSpan.FromSeconds(3));
        }
    }

    // ---- Recovery on start -------------------------------------------------------------------------------------

    private async Task RecoverInterruptedRecordingsAsync()
    {
        try
        {
            // This is the only running copy, so any marker not written by this process belongs to a recording that never ended.
            var interrupted = await RecordingRecovery.FindInterruptedAsync(_sessionStore, processId => processId == Environment.ProcessId);
            RecordingSession? recovered = null;
            foreach (var recording in interrupted)
            {
                recovered = await RecordingRecovery.RepairAsync(_sessionStore, recording) ?? recovered;
            }

            if (recovered is null)
            {
                return;
            }

            var steps = recovered.Steps.Count == 1 ? "1 step" : $"{recovered.Steps.Count} steps";
            var answer = System.Windows.MessageBox.Show(
                this,
                $"OpenSteps closed unexpectedly while recording \"{recovered.Title}\".\n\n"
                + $"Its {steps} and screenshots were recovered. The video for the interrupted part could not be saved.\n\n"
                + "Open the recovered guide now?",
                "OpenSteps",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
            {
                await OpenSavedSessionAsync(recovered.Id);
            }
        }
        catch (Exception ex)
        {
            App.WriteCrashLog(ex, "recovery");
        }
    }

    // ---- Library: rename and tags ------------------------------------------------------------------------------

    internal async Task SetSavedSessionTagsAsync(Guid sessionId, IEnumerable<string> tags)
    {
        var parsed = SessionLibrary.ParseTags(SessionLibrary.FormatTags(tags));
        if (_session.Id == sessionId)
        {
            _session.Tags = parsed;
        }

        await _sessionStore.SetSessionTagsAsync(sessionId, parsed);
    }

    internal async Task RenameSavedSessionFromLibraryAsync(Guid sessionId, string title)
    {
        await _sessionStore.RenameSessionAsync(sessionId, title);
        if (_session.Id == sessionId)
        {
            _session.Title = string.IsNullOrWhiteSpace(title) ? _session.Title : title.Trim();
            SetEditorTitle(_session.Title);
        }
    }

    // ---- Theme -------------------------------------------------------------------------------------------------

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { ContextMenu: { } menu } button)
        {
            return;
        }

        foreach (var item in menu.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            item.IsChecked = item.Tag is string tag && Enum.TryParse<AppTheme>(tag, out var theme) && theme == ThemeManager.Chosen;
        }

        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    private void ThemeChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string tag } && Enum.TryParse<AppTheme>(tag, out var theme))
        {
            _settings.Theme = theme;
            ThemeManager.SetTheme(theme);
            _ = SaveSettingsAsync();
        }
    }
}
