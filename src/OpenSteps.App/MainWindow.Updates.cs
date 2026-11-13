using System.Windows;
using System.Windows.Threading;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.App;

/// <summary>
/// Keeping OpenSteps current: once a day it looks at the project's releases page, and when a newer version is
/// there it shows what changed and offers to install it. Nothing is downloaded or installed without the user
/// saying so, a recording is never interrupted, and IT can switch the whole thing off by policy.
/// </summary>
public partial class MainWindow
{
    private readonly UpdateService _updateService = new();
    private readonly UpdateAttemptMarker _updateAttempt = new();
    private DispatcherTimer? _updateCheckTimer;
    private UpdateCheckResult? _knownUpdate;
    private bool _updateCheckRunning;
    private bool _updateWindowOpen;

    /// <summary>The version of OpenSteps that is running.</summary>
    internal static ReleaseVersion CurrentVersion =>
        ReleaseVersion.FromAssembly(typeof(MainWindow).Assembly.GetName().Version);

    /// <summary>True when the user and IT both allow OpenSteps to look for new versions.</summary>
    private bool AutomaticUpdateChecksAllowed => _settings.CheckForUpdates && !_policy.DisableUpdateChecks;

    // ---- The button on the recorder bar ------------------------------------------------------------------------

    public string UpdateGlyph => _knownUpdate?.HasUpdate == true ? "" : "";

    public string UpdateToolTip
    {
        get
        {
            if (_knownUpdate?.Newest is { } newest && _knownUpdate.HasUpdate)
            {
                return $"OpenSteps {newest.Version} is available. Click to see what changed and install it.";
            }

            if (_policy.DisableUpdateChecks)
            {
                return "Your IT department looks after OpenSteps updates. Click to see which version you are running.";
            }

            return $"OpenSteps {CurrentVersion}. Click to check for a new version.";
        }
    }

    /// <summary>An update waiting to be installed colours the button, so it is noticed without a popup.</summary>
    public bool UpdateAvailable => _knownUpdate?.HasUpdate == true;

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        // The user asked, so say what happened either way, and ignore a version they skipped earlier.
        if (_knownUpdate?.HasUpdate == true)
        {
            ShowUpdateWindow(_knownUpdate);
            return;
        }

