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

    /// <summary>Height of a new text label (image pixels) that stays readable however large the screenshot is.</summary>
    public static double TextHeightFor(int imageWidth, int imageHeight)
    {
        return Math.Max(26, Math.Round(Math.Max(imageWidth, imageHeight) / 42.0));
    }

    /// <summary>Font size (pixels) for a text label of the given height; the rest is padding.</summary>
    public static double TextFontSizeFor(double labelHeight)
    {
        return Math.Max(8, Math.Abs(labelHeight) * 0.6);
    }

    /// <summary>The area an annotation covers: left, top, right, bottom in image pixels.</summary>
    public static (double Left, double Top, double Right, double Bottom) GetBounds(ScreenshotAnnotation annotation)
    {
        if (annotation.Type == ScreenshotAnnotationType.Marker)
        {
            var radius = Math.Abs(annotation.X2);
            return (annotation.X1 - radius, annotation.Y1 - radius, annotation.X1 + radius, annotation.Y1 + radius);
        }

        return (Math.Min(annotation.X1, annotation.X2), Math.Min(annotation.Y1, annotation.Y2), Math.Max(annotation.X1, annotation.X2), Math.Max(annotation.Y1, annotation.Y2));
    }

    /// <summary>
    /// Moves an annotation by (dx, dy), stopping where its centre would leave the image so it can't be lost off the edge.
    /// </summary>
    public static void Move(ScreenshotAnnotation annotation, double dx, double dy, int imageWidth, int imageHeight)
    {
        var (left, top, right, bottom) = GetBounds(annotation);
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        dx = Math.Clamp(centerX + dx, 0, imageWidth) - centerX;
        dy = Math.Clamp(centerY + dy, 0, imageHeight) - centerY;

        annotation.X1 += dx;
        annotation.Y1 += dy;
        if (annotation.Type != ScreenshotAnnotationType.Marker)
        {
            annotation.X2 += dx;
            annotation.Y2 += dy;
        }
    }

    /// <summary>The points an annotation can be resized from, in image pixels.</summary>
    public static IReadOnlyList<(EditHandle Handle, double X, double Y)> GetHandles(ScreenshotAnnotation annotation)
    {
        var (left, top, right, bottom) = GetBounds(annotation);
        return annotation.Type switch
        {
            ScreenshotAnnotationType.Arrow => [(EditHandle.Start, annotation.X1, annotation.Y1), (EditHandle.End, annotation.X2, annotation.Y2)],
            ScreenshotAnnotationType.Marker or ScreenshotAnnotationType.Text => [(EditHandle.BottomRight, right, bottom)],
            _ => [(EditHandle.TopLeft, left, top), (EditHandle.TopRight, right, top), (EditHandle.BottomLeft, left, bottom), (EditHandle.BottomRight, right, bottom)]
        };
    }

    /// <summary>The resize handle at (x, y), or <see cref="EditHandle.None"/>.</summary>
    public static EditHandle HitTestHandle(ScreenshotAnnotation annotation, double x, double y, double tolerance)
    {
        foreach (var (handle, hx, hy) in GetHandles(annotation))
        {
            if (Math.Abs(x - hx) <= tolerance && Math.Abs(y - hy) <= tolerance)
            {
                return handle;
            }
        }

        return EditHandle.None;
    }

    /// <summary>
    /// Drags <paramref name="handle"/> to (x, y). Boxes and highlights move that corner; arrows move that end;
    /// callouts and text labels grow or shrink from their bottom-right corner (text keeps its proportions).
    /// </summary>
    public static void Resize(ScreenshotAnnotation annotation, EditHandle handle, double x, double y, int imageWidth, int imageHeight)
    {
        x = Math.Clamp(x, 0, imageWidth);
        y = Math.Clamp(y, 0, imageHeight);
        switch (annotation.Type)
        {
            case ScreenshotAnnotationType.Arrow:
                if (handle == EditHandle.Start)
                {
                    (annotation.X1, annotation.Y1) = (x, y);
                }
                else if (handle == EditHandle.End)
                {
                    (annotation.X2, annotation.Y2) = (x, y);
                }

                break;

            case ScreenshotAnnotationType.Marker:
            {
                var radius = Math.Max(Math.Abs(x - annotation.X1), Math.Abs(y - annotation.Y1));
                annotation.X2 = annotation.Y2 = Math.Clamp(radius, 8, Math.Max(imageWidth, imageHeight) / 4.0);
                break;
            }

            case ScreenshotAnnotationType.Text:
            {
                var (left, top, right, bottom) = GetBounds(annotation);
                var oldHeight = Math.Max(1, bottom - top);
                var newHeight = Math.Clamp(y - top, 12, Math.Max(12, imageHeight / 3.0));
                (annotation.X1, annotation.Y1) = (left, top);
                annotation.X2 = left + (right - left) * newHeight / oldHeight;
                annotation.Y2 = top + newHeight;
                break;
            }

            default:
            {
                var (left, top, right, bottom) = GetBounds(annotation);
                const double minimum = 6;
                switch (handle)
                {
                    case EditHandle.TopLeft:
                        (left, top) = (Math.Min(x, right - minimum), Math.Min(y, bottom - minimum));
                        break;
                    case EditHandle.TopRight:
                        (right, top) = (Math.Max(x, left + minimum), Math.Min(y, bottom - minimum));
                        break;
                    case EditHandle.BottomLeft:
                        (left, bottom) = (Math.Min(x, right - minimum), Math.Max(y, top + minimum));
                        break;
                    case EditHandle.BottomRight:
                        (right, bottom) = (Math.Max(x, left + minimum), Math.Max(y, top + minimum));
                        break;
                }

                (annotation.X1, annotation.Y1, annotation.X2, annotation.Y2) = (left, top, right, bottom);
                break;
            }
        }
    }

    /// <summary>A blur or circle moved by (dx, dy) and kept inside the image.</summary>
    public static RedactionRegion MoveRegion(RedactionRegion region, int dx, int dy, int imageWidth, int imageHeight)
    {
        var x = Math.Clamp(region.X + dx, 0, Math.Max(0, imageWidth - region.Width));
        var y = Math.Clamp(region.Y + dy, 0, Math.Max(0, imageHeight - region.Height));
        return region with { X = x, Y = y };
    }

    /// <summary>A blur or circle with one corner dragged to (x, y), kept inside the image and at least 6 pixels each way.</summary>
    public static RedactionRegion ResizeRegion(RedactionRegion region, EditHandle handle, int x, int y, int imageWidth, int imageHeight)
    {
        const int minimum = 6;
        x = Math.Clamp(x, 0, imageWidth);
        y = Math.Clamp(y, 0, imageHeight);
        var (left, top, right, bottom) = (region.X, region.Y, region.X + region.Width, region.Y + region.Height);
        switch (handle)
        {
            case EditHandle.TopLeft:
                (left, top) = (Math.Min(x, right - minimum), Math.Min(y, bottom - minimum));
                break;
            case EditHandle.TopRight:
                (right, top) = (Math.Max(x, left + minimum), Math.Min(y, bottom - minimum));
                break;
            case EditHandle.BottomLeft:
                (left, bottom) = (Math.Min(x, right - minimum), Math.Max(y, top + minimum));
                break;
            case EditHandle.BottomRight:
                (right, bottom) = (Math.Max(x, left + minimum), Math.Max(y, top + minimum));
                break;
        }

        return region with { X = left, Y = top, Width = right - left, Height = bottom - top };
    }

    /// <summary>The corner of a blur or circle at (x, y), or <see cref="EditHandle.None"/>.</summary>
    public static EditHandle HitTestRegionHandle(RedactionRegion region, double x, double y, double tolerance)
    {
        (EditHandle Handle, int X, int Y)[] corners =
        [
            (EditHandle.TopLeft, region.X, region.Y),
            (EditHandle.TopRight, region.X + region.Width, region.Y),
            (EditHandle.BottomLeft, region.X, region.Y + region.Height),
            (EditHandle.BottomRight, region.X + region.Width, region.Y + region.Height)
        ];
        foreach (var corner in corners)
        {
            if (Math.Abs(x - corner.X) <= tolerance && Math.Abs(y - corner.Y) <= tolerance)
            {
                return corner.Handle;
            }
        }

        return EditHandle.None;
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

/// <summary>Where an edit is being dragged from.</summary>
public enum EditHandle
{
    None,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Start,
    End
}
