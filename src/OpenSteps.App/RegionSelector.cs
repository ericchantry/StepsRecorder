using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;
using WinForms = System.Windows.Forms;

namespace OpenSteps.App;

/// <summary>
/// Full-screen overlay for drawing the area to record. The desktop is frozen into a picture first, dimmed, and the
/// dragged rectangle is shown undimmed with its size. A WinForms form is used (as for <see cref="WindowHighlight"/>)
/// because it works in physical screen pixels, which keeps the result exact on mixed-DPI monitor setups.
/// </summary>
internal sealed class RegionSelector : WinForms.Form
{
    private const int WM_DPICHANGED = 0x02E0;
    private static readonly Color Accent = Color.FromArgb(255, 76, 110, 245);

    private readonly Rectangle _screen;
    private readonly Bitmap _backdrop;
    private readonly Bitmap _dimmed;
    private readonly Rectangle? _previous;
    private Point? _dragStart;
    private Point _dragCurrent;
    private ScreenBounds? _result;

    private RegionSelector(Rectangle screen, Bitmap backdrop, ScreenBounds? previous)
    {
        _screen = screen;
        _backdrop = backdrop;
        _previous = previous is { } p ? new Rectangle(p.X - screen.X, p.Y - screen.Y, p.Width, p.Height) : null;
        _dimmed = new Bitmap(backdrop.Width, backdrop.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(_dimmed))
        {
            graphics.DrawImageUnscaled(backdrop, 0, 0);
            using var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            graphics.FillRectangle(shade, 0, 0, backdrop.Width, backdrop.Height);
        }

        FormBorderStyle = WinForms.FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = WinForms.FormStartPosition.Manual;
        AutoScaleMode = WinForms.AutoScaleMode.None;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        Cursor = WinForms.Cursors.Cross;
        Text = "Select a region to record";
        Bounds = screen;
    }

    /// <summary>
    /// Lets the user drag out a rectangle. Returns it in physical screen pixels, or null when cancelled
    /// (Esc or right-click). Enter keeps <paramref name="previous"/> when there is one.
    /// </summary>
    public static ScreenBounds? Select(ScreenBounds? previous)
    {
        var screen = WinForms.SystemInformation.VirtualScreen;
        using var backdrop = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppPArgb);
        using (var graphics = Graphics.FromImage(backdrop))
        {
            graphics.CopyFromScreen(screen.Location, Point.Empty, screen.Size, CopyPixelOperation.SourceCopy);
        }

