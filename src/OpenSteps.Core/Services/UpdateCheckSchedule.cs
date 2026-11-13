namespace OpenSteps.Core.Services;

/// <summary>
/// Decides when it is time to look for a new version. OpenSteps checks once a day: on the first start of the
/// day, and, for a machine left running, when a day has gone by since the last look.
/// </summary>
public static class UpdateCheckSchedule
{
    /// <summary>How long OpenSteps waits between looks at the releases page.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    /// <summary>How often the running app asks whether a check is due. Cheap: it only reads a saved time.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromHours(2);

    /// <summary>
    /// True when a check should run now: nothing has been checked yet, a day has gone by, or the saved time is
    /// in the future (the clock was changed, or settings were copied from another machine).
    /// </summary>
    public static bool IsDue(DateTimeOffset? lastCheck, DateTimeOffset now, TimeSpan? interval = null)
    {
        if (lastCheck is not { } last)
        {
            return true;
        }

        if (last > now)
        {
            return true;
        }

        return now - last >= (interval ?? Interval);
    }

    /// <summary>When the next check falls due, for the tooltip on the update button.</summary>
    public static DateTimeOffset NextDue(DateTimeOffset? lastCheck, DateTimeOffset now, TimeSpan? interval = null)
    {
        return IsDue(lastCheck, now, interval) ? now : lastCheck!.Value + (interval ?? Interval);
    }
}
