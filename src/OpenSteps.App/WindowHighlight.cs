using System.Drawing;
using WinForms = System.Windows.Forms;

namespace OpenSteps.App;

/// <summary>
/// Briefly draws a coloured outline around a window so the user can see which one will be recorded.
/// A WinForms form is used because it is positioned in physical screen pixels, which matches the
/// window bounds Windows reports on mixed-DPI multi-monitor setups.
/// </summary>
public static class WindowHighlight
{
    private const int Thickness = 5;

    public static void Flash(Rectangle bounds, int milliseconds = 1400)
    {
        try
        {
            var frame = new HighlightForm(Rectangle.Inflate(bounds, Thickness, Thickness));
            frame.Show();
            var timer = new WinForms.Timer { Interval = milliseconds };
            timer.Tick += (_, _) =>
            {
                timer.Dispose();
                frame.Close();
                frame.Dispose();
            };
            timer.Start();
        }
        catch
        {
            // The outline is only a visual hint.
        }
    }

    private sealed class HighlightForm : WinForms.Form
    {
        private const int WM_DPICHANGED = 0x02E0;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private readonly Rectangle _screenBounds;

        public HighlightForm(Rectangle screenBounds)
        {
            _screenBounds = screenBounds;
            FormBorderStyle = WinForms.FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = WinForms.FormStartPosition.Manual;
            AutoScaleMode = WinForms.AutoScaleMode.None;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            TopMost = true;
            Bounds = screenBounds;
        }

        protected override bool ShowWithoutActivation => true;

        protected override WinForms.CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_TOPMOST | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // Keep the outline out of screenshots and the video if it overlaps the start of a recording.
            SetWindowDisplayAffinity(Handle, 0x11);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = _screenBounds;
        }

        protected override void OnPaint(WinForms.PaintEventArgs e)
        {
            base.OnPaint(e);
            using var pen = new Pen(Color.FromArgb(255, 76, 110, 245), Thickness);
            pen.Alignment = System.Drawing.Drawing2D.PenAlignment.Inset;
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void WndProc(ref WinForms.Message m)
        {
            // Keep the exact pixel bounds when the outline spans monitors with different scaling.
            if (m.Msg == WM_DPICHANGED)
            {
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);
        }
    }
}
