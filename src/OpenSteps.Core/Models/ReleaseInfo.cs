namespace OpenSteps.Core.Models;

/// <summary>One published OpenSteps release on GitHub, with the download it offers and the notes that go with it.</summary>
public sealed record ReleaseInfo
{
    public required string TagName { get; init; }

    public required ReleaseVersion Version { get; init; }

    /// <summary>The release's own title ("OpenSteps v0.6.0"), or the tag when it has none.</summary>
    public string? Title { get; init; }

    /// <summary>The release notes, as markdown. These come from the matching CHANGELOG.md section.</summary>
    public string Notes { get; init; } = string.Empty;

    public DateTimeOffset? PublishedAt { get; init; }

    public bool IsPrerelease { get; init; }

    /// <summary>Download address of the OpenSteps-&lt;version&gt;-win-x64.zip asset, when the release has one.</summary>
    public string? PackageUrl { get; init; }

    public string? PackageName { get; init; }

    public long PackageSize { get; init; }

    /// <summary>Download address of the matching .sha256 file, used to check the download arrived intact.</summary>
    public string? ChecksumUrl { get; init; }

    /// <summary>A release can only be installed by OpenSteps itself when it ships the Windows package.</summary>
    public bool CanInstall => !string.IsNullOrWhiteSpace(PackageUrl);

    public string PageUrl(string repository) => $"https://github.com/{repository}/releases/tag/{TagName}";
}