        await RunUpdateCheckAsync(askedByUser: true);
    }

    // ---- The daily check ---------------------------------------------------------------------------------------

    /// <summary>
    /// Called once the main window is up: reports an update that did not finish installing, then looks for a new
    /// version if a day has gone by. Starts the timer that keeps doing so while OpenSteps is left running.
    /// </summary>
    private async Task StartUpdateChecksAsync()
    {
        ReportUnfinishedUpdate();

        _updateCheckTimer ??= CreateUpdateCheckTimer();
        _updateCheckTimer.Start();
        await RunDueUpdateCheckAsync();
    }

    private DispatcherTimer CreateUpdateCheckTimer()
    {
        var timer = new DispatcherTimer { Interval = UpdateCheckSchedule.PollInterval };
        timer.Tick += async (_, _) => await RunDueUpdateCheckAsync();
        return timer;
    }

    /// <summary>Runs the daily check when it is due, nothing is in the way, and the user has it switched on.</summary>
    private async Task RunDueUpdateCheckAsync()
    {
        if (!AutomaticUpdateChecksAllowed
            || !UpdateCheckSchedule.IsDue(_settings.LastUpdateCheck, DateTimeOffset.Now))
        {
            return;
        }

        await RunUpdateCheckAsync(askedByUser: false);
    }

    /// <summary>
    /// Looks at the releases page and, when something newer is there, offers it. A check the user did not ask
    /// for stays silent unless there is an update: no "you are up to date" box, and no complaint when the
    /// machine is offline.
    /// </summary>
    private async Task RunUpdateCheckAsync(bool askedByUser)
    {
        if (_updateCheckRunning || _updateWindowOpen)
        {
            return;
        }

        // Never interrupt a recording, and never show a dialog over one.
        if (_isRecording)
        {
            return;
        }

        _updateCheckRunning = true;
        try
        {
            var result = await _updateService.CheckAsync(
                CurrentVersion,
                askedByUser ? null : _settings.SkippedUpdateVersion,
                _settings.IncludePrereleaseUpdates);

            if (result.Status != UpdateStatus.CheckFailed)
            {
                _settings.LastUpdateCheck = DateTimeOffset.Now;
                await SaveSettingsAsync();
            }

            _knownUpdate = result;
            RaiseUpdateChanged();

            if (result.HasUpdate && !_isRecording)
            {
                ShowUpdateWindow(result);
            }
            else if (askedByUser)
            {
                ShowUpdateWindow(result);
            }
        }
        finally
        {
            _updateCheckRunning = false;
        }
    }

    // ---- The window --------------------------------------------------------------------------------------------

    private void ShowUpdateWindow(UpdateCheckResult result)
    {
        if (_updateWindowOpen || _isRecording)
        {
            return;
        }

        var installation = DescribeInstallation();
        var window = new UpdateWindow(
            result,
            _updateService,
            installation,
            _settings.CheckForUpdates,
            _policy.DisableUpdateChecks)
        {
            Owner = IsVisible ? this : null
        };

        _updateWindowOpen = true;
        try
        {
            window.ShowDialog();
        }
        finally
        {
            _updateWindowOpen = false;
        }

        _ = ApplyUpdateChoiceAsync(window);
    }

    private async Task ApplyUpdateChoiceAsync(UpdateWindow window)
    {
        var changed = false;
        if (window.AutomaticChecksEnabled != _settings.CheckForUpdates && !_policy.DisableUpdateChecks)
        {
            _settings.CheckForUpdates = window.AutomaticChecksEnabled;
            changed = true;
        }

        if (window.Outcome == UpdateDialogOutcome.Skipped && window.SkippedVersion is { } skipped)
        {
            _settings.SkippedUpdateVersion = skipped;
            _knownUpdate = _knownUpdate is null ? null : _knownUpdate with { Status = UpdateStatus.Skipped };
            RaiseUpdateChanged();
            changed = true;
        }

        if (changed)
        {
            await SaveSettingsAsync();
        }

        if (window.Outcome == UpdateDialogOutcome.Installing)
        {
            // The installer is waiting for this copy to close before it can replace the files.
            await SaveAndExitAsync();
        }
    }

    /// <summary>Where this copy of OpenSteps lives and whether it is allowed to replace itself there.</summary>
    private static UpdateInstallation DescribeInstallation()
    {
        return UpdateInstallation.Describe(
            AppContext.BaseDirectory,
            System.IO.File.Exists,
            CanWriteToDirectory);
    }

    private static bool CanWriteToDirectory(string directory)
    {
        try
        {
            var probe = System.IO.Path.Combine(directory, $".opensteps-write-{Guid.NewGuid():N}");
            System.IO.File.WriteAllText(probe, string.Empty);
            System.IO.File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// If OpenSteps asked the installer to upgrade it last time and is still on the old version, the upgrade did
    /// not finish. Say so once and point at the install log rather than leaving the user wondering.
    /// </summary>
    private void ReportUnfinishedUpdate()
    {
        var attempt = _updateAttempt.Read();
        if (attempt is null)
        {
            return;
        }

        _updateAttempt.Clear();
        if (!UpdateAttemptMarker.IsFailedAttempt(attempt, CurrentVersion))
        {
            return;
        }

        System.Windows.MessageBox.Show(
            this,
            $"The update to OpenSteps {attempt.Version} did not finish, so you are still running {CurrentVersion}. "
            + "Nothing has been lost; your guides and settings are untouched.\n\n"
            + $"You can try again from the update button, or see what happened in:\n{attempt.LogPath}",
            "OpenSteps",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RaiseUpdateChanged()
    {
        foreach (var name in new[] { nameof(UpdateGlyph), nameof(UpdateToolTip), nameof(UpdateAvailable) })
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }
    }
}
