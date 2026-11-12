using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>
/// Rules for the step screenshot editor that don't depend on the UI: numbering callouts, finding the edit under the
/// pointer, sizing marks for the image, and keeping the crop inside the image. All coordinates are original-image pixels.
/// </summary>
public static class ScreenshotEditing
{
    /// <summary>Crops smaller than this (in either direction) are treated as accidental.</summary>
    public const int MinimumCropSize = 16;

    /// <summary>True for a callout showing a number (letters and other text are left alone).</summary>
    public static bool IsNumberedCallout(ScreenshotAnnotation annotation)
    {
        return annotation.Type == ScreenshotAnnotationType.Marker && int.TryParse(annotation.Text, out _);
    }

    /// <summary>Number for the next callout: one more than the highest number used so far.</summary>
    public static int NextCalloutNumber(IEnumerable<ScreenshotAnnotation> annotations)
    {
        var highest = 0;
        foreach (var annotation in annotations)
        {
            if (annotation.Type == ScreenshotAnnotationType.Marker && int.TryParse(annotation.Text, out var number))
            {
                highest = Math.Max(highest, number);
            }
        }

        return highest + 1;
    }

    /// <summary>Renumbers numbered callouts 1, 2, 3… in the order they were added, e.g. after one is deleted.</summary>
    public static void RenumberCallouts(IEnumerable<ScreenshotAnnotation> annotations)
    {
        var next = 1;
        foreach (var annotation in annotations.Where(IsNumberedCallout))
        {
            annotation.Text = (next++).ToString();
        }
    }

    /// <summary>Line width that stays visible however large the screenshot is.</summary>
    public static double StrokeFor(int imageWidth, int imageHeight)
    {
        return Math.Max(3, Math.Round(Math.Max(imageWidth, imageHeight) / 640.0));
    }

    /// <summary>Callout radius that stays readable however large the screenshot is.</summary>
    public static double CalloutRadiusFor(int imageWidth, int imageHeight)
    {
        return Math.Max(14, Math.Round(Math.Max(imageWidth, imageHeight) / 110.0));
    }

