using System.IO;
using System.Windows;
using System.Windows.Threading;
using OpenSteps.Capture;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.App;

/// <summary>
/// Keeping work safe and the app well-behaved: the keyboard hook exists only while it is needed, a recording is
/// saved as it goes and can be recovered after a crash, another launch brings this one to the front, and the
/// installer can ask the app to save and close.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan RecordingAutosaveInterval = TimeSpan.FromSeconds(5);

    private readonly RecordingMarker _recordingMarker = new();
    private DispatcherTimer? _recordingAutosaveTimer;
    private int _autosavedStepCount = -1;
    private int _autosavedEventCount = -1;
    private bool _exiting;
    private bool _crashed;

    // ---- Keyboard hook -----------------------------------------------------------------------------------------

    /// <summary>
    /// Installs the system-wide keyboard hook when keyboard recording is on and a recording is running, and removes
    /// it otherwise (keyboard recording off, IT policy, paused, stopped). With it removed, no keystroke reaches
    /// OpenSteps at all.
    /// </summary>
    private void SyncKeyboardHook()
    {
        var wanted = KeyboardHookRules.ShouldInstall(
            _isRecording,
            _isPaused,
            _settings.CaptureTypedText,
            _policy.DisableTypedTextCapture,
            _screenshotCaptureTargetStep is not null);
        if (wanted == (_keyboardHook is not null))
        {
            return;
        }

        if (wanted)
        {
            var hook = new GlobalKeyboardHook(ShouldIgnoreKeyboard, () => CaptureTypedText && _screenshotCaptureTargetStep is null);
            hook.KeyboardInputCaptured += KeyboardHook_KeyboardInputCaptured;
            hook.Start();
            _keyboardHook = hook;
        }
        else
        {
            RemoveKeyboardHook();
        }
    }

    private void RemoveKeyboardHook()
    {
        try
        {
            if (_keyboardHook is not null)
            {
                _keyboardHook.KeyboardInputCaptured -= KeyboardHook_KeyboardInputCaptured;
                _keyboardHook.Dispose();
            }
        }
        finally
        {
            _keyboardHook = null;
        }
    }

    // ---- Saving while recording, and recovery ------------------------------------------------------------------

    /// <summary>Called when a recording starts: marks it as in progress and saves the guide every few seconds.</summary>
    private void BeginRecordingSafetyNet()
    {
        if (_screenshotCaptureTargetStep is not null)
        {
            return;
        }

        _recordingMarker.Write(_session.Id, Environment.ProcessId);
        (System.Windows.Application.Current as App)?.Instance?.SetRecording(true);
        _autosavedStepCount = -1;
        _autosavedEventCount = -1;
        _recordingAutosaveTimer ??= CreateRecordingAutosaveTimer();
        _recordingAutosaveTimer.Start();
    }

    /// <summary>Called whenever a recording ends, however it ends.</summary>
    private void EndRecordingSafetyNet()
    {
        _recordingAutosaveTimer?.Stop();
        _recordingMarker.Clear();
        (System.Windows.Application.Current as App)?.Instance?.SetRecording(false);
    }

    private DispatcherTimer CreateRecordingAutosaveTimer()
    {
        var timer = new DispatcherTimer { Interval = RecordingAutosaveInterval };
        timer.Tick += async (_, _) => await AutosaveWhileRecordingAsync();
        return timer;
    }

    private async Task AutosaveWhileRecordingAsync()
    {
        if (!_isRecording || (Steps.Count == _autosavedStepCount && _session.InputEvents.Count == _autosavedEventCount))
        {
            return;
        }

        await _saveLock.WaitAsync();
        try
        {
            if (!_isRecording)
            {
                return;
            }

            SyncSessionOrder(refreshStepViews: false);
            await _sessionStore.SaveAsync(_session);
            _autosavedStepCount = Steps.Count;
            _autosavedEventCount = _session.InputEvents.Count;
        }
        catch
        {
            // Autosave is a safety net; the next tick tries again.
        }
        finally
        {
            _saveLock.Release();
        }
    }

    /// <summary>
    /// At start-up: if the last run ended in the middle of a recording, offers to open what was saved of it.
    /// </summary>
    private async Task OfferRecoveryAsync()
    {
        var interrupted = _recordingMarker.Read();
        if (interrupted is null)
        {
            return;
        }

        _recordingMarker.Clear();
        var session = await _sessionStore.LoadSessionAsync(interrupted.SessionId);
        if (session is null || session.Steps.Count == 0)
        {
            return;
        }

        var steps = session.Steps.Count == 1 ? "1 step was" : $"{session.Steps.Count} steps were";
        var answer = System.Windows.MessageBox.Show(
            this,
            $"OpenSteps closed unexpectedly while recording \"{session.Title}\".\n\n{steps} saved. Open the recovered guide now?\n\nIf you choose No, it stays in your saved guides.",
            "OpenSteps",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information,
            MessageBoxResult.Yes);

        var changed = RecordingRecovery.MarkInterruptedSegments(session);
        if (changed)
        {
            await _sessionStore.SaveAsync(session);
        }

        if (answer == MessageBoxResult.Yes)
        {
            LoadSession(session);
            ShowSessionEditor();
        }
    }

    // ---- Requests from outside (second launch, installer, crash handler) ---------------------------------------

    /// <summary>A second copy of OpenSteps was started: show this one instead.</summary>
    internal void BringToFrontFromAnotherLaunch()
    {
        if (_isRecording)
        {
            // The recording bar is already on screen; don't disturb the recording.
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

    /// <summary>The installer asked OpenSteps to close: finish and save any recording, save the guide, then exit.</summary>
    internal async Task SaveAndExitAsync()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        try
        {
            if (_isRecording)
            {
                await StopRecordingAsync();
            }
            else if (Steps.Count > 0)
            {
                await SaveSessionAsync(showMessage: false);
            }
        }
        catch
        {
            // Exit anyway; whatever was autosaved is still on disk.
        }

        System.Windows.Application.Current.Shutdown();
    }

    /// <summary>Last-resort save from the crash handler. Returns true when the guide was written to disk.</summary>
    internal bool EmergencySave()
    {
        _crashed = true;
        try
        {
            _recordingAutosaveTimer?.Stop();
            StopHooks();
            StopVideoForShutdown();
            if (Steps.Count == 0)
            {
                return false;
            }

            SyncSessionOrder(refreshStepViews: false);
            var session = _session;
            return Task.Run(() => _sessionStore.SaveAsync(session)).Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            return false;
        }
    }

    // ---- Theme -------------------------------------------------------------------------------------------------

    public AppThemePreference ThemePreference
    {
        get => _settings.Theme;
        set
        {
            if (_settings.Theme == value)
            {
                return;
            }

            _settings.Theme = value;
            ThemeManager.Apply(value);
            RaiseThemeChanged();
            if (!_loadingSettings)
            {
                _ = SaveSettingsAsync();
            }
        }
    }

    /// <summary>Icon for the theme button: a monitor for "match Windows", a sun for light, a moon for dark.</summary>
    public string ThemeGlyph => _settings.Theme switch
    {
        AppThemePreference.Light => "",
        AppThemePreference.Dark => "",
        _ => ""
    };

    public string ThemeToolTip => _settings.Theme switch
    {
        AppThemePreference.Light => "Theme: Light. Click to switch to Dark.",
        AppThemePreference.Dark => "Theme: Dark. Click to match Windows.",
        _ => "Theme: Match Windows. Click to switch to Light."
    };

    private void CycleTheme_Click(object sender, RoutedEventArgs e)
    {
        ThemePreference = _settings.Theme switch
        {
            AppThemePreference.System => AppThemePreference.Light,
            AppThemePreference.Light => AppThemePreference.Dark,
            _ => AppThemePreference.System
        };
    }

    private void RaiseThemeChanged()
    {
        foreach (var name in new[] { nameof(ThemePreference), nameof(ThemeGlyph), nameof(ThemeToolTip) })
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }
}
