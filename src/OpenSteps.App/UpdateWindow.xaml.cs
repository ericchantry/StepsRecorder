using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace OpenSteps.App;

/// <summary>What the user decided in the update window.</summary>
public enum UpdateDialogOutcome
{
    /// <summary>Stay on this version for now; OpenSteps asks again tomorrow.</summary>
    Later,

    /// <summary>Don't mention this version again.</summary>
    Skipped,

    /// <summary>The package is downloaded and the installer has been started: OpenSteps must now save and close.</summary>
    Installing
}

/// <summary>
/// Shows what a newer OpenSteps would change and, if the user wants it, fetches that version and hands it to
/// the per-user installer. The same window reports "up to date" after a check the user asked for.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateCheckResult _result;
    private readonly UpdateService _service;
    private readonly UpdateInstallation _installation;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly bool _automaticChecksLocked;
    private bool _working;

    public UpdateWindow(
        UpdateCheckResult result,
        UpdateService service,
        UpdateInstallation installation,
        bool automaticChecksEnabled,
        bool automaticChecksLockedByPolicy = false)
    {
        _result = result;
        _service = service;
        _installation = installation;
        InitializeComponent();

        AutoCheckBox.IsChecked = automaticChecksEnabled;
        _automaticChecksLocked = automaticChecksLockedByPolicy;
        if (automaticChecksLockedByPolicy)
        {
            AutoCheckBox.IsEnabled = false;
            AutoCheckBox.ToolTip = "Your IT department has turned off update checks.";
        }

        AutomaticChecksEnabled = automaticChecksEnabled;
        Build();
    }

    public UpdateDialogOutcome Outcome { get; private set; } = UpdateDialogOutcome.Later;

    /// <summary>The state of the "check daily" box when the window closed.</summary>
    public bool AutomaticChecksEnabled { get; private set; }

    /// <summary>The version the user chose to skip, when they did.</summary>
    public string? SkippedVersion { get; private set; }

    private void Build()
    {
        if (!_result.HasUpdate)
        {
            BuildUpToDate();
            return;
        }

        var newest = _result.Newest!;
        Title = $"OpenSteps {newest.Version} is available";
        HeadlineText.Text = $"OpenSteps {newest.Version} is available";

        var size = PackageChecksum.FormatSize(newest.PackageSize);
        var sizeText = size.Length > 0 ? $" · {size} download" : string.Empty;
        var countText = _result.Changes.Count > 1
            ? $" · {_result.Changes.Count} versions of changes"
            : string.Empty;
        SubheadText.Text = $"You have {_result.CurrentVersion}.{countText}{sizeText}";

        RenderNotes(ReleaseNotes.FormatAll(_result.Changes));

        if (!_installation.CanSelfUpdate)
        {
            UpdateButton.Content = "Open releases page";
            UpdateButton.ToolTip = "Open the releases page in your browser so you (or your IT department) can install this version.";
            ShowError(_installation.Blocker ?? "This copy of OpenSteps cannot update itself.");
        }
        else
        {
            UpdateButton.ToolTip = $"Download OpenSteps {newest.Version} and install it. OpenSteps saves your work, closes, and starts again on the new version.";
        }
    }

    private void BuildUpToDate()
    {
        Title = "OpenSteps is up to date";
        HeadlineText.Text = "OpenSteps is up to date";
        SubheadText.Text = _result.Status == UpdateStatus.CheckFailed
            ? "OpenSteps could not reach its releases page just now, so there may be a newer version it has not seen."
            : $"You are running {_result.CurrentVersion}, the newest version published.";

        ChangesLabel.Visibility = Visibility.Collapsed;
        NotesScroller.Visibility = Visibility.Collapsed;
        ((Border)NotesScroller.Parent).Visibility = Visibility.Collapsed;
        SkipButton.Visibility = Visibility.Collapsed;
        LaterButton.Visibility = Visibility.Collapsed;
        UpdateButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
        CloseButton.IsDefault = true;

        if (_result.Status == UpdateStatus.CheckFailed && _result.Error is { Length: > 0 } error)
        {
            ShowError($"The check failed: {error}");
        }

        Height = 320;
        MinHeight = 280;
        SizeToContent = SizeToContent.Manual;
    }

    /// <summary>Draws the release notes as headings, bullets and paragraphs, keeping bold and code runs.</summary>
    private void RenderNotes(IReadOnlyList<ReleaseNoteLine> lines)
    {
        NotesPanel.Children.Clear();
        foreach (var line in lines)
        {
            if (line.Spans.Count == 0)
            {
                NotesPanel.Children.Add(new WpfTextBlock { Height = 8 });
                continue;
            }

            var block = new WpfTextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(line.Indent * 16, line.Kind == ReleaseNoteLineKind.Heading ? 10 : 2, 0, 2),
                Foreground = (System.Windows.Media.Brush)FindResource(
                    line.Kind == ReleaseNoteLineKind.Heading ? "TextPrimaryBrush" : "TextSecondaryBrush")
            };

            if (line.Kind == ReleaseNoteLineKind.Heading)
            {
                block.FontSize = 13.5;
                block.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                block.FontSize = 12.5;
            }

            if (line.Kind == ReleaseNoteLineKind.Bullet)
            {
                block.Inlines.Add(new Run("•  "));
            }

            foreach (var span in line.Spans)
            {
                var run = new Run(span.Text);
                if (span.Bold)
                {
                    run.FontWeight = FontWeights.SemiBold;
                    run.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimaryBrush");
                }

                if (span.Code)
                {
                    run.FontFamily = new System.Windows.Media.FontFamily("Consolas, Cascadia Mono, Courier New");
                }

                block.Inlines.Add(run);
            }

            NotesPanel.Children.Add(block);
        }
    }

    private void AutoCheck_Changed(object sender, RoutedEventArgs e)
    {
        AutomaticChecksEnabled = AutoCheckBox.IsChecked == true;
    }

    private void Skip_Click(object sender, RoutedEventArgs e)
    {
        SkippedVersion = _result.Newest?.Version.ToString();
        Outcome = UpdateDialogOutcome.Skipped;
        Close();
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        Outcome = UpdateDialogOutcome.Later;
        Close();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_result.Newest is not { } release || _working)
        {
            return;
        }

        if (!_installation.CanSelfUpdate)
        {
            OpenReleasesPage(release);
            return;
        }

        SetWorking(true);
        try
        {
            var progress = new Progress<DownloadProgress>(ReportDownload);
            ProgressText.Text = "Downloading…";
            var download = await _service.DownloadAsync(release, progress, _cancellation.Token);

            ProgressText.Text = "Starting the installer…";
            ProgressBarControl.IsIndeterminate = true;

            var installer = _service.ExtractInstaller(download.PackagePath) ?? _installation.InstallerScriptPath;
            if (installer is null)
            {
                throw new InvalidOperationException(
                    "The downloaded package does not contain the OpenSteps install script, so it has not been installed.");
            }

            StartInstaller(installer, download, release);
            Outcome = UpdateDialogOutcome.Installing;
            Close();
        }
        catch (OperationCanceledException)
        {
            SetWorking(false);
            ProgressPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SetWorking(false);
            ProgressPanel.Visibility = Visibility.Collapsed;
            ShowError(ex.Message);
            UpdateButton.Content = "Try again";
        }
    }

    /// <summary>
    /// Hands the package to the per-user install script in its own process. It waits for OpenSteps to close,
    /// swaps the installed folder and starts the new version, so nothing here needs administrator rights.
    /// </summary>
    private void StartInstaller(string installerScriptPath, UpdateDownload download, ReleaseInfo release)
    {
        var arguments = UpdateInstallation.BuildInstallerArguments(
            installerScriptPath,
            download.PackagePath,
            _installation.InstallDirectory,
            download.Sha256);

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(installerScriptPath) ?? Environment.CurrentDirectory
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not start the installer.");

        // So the next start can say the update did not finish, if it doesn't.
        new UpdateAttemptMarker().Write(
            release.Version,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenSteps",
                "install.log"));
        process.Dispose();
    }

    private void OpenReleasesPage(ReleaseInfo release)
    {
        try
        {
            Process.Start(new ProcessStartInfo(release.PageUrl(_service.Repository)) { UseShellExecute = true })?.Dispose();
            Outcome = UpdateDialogOutcome.Later;
            Close();
        }
        catch (Exception ex)
        {
            ShowError($"The releases page could not be opened: {ex.Message}");
        }
    }

    private void ReportDownload(DownloadProgress progress)
    {
        var received = PackageChecksum.FormatSize(progress.BytesReceived);
        if (progress.Fraction is { } fraction)
        {
            ProgressBarControl.IsIndeterminate = false;
            ProgressBarControl.Value = fraction;
            ProgressText.Text = $"Downloading… {received} of {PackageChecksum.FormatSize(progress.TotalBytes!.Value)}";
        }
        else
        {
            ProgressBarControl.IsIndeterminate = true;
            ProgressText.Text = $"Downloading… {received}";
        }
    }

    private void SetWorking(bool working)
    {
        _working = working;
        ProgressPanel.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        UpdateButton.IsEnabled = !working;
        SkipButton.IsEnabled = !working;
        AutoCheckBox.IsEnabled = !working && !_automaticChecksLocked;
        LaterButton.Content = working ? "Cancel" : "Later";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_working && Outcome != UpdateDialogOutcome.Installing)
        {
            // "Later" during a download means cancel the download, not close the window.
            _cancellation.Cancel();
            e.Cancel = true;
            return;
        }

        _cancellation.Dispose();
    }
}
