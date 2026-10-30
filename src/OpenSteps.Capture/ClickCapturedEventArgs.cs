using OpenSteps.Core.Models;

namespace OpenSteps.Capture;

public sealed class ClickCapturedEventArgs(
    int x,
    int y,
    StepActionType actionType = StepActionType.Click,
    string mouseButton = "Left",
    int clickCount = 1,
    string? doubleClickDetectionReason = null,
    DateTimeOffset? timestamp = null) : EventArgs
{
    public int X { get; } = x;

    public int Y { get; } = y;

    public StepActionType ActionType { get; } = actionType;

    public string MouseButton { get; } = mouseButton;

    public int ClickCount { get; } = clickCount;

    public string? DoubleClickDetectionReason { get; } = doubleClickDetectionReason;

    /// <summary>When the (first) button press physically happened (UTC).</summary>
    public DateTimeOffset Timestamp { get; } = timestamp ?? DateTimeOffset.UtcNow;
}
