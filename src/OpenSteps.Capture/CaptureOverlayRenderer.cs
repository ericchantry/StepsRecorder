using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Capture;

/// <summary>
/// Draws what OpenSteps adds on top of captured pixels: click markers (colour and label per mouse action),
/// the cursor halo in the video, and the cover over password and other sensitive fields.
/// All coordinates are in the captured image's pixels.
/// </summary>
public static class CaptureOverlayRenderer
{
    private static readonly Color HaloColor = Color.FromArgb(255, 214, 10);
    private static readonly Color SecureFill = Color.FromArgb(255, 31, 41, 55);
    private static readonly Color SecureText = Color.FromArgb(255, 209, 213, 219);

    public static Color GetColor(ClickMarkerKind kind)
    {
        return kind switch
        {
            ClickMarkerKind.Right => Color.FromArgb(255, 37, 99, 235),
            ClickMarkerKind.Double => Color.FromArgb(255, 147, 51, 234),
            ClickMarkerKind.Middle => Color.FromArgb(255, 22, 163, 74),
            _ => Color.FromArgb(255, 220, 38, 38)
        };
    }

    /// <summary>
    /// Screenshot click marker: a ring with a white edge (visible on light and dark backgrounds), a centre dot
    /// on the exact click point, and a badge naming the action ("Click", "Right-click", "Double-click").
    /// </summary>
    public static void DrawScreenshotMarker(Graphics graphics, int x, int y, ClickMarkerKind kind, Size imageSize)
    {
        const int radius = 22;
        var color = GetColor(kind);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var fill = new SolidBrush(Color.FromArgb(46, color)))
        {
            graphics.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
        }

        using (var edge = new Pen(Color.FromArgb(235, 255, 255, 255), 8))
        {
            graphics.DrawEllipse(edge, x - radius, y - radius, radius * 2, radius * 2);
        }

        using (var ring = new Pen(color, 4.5f))
        {
            graphics.DrawEllipse(ring, x - radius, y - radius, radius * 2, radius * 2);
            if (kind == ClickMarkerKind.Double)
            {
                const int inner = radius - 8;
                graphics.DrawEllipse(ring, x - inner, y - inner, inner * 2, inner * 2);
            }
        }

        using (var dot = new SolidBrush(color))
        using (var dotEdge = new Pen(Color.White, 1.5f))
        {
            graphics.FillEllipse(dot, x - 3.5f, y - 3.5f, 7, 7);
            graphics.DrawEllipse(dotEdge, x - 3.5f, y - 3.5f, 7, 7);
        }

        DrawBadge(graphics, ClickMarkers.GetLabel(kind), color, x, y, radius, imageSize, fontPixels: 13);
    }

    /// <summary>
    /// Video click marker: a ring that grows and fades over <paramref name="progress"/> (0 → 1),
    /// with a short tag (L, R, 2×, M) while it is fresh.
    /// </summary>
    public static void DrawVideoClick(Graphics graphics, int x, int y, ClickMarkerKind kind, double progress, Size imageSize)
    {
        progress = Math.Clamp(progress, 0, 1);
        var color = GetColor(kind);
        var alpha = (int)(240 * (1 - progress * 0.85));
        var radius = (float)(12 + 20 * progress);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var fill = new SolidBrush(Color.FromArgb((int)(alpha * 0.3), color)))
        {
            graphics.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
        }

        using (var edge = new Pen(Color.FromArgb((int)(alpha * 0.8), 255, 255, 255), 6))
        {
            graphics.DrawEllipse(edge, x - radius, y - radius, radius * 2, radius * 2);
        }

        using (var ring = new Pen(Color.FromArgb(alpha, color), 3.5f))
        {
            graphics.DrawEllipse(ring, x - radius, y - radius, radius * 2, radius * 2);
        }

        if (progress < 0.75)
        {
            DrawBadge(graphics, ClickMarkers.GetShortLabel(kind), Color.FromArgb(alpha, color), x, y, (int)radius, imageSize, fontPixels: 12);
        }
    }

    /// <summary>Soft yellow halo centred on the pointer, drawn under the cursor image.</summary>
    public static void DrawCursorHalo(Graphics graphics, int x, int y)
    {
        const int radius = 26;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = new GraphicsPath();
        path.AddEllipse(x - radius, y - radius, radius * 2, radius * 2);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb(120, HaloColor),
            SurroundColors = [Color.FromArgb(40, HaloColor)]
        };
        graphics.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
        using var edge = new Pen(Color.FromArgb(150, HaloColor), 2);
        graphics.DrawEllipse(edge, x - radius, y - radius, radius * 2, radius * 2);
    }

    /// <summary>Covers sensitive fields with a solid block (no blur, so nothing can be recovered) labelled "Hidden".</summary>
    public static void CoverSensitiveAreas(Graphics graphics, IReadOnlyList<ScreenBounds> localAreas)
    {
        if (localAreas.Count == 0)
        {
            return;
        }

        var previousSmoothing = graphics.SmoothingMode;
        graphics.SmoothingMode = SmoothingMode.None;
        using var fill = new SolidBrush(SecureFill);
        foreach (var area in localAreas)
        {
            var rect = new Rectangle(area.X, area.Y, area.Width, area.Height);
            graphics.FillRectangle(fill, rect);
            if (rect.Height >= 14 && rect.Width >= 60)
            {
                var fontPixels = Math.Clamp(rect.Height * 0.5f, 10f, 14f);
                using var font = new Font("Segoe UI", fontPixels, FontStyle.Regular, GraphicsUnit.Pixel);
                using var text = new SolidBrush(SecureText);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.None, FormatFlags = StringFormatFlags.NoWrap };
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString("Hidden by OpenSteps", font, text, rect, format);
            }
        }

        graphics.SmoothingMode = previousSmoothing;
    }

    private static void DrawBadge(Graphics graphics, string label, Color color, int x, int y, int radius, Size imageSize, float fontPixels)
    {
        using var font = new Font("Segoe UI", fontPixels, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var textSize = graphics.MeasureString(label, font);
        var width = textSize.Width + 12;
        var height = textSize.Height + 4;

        // Below-right of the ring by default; flipped when that would leave the image.
        var left = x + radius * 0.6f;
        var top = y + radius * 0.6f;
        if (left + width > imageSize.Width)
        {
            left = x - radius * 0.6f - width;
        }

        if (top + height > imageSize.Height)
        {
            top = y - radius * 0.6f - height;
        }

        left = Math.Max(0, left);
        top = Math.Max(0, top);

        var rect = new RectangleF(left, top, width, height);
        using var path = RoundedRectangle(rect, height / 2);
        using (var background = new SolidBrush(color))
        {
            graphics.FillPath(background, path);
        }

        using (var border = new Pen(Color.FromArgb(color.A, 255, 255, 255), 1.5f))
        {
            graphics.DrawPath(border, path);
        }

        using var text = new SolidBrush(Color.FromArgb(color.A, 255, 255, 255));
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(label, font, text, rect, format);
    }

    private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
