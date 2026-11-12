using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using OpenSteps.Core.Models;
using WpfColor = System.Windows.Media.Color;

namespace OpenSteps.App;

public enum AppTheme
{
    Light,
    Dark,
    HighContrast
}

/// <summary>
/// Applies the light, dark or high-contrast colours by replacing the colour brushes in the application resources
/// (every window uses them through DynamicResource, so open windows update at once). "Match Windows" follows the
/// Windows app mode, and Windows high contrast always wins so the app respects the user's contrast theme.
/// </summary>
public static class ThemeManager
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private static AppThemePreference _preference = AppThemePreference.System;
    private static bool _watching;

    public static AppTheme Current { get; private set; } = AppTheme.Light;

    public static event EventHandler? ThemeChanged;

    /// <summary>The theme a preference resolves to right now.</summary>
    public static AppTheme Resolve(AppThemePreference preference, bool windowsHighContrast, bool windowsUsesLightApps)
    {
        if (windowsHighContrast)
        {
            return AppTheme.HighContrast;
        }

        return preference switch
        {
            AppThemePreference.Light => AppTheme.Light,
            AppThemePreference.Dark => AppTheme.Dark,
            _ => windowsUsesLightApps ? AppTheme.Light : AppTheme.Dark
        };
    }

    public static void Apply(AppThemePreference preference)
    {
        _preference = preference;
        WatchWindows();
        var theme = Resolve(preference, SystemParameters.HighContrast, WindowsUsesLightApps());
        var resources = System.Windows.Application.Current.Resources;
        foreach (var (key, color) in GetPalette(theme))
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            resources[key] = brush;
        }

        Current = theme;
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            ApplyTitleBar(window);
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Makes the window's title bar dark or light to match the theme.</summary>
    public static void ApplyTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                var dark = Current == AppTheme.Dark ? 1 : 0;
                _ = DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            }
        }
        catch
        {
            // Older Windows builds don't support dark title bars.
        }
    }

    public static IReadOnlyDictionary<string, WpfColor> GetPalette(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Dark => Dark,
            AppTheme.HighContrast => BuildHighContrast(),
            _ => Light
        };
    }

    private static bool WindowsUsesLightApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch
        {
            return true;
        }
    }

    private static void WatchWindows()
    {
        if (_watching)
        {
            return;
        }

        _watching = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility or UserPreferenceCategory.VisualStyle)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    var next = Resolve(_preference, SystemParameters.HighContrast, WindowsUsesLightApps());
                    if (next != Current || next == AppTheme.HighContrast)
                    {
                        Apply(_preference);
                    }
                });
            }
        };
    }

    private static WpfColor C(string hex) => (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);

    private static readonly Dictionary<string, WpfColor> Light = new()
    {
        ["AppBackgroundBrush"] = C("#F4F6F9"),
        ["PanelBackgroundBrush"] = C("#FFFFFF"),
        ["SurfaceBackgroundBrush"] = C("#FFFFFF"),
        ["SoftSurfaceBrush"] = C("#EEF1F6"),
        ["HoverSurfaceBrush"] = C("#E5E9F0"),
        ["PressedSurfaceBrush"] = C("#D8DEE7"),
        ["SubtleSurfaceBrush"] = C("#F7F8FB"),
        ["BorderSoftBrush"] = C("#E3E7EE"),
        ["BorderBrush"] = C("#D2D8E1"),
        ["BorderEmphasisBrush"] = C("#B5BFCD"),
        ["FocusBorderBrush"] = C("#5B7CFA"),
        ["TextPrimaryBrush"] = C("#0E1626"),
        ["TextSecondaryBrush"] = C("#5A6573"),
        ["TextMutedBrush"] = C("#6F7988"),
        ["TextInverseBrush"] = C("#FFFFFF"),
        ["AccentBrush"] = C("#1A2233"),
        ["AccentHoverBrush"] = C("#2A3346"),
        ["AccentPressedBrush"] = C("#0E1626"),
        ["InfoBrush"] = C("#4C6EF5"),
        ["SuccessBrush"] = C("#16A34A"),
        ["WarningBrush"] = C("#D97706"),
        ["DangerBrush"] = C("#DC2626"),
        ["RecordingBrush"] = C("#E0394A"),
        ["RecordingHoverBrush"] = C("#C42D3D"),
        ["RecordingPressedBrush"] = C("#A52532"),
        ["OnRecordingTextBrush"] = C("#FFFFFF"),
        ["ScrollThumbBrush"] = C("#C5CCD6"),
        ["ScrollThumbHoverBrush"] = C("#A4ADBA"),
        ["ScrollThumbPressedBrush"] = C("#8893A2"),
        ["SuccessSurfaceBrush"] = C("#E7F7EE"),
        ["OverlayButtonBrush"] = C("#CCFFFFFF"),
        ["CanvasBackgroundBrush"] = C("#E9EDF2"),
        ["CanvasInnerBrush"] = C("#F8FAFC"),
        ["NoticeBackgroundBrush"] = C("#FFF7E6"),
        ["NoticeBorderBrush"] = C("#F5D49A"),
        ["SelectedToolBrush"] = C("#E6ECFF")
    };

    private static readonly Dictionary<string, WpfColor> Dark = new()
    {
        ["AppBackgroundBrush"] = C("#14171C"),
        ["PanelBackgroundBrush"] = C("#1D2128"),
        ["SurfaceBackgroundBrush"] = C("#1D2128"),
        ["SoftSurfaceBrush"] = C("#272C35"),
        ["HoverSurfaceBrush"] = C("#313743"),
        ["PressedSurfaceBrush"] = C("#3B4250"),
        ["SubtleSurfaceBrush"] = C("#191C22"),
        ["BorderSoftBrush"] = C("#2C323C"),
        ["BorderBrush"] = C("#3A414E"),
        ["BorderEmphasisBrush"] = C("#556072"),
        ["FocusBorderBrush"] = C("#7C97FF"),
        ["TextPrimaryBrush"] = C("#EEF1F6"),
        ["TextSecondaryBrush"] = C("#B3BCC9"),
        ["TextMutedBrush"] = C("#929CAB"),
        ["TextInverseBrush"] = C("#0E1626"),
        ["AccentBrush"] = C("#E6EAF2"),
        ["AccentHoverBrush"] = C("#FFFFFF"),
        ["AccentPressedBrush"] = C("#C9D0DC"),
        ["InfoBrush"] = C("#7C97FF"),
        ["SuccessBrush"] = C("#34C36B"),
        ["WarningBrush"] = C("#F0A23B"),
        ["DangerBrush"] = C("#F26D6D"),
        ["RecordingBrush"] = C("#E0394A"),
        ["RecordingHoverBrush"] = C("#EC5463"),
        ["RecordingPressedBrush"] = C("#C42D3D"),
        ["OnRecordingTextBrush"] = C("#FFFFFF"),
        ["ScrollThumbBrush"] = C("#4A5261"),
        ["ScrollThumbHoverBrush"] = C("#5C6577"),
        ["ScrollThumbPressedBrush"] = C("#6E788C"),
        ["SuccessSurfaceBrush"] = C("#153524"),
        ["OverlayButtonBrush"] = C("#CC1D2128"),
        ["CanvasBackgroundBrush"] = C("#101216"),
        ["CanvasInnerBrush"] = C("#0B0D10"),
        ["NoticeBackgroundBrush"] = C("#3A2E12"),
        ["NoticeBorderBrush"] = C("#6B5318"),
        ["SelectedToolBrush"] = C("#2A3559")
    };

    /// <summary>High contrast uses the colours of the Windows contrast theme the user chose.</summary>
    private static Dictionary<string, WpfColor> BuildHighContrast()
    {
        var window = System.Windows.SystemColors.WindowColor;
        var text = System.Windows.SystemColors.WindowTextColor;
        var highlight = System.Windows.SystemColors.HighlightColor;
        var highlightText = System.Windows.SystemColors.HighlightTextColor;
        var gray = System.Windows.SystemColors.GrayTextColor;
        var hot = System.Windows.SystemColors.HotTrackColor;
        var palette = new Dictionary<string, WpfColor>();
        foreach (var key in Light.Keys)
        {
            palette[key] = key switch
            {
                "TextPrimaryBrush" or "TextSecondaryBrush" or "InfoBrush" or "SuccessBrush" or "WarningBrush" or "DangerBrush" => text,
                "TextMutedBrush" => gray,
                "TextInverseBrush" or "OnRecordingTextBrush" => highlightText,
                "AccentBrush" or "AccentHoverBrush" or "AccentPressedBrush" or "RecordingBrush" or "RecordingHoverBrush" or "RecordingPressedBrush" or "SelectedToolBrush" => highlight,
                "FocusBorderBrush" => hot,
                "BorderSoftBrush" or "BorderBrush" or "BorderEmphasisBrush" or "NoticeBorderBrush" or "ScrollThumbBrush" or "ScrollThumbHoverBrush" or "ScrollThumbPressedBrush" => text,
                _ => window
            };
        }

        return palette;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
