namespace OpenSteps.Core.Models;

/// <summary>
/// One keyboard or mouse event on the recording timeline.
/// <see cref="OffsetSeconds"/> is measured from the start of its recording segment with paused time removed,
/// so it lines up with the segment's video.
/// </summary>
public sealed class InputEvent
{
    public int Segment { get; set; } = 1;

    public double OffsetSeconds { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public InputEventKind Kind { get; set; }

    /// <summary>Key or key combination name, e.g. "Enter", "Ctrl+Shift+N". Null for pointer events.</summary>
    public string? Key { get; set; }

    /// <summary>Characters produced by a <see cref="InputEventKind.KeyText"/> event. Null when typed-text capture is off or the input was masked.</summary>
    public string? Text { get; set; }

    /// <summary>True when the key went into a password or other sensitive field and its character was not stored.</summary>
    public bool Masked { get; set; }

    public int? X { get; set; }

    public int? Y { get; set; }

    public int? EndX { get; set; }

    public int? EndY { get; set; }

    /// <summary>Scroll wheel delta (120 per notch). Positive is up (or right for horizontal scroll).</summary>
    public int? Delta { get; set; }

    public string? WindowTitle { get; set; }

    public string? ProcessName { get; set; }
}
