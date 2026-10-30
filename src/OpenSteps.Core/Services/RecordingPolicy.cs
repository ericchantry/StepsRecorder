namespace OpenSteps.Core.Services;

/// <summary>
/// Settings an IT department can enforce through Group Policy / Intune by writing DWORD values to
/// HKLM\Software\Policies\OpenSteps or HKCU\Software\Policies\OpenSteps (machine values win).
/// Standard users cannot write to either key, so these cannot be overridden from the app.
/// </summary>
public sealed record RecordingPolicy(bool DisableTypedTextCapture, bool DisableVideoRecording)
{
    public const string RegistryKeyPath = @"Software\Policies\OpenSteps";
    public const string DisableTypedTextCaptureValue = "DisableTypedTextCapture";
    public const string DisableVideoRecordingValue = "DisableVideoRecording";

    public static RecordingPolicy None { get; } = new(false, false);

    /// <summary>Builds a policy from a value reader that returns the machine value first, then the user value, or null.</summary>
    public static RecordingPolicy FromValues(Func<string, int?> readValue)
    {
        return new RecordingPolicy(
            readValue(DisableTypedTextCaptureValue) is > 0,
            readValue(DisableVideoRecordingValue) is > 0);
    }
}
