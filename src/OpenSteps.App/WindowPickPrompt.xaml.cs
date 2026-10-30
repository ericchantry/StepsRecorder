using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OpenSteps.App;

/// <summary>
/// "Click the window you want to record" banner shown while picking a window. It never takes focus,
/// so the first window the user activates after it appears is the one picked.
/// </summary>
public partial class WindowPickPrompt : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    public WindowPickPrompt()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
            SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        };
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
            Top = area.Top + 24;
        };
    }

    public bool IsCancelled { get; private set; }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        IsCancelled = true;
        Close();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
