using System.IO;
using OpenSteps.Capture;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class ScreenVideoRecorderTests
{
    /// <summary>
    /// Records the real screen, so it only runs when OPENSTEPS_RUN_SCREEN_TESTS=1 is set
    /// in an interactive Windows session. CI and normal test runs skip it.
    /// </summary>
    [Fact]
    public async Task RecordsPlayableMp4AndSkipsPausedTime()
    {
        if (Environment.GetEnvironmentVariable("OPENSTEPS_RUN_SCREEN_TESTS") != "1")
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), "OpenStepsTests", Guid.NewGuid().ToString("N"), "recording.mp4");
        var clock = new RecordingClock();
        var recorder = new ScreenVideoRecorder(clock, framesPerSecond: 10);

        // Encoder set-up can take a second or two, so the clock starts once the recorder is ready.
        await recorder.StartAsync(path);
        clock.Start();

        await Task.Delay(1500);
        recorder.MarkClick(100, 100);
        clock.Pause();
        await Task.Delay(1500);
        clock.Resume();
        await Task.Delay(1500);
        clock.Stop();
        var result = await recorder.StopAsync();

        Assert.True(result.Succeeded, result.Error);
        Assert.True(new FileInfo(path).Length > 1000);
        Assert.InRange(result.DurationSeconds, 2.0, 3.6);

        var header = new byte[12];
        await using (var stream = File.OpenRead(path))
        {
            await stream.ReadExactlyAsync(header);
        }

        Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(header, 4, 4));
    }
}
