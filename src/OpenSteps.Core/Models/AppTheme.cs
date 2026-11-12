namespace OpenSteps.Core.Models;

/// <summary>The colour theme chosen in OpenSteps.</summary>
public enum AppTheme
{
    /// <summary>Follow the Windows app mode (light or dark).</summary>
    System,
    Light,
    Dark
}

/// <summary>The theme actually shown.</summary>
public enum ResolvedTheme
{
    Light,
    Dark,
    HighContrast
}
