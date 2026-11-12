namespace OpenSteps.Core.Services;

/// <summary>
/// Decides when OpenSteps may listen to the keyboard at all. The system-wide keyboard hook is installed only while
/// every condition holds, and removed the moment one stops holding, so that switching keyboard recording off
/// (by the user or by IT policy) means no keystroke ever reaches OpenSteps.
/// </summary>
public static class KeyboardHookRules
{
    public static bool ShouldInstall(
        bool isRecording,
        bool isPaused,
        bool keyboardCaptureEnabled,
        bool disabledByPolicy,
        bool capturingReplacementScreenshot)
    {
        return isRecording
            && !isPaused
            && keyboardCaptureEnabled
            && !disabledByPolicy
            && !capturingReplacementScreenshot;
    }
}
