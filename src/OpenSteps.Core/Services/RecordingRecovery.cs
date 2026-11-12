using System.Text.Json;
using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>Written into a session folder while it is being recorded, and removed when recording ends normally.</summary>
public sealed record RecordingMarker(Guid SessionId, int ProcessId, DateTimeOffset StartedAt, int SegmentNumber, string? VideoRelativePath);

/// <summary>A recording that never ended normally: OpenSteps crashed, was force-closed, or the PC lost power.</summary>
public sealed record InterruptedRecording(RecordingMarker Marker, string SessionDirectory, string Title, int StepCount);

/// <summary>
/// Finds and repairs recordings that were cut short. While recording, OpenSteps keeps a marker file in the session
/// folder and saves the session after every step, so the steps and screenshots survive a crash. The video for the
/// interrupted part can't be saved: an MP4 is only playable once it has been closed properly.
/// </summary>
public static class RecordingRecovery
{
    public const string MarkerFileName = "recording.lock";
    public const string InterruptedVideoMessage = "OpenSteps closed unexpectedly during this part of the recording, so its video could not be saved. The steps and screenshots were recovered.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void WriteMarker(string sessionDirectory, RecordingMarker marker)
    {
        Directory.CreateDirectory(sessionDirectory);
        File.WriteAllText(Path.Combine(sessionDirectory, MarkerFileName), JsonSerializer.Serialize(marker, JsonOptions));
    }

    public static void ClearMarker(string? sessionDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory))
        {
            return;
        }

        try
        {
            File.Delete(Path.Combine(sessionDirectory, MarkerFileName));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Sessions under <paramref name="sessionsRoot"/> with a marker whose recording process is gone.
    /// <paramref name="isRecorderStillRunning"/> is given the marker's process id.
    /// </summary>
    public static async Task<IReadOnlyList<InterruptedRecording>> FindInterruptedAsync(SessionStore store, Func<int, bool> isRecorderStillRunning)
    {
        var found = new List<InterruptedRecording>();
        if (!Directory.Exists(store.RootDirectory))
        {
            return found;
        }

        foreach (var markerPath in Directory.EnumerateFiles(store.RootDirectory, MarkerFileName, SearchOption.AllDirectories))
        {
            var directory = Path.GetDirectoryName(markerPath)!;
            RecordingMarker? marker = null;
            try
            {
                marker = JsonSerializer.Deserialize<RecordingMarker>(await File.ReadAllTextAsync(markerPath), JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
            }

            if (marker is null)
            {
                // Unreadable marker: nothing to recover from it.
                ClearMarker(directory);
                continue;
            }

            if (isRecorderStillRunning(marker.ProcessId))
            {
                continue;
            }

            var session = await store.LoadSessionAsync(marker.SessionId);
            found.Add(new InterruptedRecording(marker, directory, session?.Title ?? "Untitled OpenSteps Guide", session?.Steps.Count ?? 0));
        }

        return found;
    }

    /// <summary>
    /// Makes an interrupted session safe to open: removes the unplayable video, notes on the segment why it has no
    /// video, and clears the marker. Returns the repaired session, or null when nothing had been saved yet
    /// (in which case the empty session folder is removed).
    /// </summary>
    public static async Task<RecordingSession?> RepairAsync(SessionStore store, InterruptedRecording interrupted)
    {
        var marker = interrupted.Marker;
        if (!string.IsNullOrWhiteSpace(marker.VideoRelativePath))
        {
            TryDelete(Path.Combine(interrupted.SessionDirectory, marker.VideoRelativePath));
        }

        var session = await store.LoadSessionAsync(marker.SessionId);
        var nothingRecorded = session is null
            || (session.Steps.Count == 0 && session.Segments.All(s => s.Number == marker.SegmentNumber));
        if (nothingRecorded)
        {
            // Interrupted before the first step: there is nothing to keep.
            ClearMarker(interrupted.SessionDirectory);
            try
            {
                Directory.Delete(interrupted.SessionDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            return null;
        }

        var segment = session!.Segments.FirstOrDefault(s => s.Number == marker.SegmentNumber);
        if (segment is not null)
        {
            segment.VideoPath = null;
            segment.VideoRelativePath = null;
            segment.VideoError = InterruptedVideoMessage;
            if (segment.DurationSeconds <= 0)
            {
                var lastOffset = session.Steps.Where(step => step.VideoSegment == segment.Number).Max(step => step.VideoOffsetSeconds) ?? 0;
                segment.DurationSeconds = Math.Round(lastOffset, 2);
            }
        }

        await store.SaveSessionAsync(session);
        ClearMarker(interrupted.SessionDirectory);
        return session;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
