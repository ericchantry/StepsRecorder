using System.IO;
using System.Windows;
using System.Windows.Threading;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.App;

/// <summary>
/// Application start-up: one copy per user, the colour theme, and last-resort handling of unexpected errors so a
/// problem while recording still leaves the steps (and where possible the video) saved.
/// </summary>
public partial class App : System.Windows.Application
{
    private AppControlServer? _control;
    private bool _handlingCrash;

    internal static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenSteps", "Logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        _control = AppControlServer.TryBecomeTheInstance();
        if (_control is null)
        {
            // Already running: bring that copy to the front instead of starting a second recorder.
            AppControlServer.Send(AppControl.Activate);
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => OnFatalException(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog(args.Exception, "background task");
            args.SetObserved();
        };

        base.OnStartup(e);
        ThemeManager.Initialize(LoadChosenTheme());

        var window = new MainWindow();
        MainWindow = window;
        _control.Start(command => Dispatcher.InvokeAsync(() => window.HandleControlCommandAsync(command)).Task.Unwrap());
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _control?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Reads the theme before any window exists, so the first window opens in the right colours.</summary>
    private static AppTheme LoadChosenTheme()
    {
        try
        {
            return Task.Run(() => new SettingsService().LoadAsync()).GetAwaiter().GetResult().Theme;
        }
        catch
        {
            return AppTheme.System;
        }
    }

    /// <summary>
    /// An error on the UI thread. Stop and save any recording first (this also closes the video file so it stays
    /// playable), then tell the user. The app keeps running unless this happens again while handling the first one.
    /// </summary>
    private async void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        var logPath = WriteCrashLog(e.Exception, "interface");
        if (_handlingCrash)
        {
            Shutdown(1);
            return;
        }

        _handlingCrash = true;
        var saved = false;
        try
        {
            if (MainWindow is MainWindow window)
            {
                saved = await window.EmergencySaveAsync();
            }
        }
        catch (Exception saveError)
        {
            WriteCrashLog(saveError, "emergency save");
        }

        System.Windows.MessageBox.Show(
            "OpenSteps ran into an unexpected problem."
            + (saved ? "\n\nYour recording was stopped and saved, so nothing you captured is lost." : string.Empty)
            + (logPath is null ? string.Empty : $"\n\nDetails were written to:\n{logPath}"),
            "OpenSteps",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        _handlingCrash = false;
    }

    /// <summary>An error the process cannot survive. There is only time to log it and write the steps to disk.</summary>
    private void OnFatalException(Exception? exception)
    {
        WriteCrashLog(exception, "fatal");
        try
        {
            (MainWindow as MainWindow)?.SaveSessionBeforeCrash();
        }
        catch
        {
            // Recovery on the next start picks up whatever was saved after the last step.
        }
    }

    internal static string? WriteCrashLog(Exception? exception, string source)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"error-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt");
            var version = typeof(App).Assembly.GetName().Version;
            File.AppendAllText(path, $"OpenSteps {version} · {DateTimeOffset.Now:O} · {source}\n{Environment.OSVersion}\n\n{exception}\n\n");

            // Keep the folder small: only the 20 most recent logs.
            foreach (var old in new DirectoryInfo(LogDirectory).GetFiles("error-*.txt").OrderByDescending(f => f.CreationTimeUtc).Skip(20))
            {
                old.Delete();
            }

            return path;
        }
        catch
        {
            return null;
        }
    }
}
