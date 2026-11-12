namespace OpenSteps.Core.Models;

public sealed class RecordingSession
{
    public int SchemaVersion { get; set; } = 1;

    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public string Title { get; set; } = "Untitled OpenSteps Guide";

    public string SessionDirectory { get; set; } = string.Empty;

    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Labels for finding the guide in the library, e.g. "onboarding", "bug".</summary>
    public List<string> Tags { get; set; } = [];

    public List<RecordedStep> Steps { get; set; } = [];

    public List<RecordingSegment> Segments { get; set; } = [];

    public List<InputEvent> InputEvents { get; set; } = [];

    public EnvironmentInfo? Environment { get; set; }
}
