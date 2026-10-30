using System.Drawing;
using System.IO;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using OpenSteps.Core.Services;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Rectangle = System.Drawing.Rectangle;

namespace OpenSteps.Capture;

public sealed record VideoRecordingResult(string Path, bool Succeeded, double DurationSeconds, int Width, int Height, string? Error);

/// <summary>
/// Records the whole virtual desktop to an H.264 MP4 using only built-in Windows components:
/// GDI screen frames are fed through a MediaStreamSource into MediaTranscoder.
/// Frame timestamps come from the shared <see cref="RecordingClock"/>, so paused time is
/// skipped and input-event offsets line up with the video.
/// </summary>
public sealed class ScreenVideoRecorder : IAsyncDisposable
{
    private const int MaxOutputWidth = 3840;
    private const int MaxOutputHeight = 2160;
    private const int MaxPooledBuffers = 6;
    private static readonly TimeSpan ClickRingDuration = TimeSpan.FromMilliseconds(600);

    private readonly RecordingClock _clock;
    private readonly TimeSpan _frameDuration;
    private readonly object _poolSync = new();
    private readonly Stack<byte[]> _bufferPool = new();
    private readonly object _clickSync = new();

    private Rectangle _sourceBounds;
    private int _outputWidth;
    private int _outputHeight;
    private Bitmap? _captureBitmap;
    private Bitmap? _scaledBitmap;
    private FileStream? _fileStream;
    private Task? _transcodeTask;
    private volatile bool _stopRequested;
    private TimeSpan _nextFrameTime;
    private TimeSpan _lastTimestamp = TimeSpan.FromTicks(-1);
    private (int X, int Y, DateTimeOffset At)? _lastClick;
    private Exception? _frameError;
    private string _outputPath = string.Empty;

    public ScreenVideoRecorder(RecordingClock clock, int framesPerSecond = 10)
    {
        _clock = clock;
        _frameDuration = TimeSpan.FromSeconds(1.0 / Math.Clamp(framesPerSecond, 1, 30));
        FramesPerSecond = Math.Clamp(framesPerSecond, 1, 30);
    }

    public int FramesPerSecond { get; }

    public bool IsRecording => _transcodeTask is { IsCompleted: false };

