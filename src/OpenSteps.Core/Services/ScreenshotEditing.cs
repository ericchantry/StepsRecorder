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
