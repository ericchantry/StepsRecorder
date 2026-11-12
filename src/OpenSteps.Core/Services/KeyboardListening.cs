namespace OpenSteps.Core.Services;

/// <summary>
/// Decides when OpenSteps may listen to the keyboard at all. The low-level keyboard hook is only installed while
/// this is true, so with keyboard recording switched off (by the user or by IT policy) no keystroke ever reaches
/// OpenSteps: not typed text, not key names, not shortcuts.
/// </summary>
public static class KeyboardListening
{
    public static bool IsNeeded(bool isRecording, bool keyboardRecordingEnabled, bool retakingScreenshot)
    {
        return isRecording && keyboardRecordingEnabled && !retakingScreenshot;
    }
}
