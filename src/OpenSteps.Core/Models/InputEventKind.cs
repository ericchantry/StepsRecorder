namespace OpenSteps.Core.Models;

public enum InputEventKind
{
    /// <summary>A printable key that produces text.</summary>
    KeyText = 0,

    /// <summary>A named non-text key such as Enter, Tab, F5 or Home.</summary>
    Key = 1,

    /// <summary>A key pressed together with Ctrl, Alt or the Windows key.</summary>
    Shortcut = 2,

    Click = 3,
    RightClick = 4,
    DoubleClick = 5,
    MiddleClick = 6,
    Scroll = 7,
    HorizontalScroll = 8,
    Drag = 9
}
