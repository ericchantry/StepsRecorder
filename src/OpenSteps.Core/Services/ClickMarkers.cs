using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>Maps recorded clicks to the marker drawn on screenshots and in the video.</summary>
public static class ClickMarkers
{
    public static ClickMarkerKind For(StepActionType actionType, string? mouseButton)
    {
        return actionType switch
        {
            StepActionType.DoubleClick => ClickMarkerKind.Double,
            StepActionType.RightClick => ClickMarkerKind.Right,
            _ => FromButton(mouseButton)
        };
    }

    public static ClickMarkerKind FromButton(string? mouseButton)
    {
        return mouseButton?.Trim().ToLowerInvariant() switch
        {
            "right" => ClickMarkerKind.Right,
            "middle" => ClickMarkerKind.Middle,
            _ => ClickMarkerKind.Left
        };
    }

    /// <summary>Text on the screenshot badge.</summary>
    public static string GetLabel(ClickMarkerKind kind)
    {
        return kind switch
        {
            ClickMarkerKind.Right => "Right-click",
            ClickMarkerKind.Double => "Double-click",
            ClickMarkerKind.Middle => "Middle-click",
            _ => "Click"
        };
    }

    /// <summary>One- or two-character tag on the video ring.</summary>
    public static string GetShortLabel(ClickMarkerKind kind)
    {
        return kind switch
        {
            ClickMarkerKind.Right => "R",
            ClickMarkerKind.Double => "2×",
            ClickMarkerKind.Middle => "M",
            _ => "L"
        };
    }

    /// <summary>
    /// True when a left press at (x, y) completes a double-click with the previous left press,
    /// using the Windows double-click time and distance.
    /// </summary>
    public static bool IsDoubleClick(
        (int X, int Y, DateTimeOffset At)? previousLeftPress,
        int x,
        int y,
        DateTimeOffset at,
        TimeSpan doubleClickTime,
        int maxDistanceX,
        int maxDistanceY)
    {
        return previousLeftPress is { } previous
            && at - previous.At <= doubleClickTime
            && at >= previous.At
            && Math.Abs(x - previous.X) <= maxDistanceX / 2
            && Math.Abs(y - previous.Y) <= maxDistanceY / 2;
    }
}
