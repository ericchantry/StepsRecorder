using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

public static class ThemeResolver
{
    /// <summary>
    /// A Windows contrast theme always wins, because the user has asked every app for those colours.
    /// Otherwise the chosen theme is used, with System following the Windows light/dark app mode.
    /// </summary>
    public static ResolvedTheme Resolve(AppTheme chosen, bool windowsUsesDarkApps, bool windowsHighContrast)
    {
        if (windowsHighContrast)
        {
            return ResolvedTheme.HighContrast;
        }

        return chosen switch
        {
            AppTheme.Dark => ResolvedTheme.Dark,
            AppTheme.Light => ResolvedTheme.Light,
            _ => windowsUsesDarkApps ? ResolvedTheme.Dark : ResolvedTheme.Light
        };
    }
}