        using var form = new RegionSelector(screen, backdrop, previous);
        return form.ShowDialog() == WinForms.DialogResult.OK ? form._result : null;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = _screen;
        Activate();
    }

    protected override void OnKeyDown(WinForms.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == WinForms.Keys.Escape)
        {
            DialogResult = WinForms.DialogResult.Cancel;
        }
        else if (e.KeyCode == WinForms.Keys.Enter && _previous is { } previous && _dragStart is null)
        {
            Finish(previous);
        }
    }

    protected override void OnMouseDown(WinForms.MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == WinForms.MouseButtons.Right)
        {
            DialogResult = WinForms.DialogResult.Cancel;
            return;
        }

        if (e.Button == WinForms.MouseButtons.Left)
        {
            _dragStart = e.Location;
            _dragCurrent = e.Location;
            Capture = true;
            Invalidate();
        }
    }

    protected override void OnMouseMove(WinForms.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is null)
        {
            return;
        }

        var before = SelectionPaintBounds();
        _dragCurrent = e.Location;
        var after = SelectionPaintBounds();
        Invalidate(Rectangle.Union(before, after));
    }

    protected override void OnMouseUp(WinForms.MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != WinForms.MouseButtons.Left || _dragStart is not { } start)
        {
            return;
        }

        Capture = false;
        _dragCurrent = e.Location;
        var selection = ToRectangle(CaptureRegionMath.FromCorners(start.X, start.Y, _dragCurrent.X, _dragCurrent.Y));
        _dragStart = null;
        if (CaptureRegionMath.IsLargeEnough(new ScreenBounds(selection.X, selection.Y, selection.Width, selection.Height)))
        {
            Finish(selection);
        }
        else
        {
            // Too small to be deliberate (probably a click): let the user try again.
            Invalidate();
        }
    }

    protected override void OnPaint(WinForms.PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.DrawImage(_dimmed, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);

        if (_dragStart is { } start)
        {
            var selection = ToRectangle(CaptureRegionMath.FromCorners(start.X, start.Y, _dragCurrent.X, _dragCurrent.Y));
            if (selection.Width > 0 && selection.Height > 0)
            {
                graphics.DrawImage(_backdrop, selection, selection, GraphicsUnit.Pixel);
                using var pen = new Pen(Accent, 2);
                graphics.DrawRectangle(pen, selection.X, selection.Y, selection.Width - 1, selection.Height - 1);
                DrawSizeLabel(graphics, selection);
            }
        }
        else if (_previous is { } previous)
        {
            using var dashed = new Pen(Color.FromArgb(220, 255, 255, 255), 2) { DashStyle = DashStyle.Dash };
            graphics.DrawRectangle(dashed, previous);
        }

        DrawInstructions(graphics);
    }

    protected override void WndProc(ref WinForms.Message m)
    {
        // Keep the exact pixel bounds when the overlay spans monitors with different scaling.
        if (m.Msg == WM_DPICHANGED)
        {
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dimmed.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Finish(Rectangle local)
    {
        _result = new ScreenBounds(local.X + _screen.X, local.Y + _screen.Y, local.Width, local.Height);
        DialogResult = WinForms.DialogResult.OK;
    }

    private Rectangle SelectionPaintBounds()
    {
        if (_dragStart is not { } start)
        {
            return Rectangle.Empty;
        }

        var selection = ToRectangle(CaptureRegionMath.FromCorners(start.X, start.Y, _dragCurrent.X, _dragCurrent.Y));
        // Room for the border and the size label below it.
        return Rectangle.Inflate(Rectangle.FromLTRB(selection.Left, selection.Top, selection.Right + 140, selection.Bottom + 44), 6, 6);
    }

    private void DrawSizeLabel(Graphics graphics, Rectangle selection)
    {
        var text = $"{selection.Width} × {selection.Height}";
        using var font = new Font("Segoe UI", 13f * DeviceDpi / 96f, FontStyle.Bold, GraphicsUnit.Pixel);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var size = graphics.MeasureString(text, font);
        var box = new RectangleF(selection.Right - size.Width - 12, selection.Bottom + 6, size.Width + 12, size.Height + 6);
        if (box.Bottom > ClientSize.Height)
        {
            box.Y = selection.Bottom - box.Height - 6;
        }

        box.X = Math.Max(0, box.X);
        using var background = new SolidBrush(Accent);
        graphics.FillRectangle(background, box);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(text, font, Brushes.White, box, format);
    }

    private void DrawInstructions(Graphics graphics)
    {
        var primary = WinForms.Screen.PrimaryScreen?.Bounds ?? _screen;
        var text = _previous is null
            ? "Drag to select the area to record    ·    Esc to cancel"
            : "Drag to select the area to record    ·    Enter keeps the current region    ·    Esc to cancel";
        using var font = new Font("Segoe UI", 15f * DeviceDpi / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var size = graphics.MeasureString(text, font);
        var box = new RectangleF(
            primary.X - _screen.X + (primary.Width - size.Width) / 2 - 18,
            primary.Y - _screen.Y + 28,
            size.Width + 36,
            size.Height + 16);
        using var background = new SolidBrush(Color.FromArgb(230, 20, 24, 33));
        graphics.FillRectangle(background, box);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(text, font, Brushes.White, box, format);
    }

    private static Rectangle ToRectangle(ScreenBounds bounds)
    {
        return new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }
}
