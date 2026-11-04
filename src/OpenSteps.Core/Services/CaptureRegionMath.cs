using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>Geometry for user-drawn capture regions and for clipping rectangles into a captured area.</summary>
public static class CaptureRegionMath
{
    /// <summary>Smallest region that can be selected; anything smaller is treated as an accidental click.</summary>
    public const int MinimumSize = 40;

    /// <summary>Rectangle spanned by two drag corners, whichever way the user dragged.</summary>
    public static ScreenBounds FromCorners(int x1, int y1, int x2, int y2)
    {
        var left = Math.Min(x1, x2);
        var top = Math.Min(y1, y2);
        return new ScreenBounds(left, top, Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    public static bool IsLargeEnough(ScreenBounds region)
    {
        return region.Width >= MinimumSize && region.Height >= MinimumSize;
    }

    /// <summary>
    /// The part of <paramref name="region"/> still on screen (monitors can be unplugged between sessions),
    /// or null when too little of it is left to record.
    /// </summary>
    public static ScreenBounds? ClampToScreen(ScreenBounds region, ScreenBounds virtualScreen)
    {
        var clipped = Intersect(region, virtualScreen);
        return clipped is { } value && IsLargeEnough(value) ? value : null;
    }

    public static ScreenBounds? Intersect(ScreenBounds a, ScreenBounds b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return right > left && bottom > top ? new ScreenBounds(left, top, right - left, bottom - top) : null;
    }

    public static bool Contains(ScreenBounds area, int x, int y)
    {
        return x >= area.X && y >= area.Y && x < area.X + area.Width && y < area.Y + area.Height;
    }

    /// <summary>
    /// Screen rectangles converted to coordinates inside a captured area: grown by <paramref name="padding"/>
    /// (so field borders and focus rings are covered too), clipped to the area, and empty ones dropped.
    /// </summary>
    public static IReadOnlyList<ScreenBounds> ToLocal(IEnumerable<ScreenBounds> screenRects, ScreenBounds capturedArea, int padding = 0)
    {
        var result = new List<ScreenBounds>();
        foreach (var rect in screenRects)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            var padded = new ScreenBounds(rect.X - padding, rect.Y - padding, rect.Width + padding * 2, rect.Height + padding * 2);
            if (Intersect(padded, capturedArea) is { } clipped)
            {
                result.Add(clipped with { X = clipped.X - capturedArea.X, Y = clipped.Y - capturedArea.Y });
            }
        }

        return result;
    }
}
