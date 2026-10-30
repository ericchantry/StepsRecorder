namespace OpenSteps.Capture;

public enum KeyboardInputKind
{
    Text,
    SpecialKey,
    Shortcut,

    /// <summary>Recorded on the input timeline only; never creates a guide step.</summary>
    TimelineOnly
}
