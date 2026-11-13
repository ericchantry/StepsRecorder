namespace OpenSteps.Core.Models;

public enum UpdateStatus
{
    /// <summary>Nothing newer than the running version is published.</summary>
    UpToDate,

    /// <summary>A newer version is published and ready to install.</summary>
    UpdateAvailable,

    /// <summary>A newer version is published, but the user asked not to be told about this one again.</summary>
    Skipped,

    /// <summary>GitHub could not be reached, or answered with something unreadable.</summary>
    CheckFailed
}

/// <summary>
/// The outcome of one look at the releases page: whether something newer exists and, if it does, every release
/// between the running version and that one, so the user can see everything that changed, not just the last step.
/// </summary>
public sealed record UpdateCheckResult
{
    public required UpdateStatus Status { get; init; }

    public required ReleaseVersion CurrentVersion { get; init; }

    /// <summary>The release to install, when there is one.</summary>
    public ReleaseInfo? Newest { get; init; }

    /// <summary>Every release newer than the running version, newest first. The "what's changed" list.</summary>
    public IReadOnlyList<ReleaseInfo> Changes { get; init; } = [];

    /// <summary>Why the check failed, for the log; never shown as the main message.</summary>
    public string? Error { get; init; }

    public bool HasUpdate => Status == UpdateStatus.UpdateAvailable && Newest is not null;

    public static UpdateCheckResult Failed(ReleaseVersion current, string error) =>
        new() { Status = UpdateStatus.CheckFailed, CurrentVersion = current, Error = error };

    public static UpdateCheckResult UpToDate(ReleaseVersion current) =>
        new() { Status = UpdateStatus.UpToDate, CurrentVersion = current };
}
