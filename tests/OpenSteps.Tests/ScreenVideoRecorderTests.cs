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

        // Decode a frame back out of the file with Windows' own media stack to prove it is playable.
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        var clip = await Windows.Media.Editing.MediaClip.CreateFromFileAsync(file);
        Assert.InRange(clip.OriginalDuration.TotalSeconds, 2.0, 3.6);
        var composition = new Windows.Media.Editing.MediaComposition();
        composition.Clips.Add(clip);
        using var thumbnail = await composition.GetThumbnailAsync(TimeSpan.FromSeconds(1), 0, 0, Windows.Media.Editing.VideoFramePrecision.NearestFrame);
        Assert.True(thumbnail.Size > 0);
        await using var output = File.Create(Path.ChangeExtension(path, ".frame.jpg"));
        await thumbnail.AsStreamForRead().CopyToAsync(output);
    }
}

public sealed class VideoFrameLayoutTests
{
    [Theory]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(1921, 1081, 1920, 1080)]
    [InlineData(5760, 1080, 3840, 720)]
    [InlineData(7680, 4320, 3840, 2160)]
    public void OutputSize_FitsEncoderLimitsWithEvenDimensions(int width, int height, int expectedWidth, int expectedHeight)
    {
        Assert.Equal(new System.Drawing.Size(expectedWidth, expectedHeight), VideoFrameLayout.OutputSize(new System.Drawing.Size(width, height)));
    }

    [Fact]
    public void Fit_LetterboxesSmallerMonitorIntoLargerFrame()
    {
        var target = VideoFrameLayout.Fit(new System.Drawing.Size(1920, 1080), new System.Drawing.Size(2560, 1600));

        Assert.Equal(new System.Drawing.Rectangle(0, 80, 2560, 1440), target);
    }

    [Fact]
    public void Fit_PillarboxesPortraitMonitor()
    {
        var target = VideoFrameLayout.Fit(new System.Drawing.Size(1080, 1920), new System.Drawing.Size(1920, 1080));

        Assert.Equal(1080, target.Height);
        Assert.Equal(608, target.Width);
        Assert.Equal((1920 - 608) / 2, target.X);
    }

    [Fact]
    public void Fit_SameSizeFillsFrame()
    {
        Assert.Equal(new System.Drawing.Rectangle(0, 0, 1920, 1080), VideoFrameLayout.Fit(new System.Drawing.Size(1920, 1080), new System.Drawing.Size(1920, 1080)));
    }
}

public sealed class VideoCaptureTargetScreenTests
{
    /// <summary>Records the primary monitor only, then the active window's monitor. Needs OPENSTEPS_RUN_SCREEN_TESTS=1.</summary>
    [Fact]
    public async Task SingleMonitorAndFollowTargetsRecordAtMonitorSize()
    {
        if (Environment.GetEnvironmentVariable("OPENSTEPS_RUN_SCREEN_TESTS") != "1")
        {
            return;
        }

        var monitors = new MonitorService();
        var primary = monitors.GetAllMonitors().First(m => m.IsPrimary);
        foreach (var target in new[]
                 {
                     VideoCaptureTarget.SingleMonitor(primary, "primary"),
                     VideoCaptureTarget.FollowActiveWindowMonitor(monitors)
                 })
        {
            var path = Path.Combine(Path.GetTempPath(), "OpenStepsTests", Guid.NewGuid().ToString("N"), "recording.mp4");
            var clock = new RecordingClock();
            var recorder = new ScreenVideoRecorder(clock, 10, target);
            await recorder.StartAsync(path);
            clock.Start();
            await Task.Delay(1500);
            clock.Stop();
            var result = await recorder.StopAsync();

            Assert.True(result.Succeeded, result.Error);
            var expected = VideoFrameLayout.OutputSize(target.FrameSize);
            Assert.Equal(expected.Width, result.Width);
            Assert.Equal(expected.Height, result.Height);
            Assert.True(new FileInfo(path).Length > 1000);
        }
    }
}
