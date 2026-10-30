using System.Text.Json.Serialization;

namespace OpenSteps.Core.Models;

/// <summary>
/// One continuous recording run within a session. "Record more steps" adds a new segment
/// with its own video file and its own timeline starting at 0.
/// </summary>
public sealed class RecordingSegment
{
    public int Number { get; set; } = 1;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;

    public double DurationSeconds { get; set; }

    public bool TypedTextCaptured { get; set; }

    /// <summary>What the video covers, e.g. "All monitors", "Display 2 · 1920×1080".</summary>
    public string? CaptureArea { get; set; }

    public string? VideoRelativePath { get; set; }

    [JsonIgnore]
    public string? VideoPath { get; set; }

    public string? VideoError { get; set; }

    [JsonIgnore]
    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoPath) && File.Exists(VideoPath);
}
