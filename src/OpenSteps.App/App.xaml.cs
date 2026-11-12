using System.IO;
using System.Windows;
using System.Windows.Threading;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.App;

/// <summary>
/// Application start-up: one running copy at a time, the colour theme, screen-reader names, and a last-resort
/// handler that saves the guide and writes a crash log if something unexpected goes wrong.
/// </summary>
public partial class App : System.Windows.Application
{
    private AppInstance? _instance;
    private bool _handlingCrash;

    internal AppInstance? Instance => _instance;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSteps",
        "logs");

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = AppInstance.TryAcquire();
        if (_instance is null)
        {
            // Already running: that copy has been asked to come to the front.
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => WriteCrashLog(args.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteCrashLog(args.Exception, "Task");
            args.SetObserved();
        };

        base.OnStartup(e);
        Accessibility.Register();
        ThemeManager.Apply(ReadThemePreference());

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        _instance.Listen(
            onActivate: () => Dispatcher.BeginInvoke(window.BringToFrontFromAnotherLaunch),
            onShutdown: () => Dispatcher.BeginInvoke(async () => await window.SaveAndExitAsync()));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        _instance = null;
        base.OnExit(e);
    }

    /// <summary>The saved theme choice, read before any window exists so the first window opens in the right colours.</summary>
    private static AppThemePreference ReadThemePreference()
    {
        try
        {
            return Task.Run(() => new SettingsService().LoadAsync()).GetAwaiter().GetResult().Theme;
        }
        catch
        {
            return AppThemePreference.System;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_handlingCrash)
        {
            return;
        }

        _handlingCrash = true;
        var logPath = WriteCrashLog(e.Exception, "UI");
        var saved = false;
        try
        {
            saved = (MainWindow as MainWindow)?.EmergencySave() == true;
        }
        catch
        {
            // Saving is best effort at this point.
        }

        System.Windows.MessageBox.Show(
            "OpenSteps ran into a problem and needs to close.\n\n"
            + (saved ? "Your guide has been saved and will be there when you start OpenSteps again.\n\n" : string.Empty)
            + (logPath is null ? string.Empty : $"Details were written to:\n{logPath}"),
            "OpenSteps",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        Shutdown(1);
    }

    /// <summary>Writes the exception to a dated file in the logs folder and returns its path (null if that failed).</summary>
    internal static string? WriteCrashLog(Exception? exception, string source)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            var version = typeof(App).Assembly.GetName().Version;
            File.WriteAllText(path, $"OpenSteps {version} · {source} · {DateTimeOffset.Now:O}\r\n{Environment.OSVersion}\r\n\r\n{exception}");
            return path;
        }
        catch
        {
            return null;
        }
    }
}
