using OpenSteps.Core.Models;

namespace OpenSteps.Capture;

/// <summary>
/// Mouse activity that goes on the input timeline but does not create guide steps:
/// middle clicks, wheel scrolling and drags.
/// </summary>
public sealed class PointerActivityEventArgs(
    InputEventKind kind,
    int x,
    int y,
    DateTimeOffset timestamp,
    int? delta = null,
    int? endX = null,
    int? endY = null) : EventArgs
{
    public InputEventKind Kind { get; } = kind;

    public int X { get; } = x;

    public int Y { get; } = y;

    public DateTimeOffset Timestamp { get; } = timestamp;

    public int? Delta { get; } = delta;

    public int? EndX { get; } = endX;

    public int? EndY { get; } = endY;
}
