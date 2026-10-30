using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace OpenSteps.App;

/// <summary>
/// Small always-on-top bar shown at the bottom of the screen while recording, with Pause, Stop and Cancel.
/// It never takes keyboard focus (so the app being recorded keeps it) and is hidden from screen capture
/// so it does not appear in screenshots or the session video.
/// </summary>
public partial class RecordingBarWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private readonly MainWindow _controller;
    private bool _positioned;

    public RecordingBarWindow(MainWindow controller)
    {
        _controller = controller;
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyWindowStyles();
        Loaded += (_, _) => PositionBottomCenter();
    }

    public void UpdateStatus(bool isPaused, TimeSpan elapsed, int stepCount, bool canPause)
    {
        StatusText.Text = isPaused ? "Paused" : "Recording";
        DetailText.Text = $"{elapsed:mm\\:ss} · {stepCount} {(stepCount == 1 ? "step" : "steps")}";
        RecordingDot.Visibility = isPaused ? Visibility.Collapsed : Visibility.Visible;
        PausedIcon.Visibility = isPaused ? Visibility.Visible : Visibility.Collapsed;
        PauseButtonIcon.Text = isPaused ? "" : "";
        PauseButtonText.Text = isPaused ? "Resume" : "Pause";
        PauseButton.ToolTip = isPaused ? "Resume recording" : "Pause recording. Nothing is recorded while paused.";
        PauseButton.Visibility = canPause ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowBar()
    {
        if (_positioned)
        {
            KeepOnScreen();
        }

        Show();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _controller.TogglePauseFromRecordingBar();
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        await _controller.StopRecordingFromRecordingBarAsync();
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        await _controller.CancelRecordingFromRecordingBarAsync();
    }

    private void Bar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindButton(source))
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static bool FindButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is System.Windows.Controls.Button)
            {
                return true;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - ActualWidth) / 2);
        Top = area.Bottom - ActualHeight - 12;
        _positioned = true;
    }

    private void KeepOnScreen()
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;
        if (Left < left || Top < top || Left + ActualWidth > right || Top + ActualHeight > bottom)
        {
            PositionBottomCenter();
        }
    }

    private void ApplyWindowStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // Clicking the bar must not move keyboard focus away from the app being recorded.
        var style = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));

        // Keep the bar out of screenshots and the video (Windows 10 2004+; older versions ignore it).
        SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}
