namespace OpenSteps.Core.Models;

public enum ScreenshotAnnotationType
{
    Rectangle,
    Highlight,
    Arrow,
    Marker,

    /// <summary>A text label: X1,Y1 is its top-left corner, Y2 − Y1 its height (which sets the font size), X2 its right edge.</summary>
    Text
}
