using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Capture;

public sealed class ScreenshotService
{
    private readonly MonitorService _monitorService;
    private readonly WindowBoundsService _windowBoundsService;

    public ScreenshotService(MonitorService? monitorService = null, WindowBoundsService? windowBoundsService = null)
    {
        _monitorService = monitorService ?? new MonitorService();
        _windowBoundsService = windowBoundsService ?? new WindowBoundsService();
    }

    /// <summary>Device name of the monitor captured in <see cref="ScreenshotMode.Monitor"/> mode.</summary>
    public string? SelectedMonitorDeviceName { get; set; }

    /// <summary>Window captured in <see cref="ScreenshotMode.Window"/> mode.</summary>
    public IntPtr SelectedWindowHandle { get; set; }

    /// <summary>Screen area (physical pixels) captured in <see cref="ScreenshotMode.Region"/> mode.</summary>
    public Rectangle? SelectedRegion { get; set; }

    /// <summary>
    /// Finds password and other sensitive fields so they can be covered before a screenshot is saved.
    /// Share one instance with the video recorder.
    /// </summary>
    public SecureFieldScanner? SecureFields { get; set; } = new();

    /// <summary>Cover password and sensitive fields in screenshots. On by default.</summary>
    public bool HideSensitiveFields { get; set; } = true;

    private static readonly TimeSpan SecureFieldTimeout = TimeSpan.FromMilliseconds(400);

    public Task<string> CaptureVirtualDesktopAsync(string sessionDirectory, int stepIndex, int clickX, int clickY, CancellationToken cancellationToken = default)
    {
        return CaptureVirtualDesktopAsync(sessionDirectory, $"step-{stepIndex:000}.png", clickX, clickY, drawHighlight: true, cancellationToken);
    }

    public async Task<string> CaptureVirtualDesktopAsync(string sessionDirectory, string fileName, int clickX, int clickY, bool drawHighlight = true, CancellationToken cancellationToken = default)
    {
        var result = await CaptureAsync(sessionDirectory, fileName, clickX, clickY, ScreenshotCaptureMode.FullVirtualDesktop, drawHighlight, cancellationToken);
        return result.FilePath;
    }

    public Task<ScreenshotCaptureResult> CaptureAsync(
        string sessionDirectory,
        int stepIndex,
        int clickX,
        int clickY,
        ScreenshotMode requestedMode,
        bool drawHighlight = true,
        CancellationToken cancellationToken = default)
    {
        return CaptureAsync(sessionDirectory, $"step-{stepIndex:000}.png", clickX, clickY, requestedMode, IntPtr.Zero, drawHighlight, ClickMarkerKind.Left, cancellationToken);
    }

    public Task<ScreenshotCaptureResult> CaptureAsync(
        string sessionDirectory,
        int stepIndex,
        int clickX,
        int clickY,
        ScreenshotMode requestedMode,
        IntPtr targetHwnd,
        bool drawHighlight = true,
        ClickMarkerKind markerKind = ClickMarkerKind.Left,
        CancellationToken cancellationToken = default)
    {
        return CaptureAsync(sessionDirectory, $"step-{stepIndex:000}.png", clickX, clickY, requestedMode, targetHwnd, drawHighlight, markerKind, cancellationToken);
    }

    public Task<ScreenshotCaptureResult> CaptureAsync(
        string sessionDirectory,
        string fileName,
        int clickX,
        int clickY,
        ScreenshotMode requestedMode,
        bool drawHighlight = true,
        CancellationToken cancellationToken = default)
    {
        return CaptureAsync(sessionDirectory, fileName, clickX, clickY, requestedMode, IntPtr.Zero, drawHighlight, ClickMarkerKind.Left, cancellationToken);
    }