    /// <summary>
    /// Prepares the encoder and starts writing to <paramref name="outputPath"/>. Start the clock after this
    /// returns: encoder set-up can take a moment, and frames are only timestamped once the clock runs.
    /// </summary>
    public async Task StartAsync(string outputPath)
    {
        if (_transcodeTask is not null)
        {
            throw new InvalidOperationException("This recorder has already been started.");
        }

        _outputPath = outputPath;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        _sourceBounds = new Rectangle(virtualScreen.X, virtualScreen.Y, virtualScreen.Width & ~1, virtualScreen.Height & ~1);
        var scale = Math.Min(1.0, Math.Min(MaxOutputWidth / (double)_sourceBounds.Width, MaxOutputHeight / (double)_sourceBounds.Height));
        _outputWidth = Math.Max(2, (int)(_sourceBounds.Width * scale) & ~1);
        _outputHeight = Math.Max(2, (int)(_sourceBounds.Height * scale) & ~1);
        _captureBitmap = new Bitmap(_sourceBounds.Width, _sourceBounds.Height, PixelFormat.Format32bppRgb);
        if (_outputWidth != _sourceBounds.Width || _outputHeight != _sourceBounds.Height)
        {
            _scaledBitmap = new Bitmap(_outputWidth, _outputHeight, PixelFormat.Format32bppRgb);
        }

        var input = VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)_outputWidth, (uint)_outputHeight);
        var source = new MediaStreamSource(new VideoStreamDescriptor(input))
        {
            BufferTime = TimeSpan.Zero,
            CanSeek = false
        };
        source.Starting += (_, args) => args.Request.SetActualStartPosition(TimeSpan.Zero);
        source.SampleRequested += OnSampleRequested;

        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
        profile.Audio = null;
        profile.Video.Width = (uint)_outputWidth;
        profile.Video.Height = (uint)_outputHeight;
        profile.Video.FrameRate.Numerator = (uint)FramesPerSecond;
        profile.Video.FrameRate.Denominator = 1;
        profile.Video.PixelAspectRatio.Numerator = 1;
        profile.Video.PixelAspectRatio.Denominator = 1;
        profile.Video.Bitrate = (uint)Math.Clamp(_outputWidth * (double)_outputHeight * FramesPerSecond * 0.08, 1_000_000, 12_000_000);

        _fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
        var prepared = await PrepareAsync(source, profile, hardwareAcceleration: true)
            ?? await PrepareAsync(source, profile, hardwareAcceleration: false);
        if (prepared is null)
        {
            await DisposeStreamAsync();
            throw new InvalidOperationException("Windows could not set up an H.264 video encoder on this computer.");
        }

        _transcodeTask = prepared.TranscodeAsync().AsTask();
    }

    /// <summary>Draws a short-lived ring at a click position so clicks are visible in the video.</summary>
    public void MarkClick(int x, int y)
    {
        lock (_clickSync)
        {
            _lastClick = (x, y, DateTimeOffset.UtcNow);
        }
    }

    public async Task<VideoRecordingResult> StopAsync()
    {
        _stopRequested = true;
        string? error = null;
        if (_transcodeTask is not null)
        {
            try
            {
                var finished = await Task.WhenAny(_transcodeTask, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
                if (finished != _transcodeTask)
                {
                    error = "The video encoder did not finish within 60 seconds.";
                }
                else
                {
                    await _transcodeTask.ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }

        error ??= _frameError?.Message;
        await DisposeStreamAsync().ConfigureAwait(false);
        _captureBitmap?.Dispose();
        _captureBitmap = null;
        _scaledBitmap?.Dispose();
        _scaledBitmap = null;

        var succeeded = error is null && File.Exists(_outputPath) && new FileInfo(_outputPath).Length > 0;
        return new VideoRecordingResult(_outputPath, succeeded, Math.Max(0, _lastTimestamp.TotalSeconds), _outputWidth, _outputHeight, error);
    }

    public async ValueTask DisposeAsync()
    {
        if (_transcodeTask is not null && !_transcodeTask.IsCompleted)
        {
            await StopAsync();
        }

        await DisposeStreamAsync();
        _captureBitmap?.Dispose();
        _scaledBitmap?.Dispose();
    }

    private async Task<PrepareTranscodeResult?> PrepareAsync(MediaStreamSource source, MediaEncodingProfile profile, bool hardwareAcceleration)
    {
        try
        {
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = hardwareAcceleration };
            _fileStream!.SetLength(0);
            _fileStream.Position = 0;
            var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, _fileStream.AsRandomAccessStream(), profile);
            return prepared.CanTranscode ? prepared : null;
        }
        catch (Exception) when (hardwareAcceleration)
        {
            return null;
        }
    }

    private async Task DisposeStreamAsync()
    {
        if (_fileStream is not null)
        {
            await _fileStream.DisposeAsync().ConfigureAwait(false);
            _fileStream = null;
        }
    }

    private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
    {
        var request = args.Request;
        if (_stopRequested)
        {
            request.Sample = null;
            return;
        }

        var deferral = request.GetDeferral();
        _ = Task.Run(async () =>
        {
            try
            {
                request.Sample = await NextSampleAsync();
            }
            catch (Exception ex)
            {
                _frameError = ex;
                request.Sample = null;
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    private async Task<MediaStreamSample?> NextSampleAsync()
    {
        while (!_stopRequested)
        {
            if (_clock.IsPaused)
            {
                await Task.Delay(50);
                continue;
            }

            var now = _clock.Elapsed;
            if (now < _nextFrameTime)
            {
                var wait = _nextFrameTime - now;
                await Task.Delay(wait > TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : wait);
                continue;
            }

            var buffer = RentBuffer();
            CaptureFrame(buffer);
            var timestamp = now > _lastTimestamp ? now : _lastTimestamp + TimeSpan.FromMilliseconds(1);
            _lastTimestamp = timestamp;
            _nextFrameTime = timestamp + _frameDuration;

            var sample = MediaStreamSample.CreateFromBuffer(buffer.AsBuffer(), timestamp);
            sample.Duration = _frameDuration;
            sample.KeyFrame = true;
            sample.Processed += (_, _) => ReturnBuffer(buffer);
            return sample;
        }

        return null;
    }

    private void CaptureFrame(byte[] destination)
    {
        var capture = _captureBitmap!;
        using (var graphics = Graphics.FromImage(capture))
        {
            try
            {
                graphics.CopyFromScreen(_sourceBounds.X, _sourceBounds.Y, 0, 0, _sourceBounds.Size, CopyPixelOperation.SourceCopy);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The secure desktop (UAC prompt, lock screen) cannot be captured; keep the previous frame.
            }

            DrawClickRing(graphics);
            DrawCursor(graphics);
        }

        var frame = capture;
        if (_scaledBitmap is not null)
        {
            using var scaledGraphics = Graphics.FromImage(_scaledBitmap);
            scaledGraphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            scaledGraphics.DrawImage(capture, 0, 0, _outputWidth, _outputHeight);
            frame = _scaledBitmap;
        }

        var data = frame.LockBits(new Rectangle(0, 0, _outputWidth, _outputHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            // Media Foundation treats uncompressed RGB as bottom-up, so write the rows in reverse.
            var rowBytes = _outputWidth * 4;
            for (var y = 0; y < _outputHeight; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, destination, (_outputHeight - 1 - y) * rowBytes, rowBytes);
            }
        }
        finally
        {
            frame.UnlockBits(data);
        }
    }

    private void DrawClickRing(Graphics graphics)
    {
        (int X, int Y, DateTimeOffset At)? click;
        lock (_clickSync)
        {
            click = _lastClick;
        }

        if (click is not { } value || DateTimeOffset.UtcNow - value.At > ClickRingDuration)
        {
            return;
        }

        var x = value.X - _sourceBounds.X;
        var y = value.Y - _sourceBounds.Y;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(Color.FromArgb(70, 255, 196, 0));
        using var pen = new Pen(Color.FromArgb(230, 255, 140, 0), 3);
        graphics.FillEllipse(fill, x - 18, y - 18, 36, 36);
        graphics.DrawEllipse(pen, x - 18, y - 18, 36, 36);
    }

    private void DrawCursor(Graphics graphics)
    {
        var info = new NativeMethods.CURSORINFO { Size = Marshal.SizeOf<NativeMethods.CURSORINFO>() };
        if (!NativeMethods.GetCursorInfo(ref info) || (info.Flags & NativeMethods.CURSOR_SHOWING) == 0 || info.Cursor == IntPtr.Zero)
        {
            return;
        }

        var hotspotX = 0;
        var hotspotY = 0;
        if (NativeMethods.GetIconInfo(info.Cursor, out var iconInfo))
        {
            hotspotX = iconInfo.HotspotX;
            hotspotY = iconInfo.HotspotY;
            if (iconInfo.MaskBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(iconInfo.MaskBitmap);
            }

            if (iconInfo.ColorBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(iconInfo.ColorBitmap);
            }
        }

        var hdc = graphics.GetHdc();
        try
        {
            NativeMethods.DrawIconEx(
                hdc,
                info.ScreenPosition.X - _sourceBounds.X - hotspotX,
                info.ScreenPosition.Y - _sourceBounds.Y - hotspotY,
                info.Cursor,
                0,
                0,
                0,
                IntPtr.Zero,
                NativeMethods.DI_NORMAL);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
    }

    private byte[] RentBuffer()
    {
        lock (_poolSync)
        {
            return _bufferPool.Count > 0 ? _bufferPool.Pop() : new byte[_outputWidth * _outputHeight * 4];
        }
    }

    private void ReturnBuffer(byte[] buffer)
    {
        lock (_poolSync)
        {
            if (_bufferPool.Count < MaxPooledBuffers && buffer.Length == _outputWidth * _outputHeight * 4)
            {
                _bufferPool.Push(buffer);
            }
        }
    }
}
