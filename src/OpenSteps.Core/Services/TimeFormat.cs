using System.Globalization;

namespace OpenSteps.Core.Services;

public static class TimeFormat
{
    /// <summary>Formats a timeline offset as m:ss.f (or h:mm:ss.f past an hour).</summary>
    public static string Format(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(Math.Floor(seconds * 10) / 10);
        var tenths = time.Milliseconds / 100;
        return time.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}.{tenths}")
            : string.Create(CultureInfo.InvariantCulture, $"{time.Minutes}:{time.Seconds:00}.{tenths}");
    }
}