    /// <summary>
    /// Captures the area chosen by <paramref name="requestedMode"/>, covers password and other sensitive fields,
    /// and (when <paramref name="drawHighlight"/> is set) draws a marker of the given kind at the click point.
    /// </summary>
    public Task<ScreenshotCaptureResult> CaptureAsync(
        string sessionDirectory,
        string fileName,
        int clickX,
        int clickY,
        ScreenshotMode requestedMode,
        IntPtr targetHwnd,
        bool drawHighlight = true,
        ClickMarkerKind markerKind = ClickMarkerKind.Left,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(sessionDirectory);
            var request = new RenderRequest(Path.Combine(sessionDirectory, fileName), clickX, clickY, drawHighlight, markerKind);

            switch (requestedMode)
            {
                case ScreenshotMode.ActiveWindow:
                    try
                    {
                        return CaptureWindow(request, targetHwnd, ScreenshotMode.ActiveWindow, cancellationToken);
                    }
                    catch (Exception ex) when (IsCaptureFailure(ex))
                    {
                        // Fall back to the monitor that was clicked rather than every monitor.
                        return ScreenshotFallbackFactory.WithFallbackMetadata(CaptureLegacy(request, ScreenshotCaptureMode.MonitorContainingClick, cancellationToken), requestedMode, ex.Message);
                    }

                case ScreenshotMode.Monitor:
                    return CaptureSelectedMonitor(request, cancellationToken);

                case ScreenshotMode.Window:
                    try
                    {
                        if (SelectedWindowHandle == IntPtr.Zero || WindowEnumerator.GetVisibleBounds(SelectedWindowHandle) is null)
                        {
                            throw new InvalidOperationException("The chosen window is closed or minimized.");
                        }

                        return CaptureWindow(request, SelectedWindowHandle, ScreenshotMode.Window, cancellationToken);
                    }
                    catch (Exception ex) when (IsCaptureFailure(ex))
                    {
                        return ScreenshotFallbackFactory.WithFallbackMetadata(CaptureLegacy(request, ScreenshotCaptureMode.MonitorContainingClick, cancellationToken), requestedMode, ex.Message);
                    }

                case ScreenshotMode.Region:
                    if (SelectedRegion is not { Width: > 0, Height: > 0 } region)
                    {
                        return ScreenshotFallbackFactory.WithFallbackMetadata(CaptureLegacy(request, ScreenshotCaptureMode.FullVirtualDesktop, cancellationToken), requestedMode, "No screen region has been selected.");
                    }

                    return CaptureRegion(request, region, cancellationToken);

                default:
                    return CaptureLegacy(request, ScreenshotCaptureMode.FullVirtualDesktop, cancellationToken);
            }
        }, cancellationToken);
    }

    public Task<ScreenshotCaptureResult> CaptureAsync(
        string sessionDirectory,
        int stepIndex,
        int clickX,
        int clickY,
        ScreenshotCaptureMode captureMode = ScreenshotCaptureMode.MonitorContainingClick,
        bool drawHighlight = true,
        CancellationToken cancellationToken = default)
    {
        return CaptureAsync(sessionDirectory, $"step-{stepIndex:000}.png", clickX, clickY, captureMode, drawHighlight, cancellationToken);
    }

    public Task<ScreenshotCaptureResult> CaptureAsync(
        string sessionDirectory,
        string fileName,
        int clickX,
        int clickY,
        ScreenshotCaptureMode captureMode,
        bool drawHighlight = true,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(sessionDirectory);
            return CaptureLegacy(new RenderRequest(Path.Combine(sessionDirectory, fileName), clickX, clickY, drawHighlight, ClickMarkerKind.Left), captureMode, cancellationToken);
        }, cancellationToken);
    }

    private ScreenshotCaptureResult CaptureWindow(RenderRequest request, IntPtr targetHwnd, ScreenshotMode reportedMode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bounds = targetHwnd == IntPtr.Zero
            ? _windowBoundsService.GetForegroundWindowBounds()
            : _windowBoundsService.GetWindowBounds(targetHwnd);
        var area = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        var rendered = Render(request, area, targetHwnd);
        return BuildResult(request, rendered, ScreenshotCaptureMode.FullVirtualDesktop, _monitorService.GetMonitorFromPoint(request.ClickX, request.ClickY), reportedMode, bounds.Source);
    }

    private ScreenshotCaptureResult CaptureSelectedMonitor(RenderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var monitor = _monitorService.FindMonitor(SelectedMonitorDeviceName);
        var area = Rectangle.FromLTRB(monitor.BoundsLeft, monitor.BoundsTop, monitor.BoundsRight, monitor.BoundsBottom);
        var rendered = Render(request, area, IntPtr.Zero);
        return BuildResult(request, rendered, ScreenshotCaptureMode.MonitorContainingClick, monitor, ScreenshotMode.Monitor, $"SelectedMonitor {monitor.DeviceName}");
    }

    private ScreenshotCaptureResult CaptureRegion(RenderRequest request, Rectangle region, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rendered = Render(request, region, IntPtr.Zero);
        var monitor = _monitorService.GetMonitorFromPoint(region.X + region.Width / 2, region.Y + region.Height / 2);
        return BuildResult(request, rendered, ScreenshotCaptureMode.FullVirtualDesktop, monitor, ScreenshotMode.Region, "SelectedRegion");
    }

    private ScreenshotCaptureResult CaptureLegacy(RenderRequest request, ScreenshotCaptureMode captureMode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Mouse hooks, Win32 monitor bounds, and Graphics.CopyFromScreen all use physical
        // virtual-screen pixels. WPF UI coordinates must be converted before reaching here.
        var monitor = _monitorService.GetMonitorFromPoint(request.ClickX, request.ClickY);
        var area = captureMode == ScreenshotCaptureMode.FullVirtualDesktop
            ? SystemInformation.VirtualScreen
            : Rectangle.FromLTRB(monitor.BoundsLeft, monitor.BoundsTop, monitor.BoundsRight, monitor.BoundsBottom);
        var rendered = Render(request, area, IntPtr.Zero);
        return BuildResult(
            request,
            rendered,
            captureMode,
            monitor,
            captureMode == ScreenshotCaptureMode.MonitorContainingClick ? ScreenshotMode.Monitor : ScreenshotMode.FullDesktop,
            captureMode == ScreenshotCaptureMode.FullVirtualDesktop ? "VirtualScreen" : "MonitorContainingClick",
            requestedMode: ScreenshotMode.FullDesktop);
    }

    /// <summary>Copies <paramref name="area"/> from the screen, covers sensitive fields, draws the click marker and saves a PNG.</summary>
    private Rendered Render(RenderRequest request, Rectangle area, IntPtr window)
    {
        var (localX, localY) = MonitorCoordinateMapper.ToLocalPoint(request.ClickX, request.ClickY, area.Left, area.Top);
        var inside = ScreenshotCoordinateMapper.IsInsideCapturedBounds(localX, localY, area.Width, area.Height);
        var captured = new ScreenBounds(area.X, area.Y, area.Width, area.Height);

        IReadOnlyList<ScreenBounds> hidden = [];
        if (HideSensitiveFields && SecureFields is { } scanner)
        {
            try
            {
                hidden = CaptureRegionMath.ToLocal(scanner.ScanNow(window, SecureFieldTimeout), captured);
            }
            catch
            {
                // Field detection is best effort; typed text is still masked separately.
            }
        }

        using var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(area.Left, area.Top, 0, 0, area.Size, CopyPixelOperation.SourceCopy);
            CaptureOverlayRenderer.CoverSensitiveAreas(graphics, hidden);
            if (request.DrawHighlight && inside)
            {
                CaptureOverlayRenderer.DrawScreenshotMarker(graphics, localX, localY, request.MarkerKind, bitmap.Size);
            }
        }

        bitmap.Save(request.FilePath, ImageFormat.Png);
        return new Rendered(captured, localX, localY, inside, hidden.Count);
    }

    private static ScreenshotCaptureResult BuildResult(
        RenderRequest request,
        Rendered rendered,
        ScreenshotCaptureMode captureMode,
        DisplayMonitorInfo monitor,
        ScreenshotMode actualMode,
        string? boundsSource,
        ScreenshotMode? requestedMode = null)
    {
        var area = rendered.Area;
        return new ScreenshotCaptureResult
        {
            FilePath = request.FilePath,
            CaptureMode = captureMode,
            Monitor = monitor,
            CaptureLeft = area.X,
            CaptureTop = area.Y,
            CaptureRight = area.X + area.Width,
            CaptureBottom = area.Y + area.Height,
            LocalClickX = rendered.LocalX,
            LocalClickY = rendered.LocalY,
            ScreenshotWidth = area.Width,
            ScreenshotHeight = area.Height,
            RequestedScreenshotMode = requestedMode ?? actualMode,
            ActualScreenshotMode = actualMode,
            UsedScreenshotFallback = false,
            CapturedBounds = area,
            GlobalClickX = request.ClickX,
            GlobalClickY = request.ClickY,
            HighlightX = rendered.LocalX,
            HighlightY = rendered.LocalY,
            HighlightWasInsideCapturedBounds = rendered.Inside,
            BoundsSource = boundsSource,
            SensitiveFieldsHidden = rendered.HiddenFields
        };
    }

    private static bool IsCaptureFailure(Exception ex)
    {
        return ex is InvalidOperationException or IOException or System.Runtime.InteropServices.ExternalException or ArgumentException;
    }

    private sealed record RenderRequest(string FilePath, int ClickX, int ClickY, bool DrawHighlight, ClickMarkerKind MarkerKind);

    private sealed record Rendered(ScreenBounds Area, int LocalX, int LocalY, bool Inside, int HiddenFields);
}
