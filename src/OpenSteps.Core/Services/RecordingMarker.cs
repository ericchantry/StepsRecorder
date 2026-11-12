using System.Text.Json;

namespace OpenSteps.Core.Services;

/// <summary>What was being recorded when the marker was written.</summary>
public sealed record RecordingMarkerInfo(Guid SessionId, DateTimeOffset StartedAt, int ProcessId);

/// <summary>
/// A small file that exists only while a recording is in progress. If OpenSteps finds it at start-up, the last
/// run ended without stopping the recording (crash, power loss, forced close) and the guide can be recovered.
/// </summary>
public sealed class RecordingMarker
{
    public RecordingMarker(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenSteps",
            "recording-in-progress.json");
    }

    public string Path { get; }

    public void Write(Guid sessionId, int processId)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(new RecordingMarkerInfo(sessionId, DateTimeOffset.Now, processId)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Recovery is a safety net; recording must not fail because the marker can't be written.
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The interrupted recording, or null when the last run ended normally (or the marker is unreadable).</summary>
    public RecordingMarkerInfo? Read()
    {
        try
        {
            return File.Exists(Path) ? JsonSerializer.Deserialize<RecordingMarkerInfo>(File.ReadAllText(Path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
