using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;
using Application = System.Windows.Application;

namespace OpenSteps.App;

/// <summary>
/// Applies the light, dark or high-contrast colours by swapping the theme dictionary merged into the application
/// resources. Every brush is referenced with DynamicResource, so open windows change at once. Follows Windows when
/// the setting is System, and always switches to the system colours when Windows is in a contrast theme.
/// </summary>
internal static class ThemeManager
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private static AppTheme _chosen = AppTheme.System;
    private static bool _initialized;

    public static ResolvedTheme Current { get; private set; } = ResolvedTheme.Light;

    public static AppTheme Chosen => _chosen;

    public static void Initialize(AppTheme chosen)
    {
        _chosen = chosen;
        if (!_initialized)
        {
            _initialized = true;

            // Give every window the theme's text colour and a matching title bar as it opens.
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window window)
                {
                    StyleWindow(window);
                }
            }));
            SystemEvents.UserPreferenceChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Apply);
            SystemParameters.StaticPropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SystemParameters.HighContrast))
                {
                    Application.Current?.Dispatcher.BeginInvoke(Apply);
                }
            };
        }

        Apply();
    }

    public static void SetTheme(AppTheme chosen)
    {
        _chosen = chosen;
        Apply();
    }

    private static void Apply()
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var resolved = ThemeResolver.Resolve(_chosen, WindowsUsesDarkApps(), SystemParameters.HighContrast);
        var source = new Uri($"Themes/{resolved}.xaml", UriKind.Relative);
        var dictionaries = application.Resources.MergedDictionaries;
        if (dictionaries.Count == 0 || dictionaries[0].Source != source)
        {
            var theme = new ResourceDictionary { Source = source };
            if (dictionaries.Count == 0)
            {
                dictionaries.Add(theme);
            }
            else
            {
                dictionaries[0] = theme;
            }
        }

        Current = resolved;
        foreach (Window window in application.Windows)
        {
            StyleWindow(window);
        }
    }

    private static void StyleWindow(Window window)
    {
        window.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextPrimaryBrush");
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                var dark = Current == ResolvedTheme.Dark ? 1 : 0;
                _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
        }
        catch
        {
            // Older Windows 10 builds don't support dark title bars; the window content is still themed.
        }
    }

    private static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
