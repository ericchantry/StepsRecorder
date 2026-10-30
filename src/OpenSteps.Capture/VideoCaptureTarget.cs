using System.Drawing;
using System.Windows.Forms;

namespace OpenSteps.Capture;

/// <summary>
/// What the session video covers. <see cref="GetSourceBounds"/> is asked for the screen area before every
/// frame; returning null keeps the previous area. Frames whose area differs from <see cref="FrameSize"/>
/// are scaled to fit with black bars.
/// </summary>
public sealed class VideoCaptureTarget
{
    private VideoCaptureTarget(Size frameSize, Func<Rectangle?> getSourceBounds, string description)
    {
        FrameSize = frameSize;
        GetSourceBounds = getSourceBounds;
        Description = description;
    }

    /// <summary>Size the video is laid out for (before any downscaling to the encoder limit).</summary>
    public Size FrameSize { get; }

    public Func<Rectangle?> GetSourceBounds { get; }

    public string Description { get; }

    public static VideoCaptureTarget AllMonitors()
    {
        return new VideoCaptureTarget(SystemInformation.VirtualScreen.Size, () => SystemInformation.VirtualScreen, "All monitors");
    }

    public static VideoCaptureTarget SingleMonitor(DisplayMonitorInfo monitor, string description)
    {
        var bounds = Rectangle.FromLTRB(monitor.BoundsLeft, monitor.BoundsTop, monitor.BoundsRight, monitor.BoundsBottom);
        return new VideoCaptureTarget(bounds.Size, () => bounds, description);
    }

    /// <summary>
    /// Follows the monitor that the foreground window is on, switching as focus moves between monitors.
    /// OpenSteps' own windows are ignored so clicking the recording bar does not switch monitors.
    /// </summary>
    public static VideoCaptureTarget FollowActiveWindowMonitor(MonitorService monitorService)
    {
        var monitors = monitorService.GetAllMonitors();
        var frame = new Size(monitors.Max(m => m.Width), monitors.Max(m => m.Height));
        var ownProcessId = (uint)Environment.ProcessId;

        Rectangle? Resolve()
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return null;
            }

            NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
            return processId == ownProcessId ? null : MonitorService.GetMonitorBoundsForWindow(foreground);
        }

        return new VideoCaptureTarget(frame, Resolve, "Monitor with the active window");
    }
}

/// <summary>Pure layout maths for video frames.</summary>
public static class VideoFrameLayout
{
    public const int MaxWidth = 3840;
    public const int MaxHeight = 2160;

    /// <summary>Encoder frame size: scaled down to fit 3840×2160 if needed, with even dimensions (H.264 needs them).</summary>
    public static Size OutputSize(Size basis)
    {
        var width = Math.Max(2, basis.Width);
        var height = Math.Max(2, basis.Height);
        var scale = Math.Min(1.0, Math.Min(MaxWidth / (double)width, MaxHeight / (double)height));
        return new Size(Math.Max(2, (int)(width * scale) & ~1), Math.Max(2, (int)(height * scale) & ~1));
    }

    /// <summary>Where a source of <paramref name="source"/> size goes inside <paramref name="frame"/>: scaled to fit and centred.</summary>
    public static Rectangle Fit(Size source, Size frame)
    {
        if (source.Width <= 0 || source.Height <= 0)
        {
            return new Rectangle(Point.Empty, frame);
        }

        var scale = Math.Min(frame.Width / (double)source.Width, frame.Height / (double)source.Height);
        var width = Math.Min(frame.Width, (int)Math.Round(source.Width * scale));
        var height = Math.Min(frame.Height, (int)Math.Round(source.Height * scale));
        return new Rectangle((frame.Width - width) / 2, (frame.Height - height) / 2, width, height);
    }
}
