namespace OpenSteps.Core.Models;

public sealed class RecordingSession
{
    public int SchemaVersion { get; set; } = 1;

    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public string Title { get; set; } = "Untitled OpenSteps Guide";

    /// <summary>Labels for finding this guide in the saved-guides list.</summary>
    public List<string> Tags { get; set; } = [];

    public string SessionDirectory { get; set; } = string.Empty;

    public string OutputDirectory { get; set; } = string.Empty;

    public List<RecordedStep> Steps { get; set; } = [];

    public List<RecordingSegment> Segments { get; set; } = [];

    public List<InputEvent> InputEvents { get; set; } = [];

    public EnvironmentInfo? Environment { get; set; }
}
