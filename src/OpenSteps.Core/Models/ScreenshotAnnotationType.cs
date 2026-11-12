namespace OpenSteps.Core.Models;

public enum ScreenshotAnnotationType
{
    Rectangle,
    Highlight,
    Arrow,
    Marker,

    /// <summary>A text label. X1/Y1 is its top-left corner and X2 its font size, in image pixels.</summary>
    Text
}
