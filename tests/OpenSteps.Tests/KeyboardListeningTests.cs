using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class KeyboardListeningTests
{
    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, true, false)]
    public void IsNeeded_OnlyWhileRecordingWithKeyboardOn(bool recording, bool enabled, bool retaking, bool expected)
    {
        Assert.Equal(expected, KeyboardListening.IsNeeded(recording, enabled, retaking));
    }

    [Fact]
    public void PolicyOff_MeansTheKeyboardIsNeverListenedTo()
    {
        var policy = RecordingPolicy.FromValues(name => name == RecordingPolicy.DisableTypedTextCaptureValue ? 1 : null);
        var userSetting = true;

        Assert.False(KeyboardListening.IsNeeded(isRecording: true, userSetting && !policy.DisableTypedTextCapture, retakingScreenshot: false));
    }
}
