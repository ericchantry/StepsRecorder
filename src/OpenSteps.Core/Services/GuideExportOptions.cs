namespace OpenSteps.Core.Services;

/// <summary>What to include in an export besides the guide steps and screenshots.</summary>
public sealed record GuideExportOptions(
    bool IncludeVideo = true,
    bool IncludeInputLog = true,
    bool IncludeEnvironment = true,
    bool CreateZip = false)
{
    public static GuideExportOptions Default { get; } = new();
}