    /// <summary>
    /// Index of the topmost annotation at (x, y), or -1. Boxes and highlights are hit inside their area, arrows near
    /// their line, callouts inside their circle. <paramref name="tolerance"/> widens thin targets.
    /// </summary>
    public static int HitTestAnnotation(IReadOnlyList<ScreenshotAnnotation> annotations, double x, double y, double tolerance)
    {
        for (var i = annotations.Count - 1; i >= 0; i--)
        {
            var annotation = annotations[i];
            var hit = annotation.Type switch
            {
                ScreenshotAnnotationType.Arrow => DistanceToSegment(x, y, annotation.X1, annotation.Y1, annotation.X2, annotation.Y2) <= tolerance + annotation.StrokeThickness,
                ScreenshotAnnotationType.Marker => Math.Sqrt(Math.Pow(x - annotation.X1, 2) + Math.Pow(y - annotation.Y1, 2)) <= Math.Abs(annotation.X2) + tolerance / 2,
                ScreenshotAnnotationType.Text => Contains(GetBounds(annotation), x, y, tolerance / 2),
                _ => x >= Math.Min(annotation.X1, annotation.X2) - tolerance
                    && x <= Math.Max(annotation.X1, annotation.X2) + tolerance
                    && y >= Math.Min(annotation.Y1, annotation.Y2) - tolerance
                    && y <= Math.Max(annotation.Y1, annotation.Y2) + tolerance
            };

            if (hit)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Index of the topmost blur or circle at (x, y), or -1.</summary>
    public static int HitTestRedaction(IReadOnlyList<RedactionRegion> regions, double x, double y)
    {
        for (var i = regions.Count - 1; i >= 0; i--)
        {
            var region = regions[i];
            if (x >= region.X && x <= region.X + region.Width && y >= region.Y && y <= region.Y + region.Height)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The crop clipped to the image, or null when it covers (almost) the whole image or is too small to be deliberate.
    /// </summary>
    public static ScreenBounds? NormalizeCrop(ScreenBounds? crop, int imageWidth, int imageHeight)
    {
        if (crop is not { } value || imageWidth <= 0 || imageHeight <= 0)
        {
            return null;
        }

        var clipped = CaptureRegionMath.Intersect(value, new ScreenBounds(0, 0, imageWidth, imageHeight));
        if (clipped is not { } result || result.Width < MinimumCropSize || result.Height < MinimumCropSize)
        {
            return null;
        }

        return result.Width >= imageWidth && result.Height >= imageHeight ? null : result;
    }

    /// <summary>Font size for a text label that stays readable however large the screenshot is.</summary>
    public static double TextSizeFor(int imageWidth, int imageHeight)
    {
        return Math.Max(16, Math.Round(Math.Max(imageWidth, imageHeight) / 70.0));
    }

    /// <summary>
    /// The area an edit covers, as (X, Y, Width, Height): what the selection outline is drawn around.
    /// A text label's size is estimated from its length and font size.
    /// </summary>
    public static (double X, double Y, double Width, double Height) GetBounds(ScreenshotAnnotation annotation)
    {
        switch (annotation.Type)
        {
            case ScreenshotAnnotationType.Marker:
            {
                var radius = Math.Abs(annotation.X2);
                return (annotation.X1 - radius, annotation.Y1 - radius, radius * 2, radius * 2);
            }

            case ScreenshotAnnotationType.Text:
            {
                var size = Math.Max(8, annotation.X2);
                var length = Math.Max(1, annotation.Text?.Length ?? 0);
                return (annotation.X1, annotation.Y1, length * size * 0.64 + size * 0.9, size * 1.6);
            }

            default:
                return (
                    Math.Min(annotation.X1, annotation.X2),
                    Math.Min(annotation.Y1, annotation.Y2),
                    Math.Abs(annotation.X2 - annotation.X1),
                    Math.Abs(annotation.Y2 - annotation.Y1));
        }
    }

    /// <summary>
    /// Points that can be dragged to reshape an edit: the four corners of a box or highlight (clockwise from
    /// X1/Y1), the two ends of an arrow. Callouts and text labels can only be moved, so they have none.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> GetHandles(ScreenshotAnnotation annotation)
    {
        return annotation.Type switch
        {
            ScreenshotAnnotationType.Rectangle or ScreenshotAnnotationType.Highlight =>
            [
                (annotation.X1, annotation.Y1), (annotation.X2, annotation.Y1), (annotation.X2, annotation.Y2), (annotation.X1, annotation.Y2)
            ],
            ScreenshotAnnotationType.Arrow => [(annotation.X1, annotation.Y1), (annotation.X2, annotation.Y2)],
            _ => []
        };
    }

    public static IReadOnlyList<(double X, double Y)> GetHandles(RedactionRegion region)
    {
        return
        [
            (region.X, region.Y), (region.X + region.Width, region.Y), (region.X + region.Width, region.Y + region.Height), (region.X, region.Y + region.Height)
        ];
    }

    /// <summary>Index of the handle within <paramref name="tolerance"/> of (x, y), or -1.</summary>
    public static int HitTestHandle(IReadOnlyList<(double X, double Y)> handles, double x, double y, double tolerance)
    {
        for (var i = 0; i < handles.Count; i++)
        {
            if (Math.Abs(handles[i].X - x) <= tolerance && Math.Abs(handles[i].Y - y) <= tolerance)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Moves an edit by (dx, dy), stopping when its centre would leave the image.</summary>
    public static void Move(ScreenshotAnnotation annotation, double dx, double dy, int imageWidth, int imageHeight)
    {
        var bounds = GetBounds(annotation);
        var centreX = bounds.X + bounds.Width / 2;
        var centreY = bounds.Y + bounds.Height / 2;
        dx = Math.Clamp(centreX + dx, 0, imageWidth) - centreX;
        dy = Math.Clamp(centreY + dy, 0, imageHeight) - centreY;

        annotation.X1 += dx;
        annotation.Y1 += dy;
        if (annotation.Type is ScreenshotAnnotationType.Rectangle or ScreenshotAnnotationType.Highlight or ScreenshotAnnotationType.Arrow)
        {
            annotation.X2 += dx;
            annotation.Y2 += dy;
        }
    }

    /// <summary>Drags one handle (see <see cref="GetHandles(ScreenshotAnnotation)"/>) to (x, y), kept inside the image.</summary>
    public static void MoveHandle(ScreenshotAnnotation annotation, int handle, double x, double y, int imageWidth, int imageHeight)
    {
        x = Math.Clamp(x, 0, imageWidth);
        y = Math.Clamp(y, 0, imageHeight);
        if (annotation.Type == ScreenshotAnnotationType.Arrow)
        {
            if (handle == 0)
            {
                (annotation.X1, annotation.Y1) = (x, y);
            }
            else if (handle == 1)
            {
                (annotation.X2, annotation.Y2) = (x, y);
            }

            return;
        }

        if (annotation.Type is not (ScreenshotAnnotationType.Rectangle or ScreenshotAnnotationType.Highlight))
        {
            return;
        }

        switch (handle)
        {
            case 0:
                (annotation.X1, annotation.Y1) = (x, y);
                break;
            case 1:
                (annotation.X2, annotation.Y1) = (x, y);
                break;
            case 2:
                (annotation.X2, annotation.Y2) = (x, y);
                break;
            case 3:
                (annotation.X1, annotation.Y2) = (x, y);
                break;
        }
    }

    /// <summary>A blur or circle moved by (dx, dy), kept wholly inside the image.</summary>
    public static RedactionRegion Move(RedactionRegion region, double dx, double dy, int imageWidth, int imageHeight)
    {
        var x = (int)Math.Round(Math.Clamp(region.X + dx, 0, Math.Max(0, imageWidth - region.Width)));
        var y = (int)Math.Round(Math.Clamp(region.Y + dy, 0, Math.Max(0, imageHeight - region.Height)));
        return region with { X = x, Y = y };
    }

    /// <summary>A blur or circle with one corner dragged to (x, y); the opposite corner stays put.</summary>
    public static RedactionRegion MoveHandle(RedactionRegion region, int handle, double x, double y, int imageWidth, int imageHeight)
    {
        const int minimum = 4;
        x = Math.Clamp(x, 0, imageWidth);
        y = Math.Clamp(y, 0, imageHeight);
        double left = region.X, top = region.Y, right = region.X + region.Width, bottom = region.Y + region.Height;
        switch (handle)
        {
            case 0:
                (left, top) = (x, y);
                break;
            case 1:
                (right, top) = (x, y);
                break;
            case 2:
                (right, bottom) = (x, y);
                break;
            case 3:
                (left, bottom) = (x, y);
                break;
            default:
                return region;
        }

        var newLeft = (int)Math.Round(Math.Min(left, right));
        var newTop = (int)Math.Round(Math.Min(top, bottom));
        var width = Math.Max(minimum, (int)Math.Round(Math.Abs(right - left)));
        var height = Math.Max(minimum, (int)Math.Round(Math.Abs(bottom - top)));
        return region with { X = newLeft, Y = newTop, Width = width, Height = height };
    }

    /// <summary>True when text on this colour should be dark (e.g. yellow), false when it should be white.</summary>
    public static bool NeedsDarkText(string? hexColor)
    {
        var hex = (hexColor ?? string.Empty).TrimStart('#');
        if (hex.Length == 8)
        {
            hex = hex[2..];
        }

        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return false;
        }

        var luminance = (0.299 * ((rgb >> 16) & 0xFF) + 0.587 * ((rgb >> 8) & 0xFF) + 0.114 * (rgb & 0xFF)) / 255;
        return luminance > 0.62;
    }

    private static bool Contains((double X, double Y, double Width, double Height) bounds, double x, double y, double tolerance)
    {
        return x >= bounds.X - tolerance && x <= bounds.X + bounds.Width + tolerance
            && y >= bounds.Y - tolerance && y <= bounds.Y + bounds.Height + tolerance;
    }

    public static ScreenshotAnnotation Clone(ScreenshotAnnotation annotation)
    {
        return new ScreenshotAnnotation
        {
            Id = annotation.Id,
            Type = annotation.Type,
            X1 = annotation.X1,
            Y1 = annotation.Y1,
            X2 = annotation.X2,
            Y2 = annotation.Y2,
            Text = annotation.Text,
            Color = annotation.Color,
            Opacity = annotation.Opacity,
            StrokeThickness = annotation.StrokeThickness
        };
    }

    private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared == 0 ? 0 : Math.Clamp(((px - x1) * dx + (py - y1) * dy) / lengthSquared, 0, 1);
        var cx = x1 + t * dx;
        var cy = y1 + t * dy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }
}
