using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>Tidies a guide that was recovered after OpenSteps closed in the middle of recording it.</summary>
public static class RecordingRecovery
{
    public const string InterruptedVideoMessage = "OpenSteps closed unexpectedly during this recording, so its video could not be finished.";

    /// <summary>
    /// Recording runs that never finished (no duration) get their length from the last thing recorded in them and
    /// a note explaining why there is no video. Returns true when anything changed.
    /// </summary>
    public static bool MarkInterruptedSegments(RecordingSession session)
    {
        var changed = false;
        foreach (var segment in session.Segments.Where(segment => segment.DurationSeconds <= 0))
        {
            var lastEvent = session.InputEvents.Where(e => e.Segment == segment.Number).Select(e => e.OffsetSeconds).DefaultIfEmpty(0).Max();
            var lastStep = session.Steps.Where(s => s.VideoSegment == segment.Number && s.VideoOffsetSeconds.HasValue).Select(s => s.VideoOffsetSeconds!.Value).DefaultIfEmpty(0).Max();
            segment.DurationSeconds = Math.Round(Math.Max(lastEvent, lastStep), 2);
            segment.VideoRelativePath = null;
            segment.VideoPath = null;
            segment.VideoError = InterruptedVideoMessage;
            changed = true;
        }

        return changed;
    }
}
