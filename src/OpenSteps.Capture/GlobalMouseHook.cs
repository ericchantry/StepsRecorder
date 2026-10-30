using System.Runtime.InteropServices;
using System.Threading;
using OpenSteps.Core.Models;

namespace OpenSteps.Capture;

public sealed class GlobalMouseHook : IDisposable
{
    private readonly object _clickSync = new();
    private readonly TimeSpan _doubleClickTime;
    private readonly int _doubleClickWidth;
    private readonly int _doubleClickHeight;
    private readonly Func<int, int, bool> _shouldIgnoreClick;
    private readonly NativeMethods.LowLevelHookProc _callback;
    private readonly int _dragThreshold;
    private PendingLeftClick? _pendingLeftClick;
    private (int X, int Y, DateTimeOffset Timestamp)? _leftButtonDown;
    private System.Threading.Timer? _pendingLeftClickTimer;
    private IntPtr _hookHandle;

    public GlobalMouseHook(Func<int, int, bool>? shouldIgnoreClick = null)
    {
        _shouldIgnoreClick = shouldIgnoreClick ?? ((_, _) => false);
        _callback = HookCallback;
        _doubleClickTime = TimeSpan.FromMilliseconds(Math.Clamp(NativeMethods.GetDoubleClickTime(), 250, 900));
        _doubleClickWidth = Math.Max(4, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDOUBLECLK));
        _doubleClickHeight = Math.Max(4, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDOUBLECLK));
        _dragThreshold = 16;
    }

    public event EventHandler<ClickCapturedEventArgs>? ClickCaptured;

    /// <summary>Middle clicks, wheel scrolls and drags, for the input timeline.</summary>
    public event EventHandler<PointerActivityEventArgs>? PointerActivityCaptured;

    public bool IsRunning => _hookHandle != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _callback, IntPtr.Zero, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to install the global mouse hook.");
        }
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        FlushPendingLeftClick();
        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        Stop();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                HandleMouseMessage(wParam, lParam);
            }
            catch
            {
                // Mouse capture should never interrupt the target application or the hook chain.
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void HandleMouseMessage(IntPtr wParam, IntPtr lParam)
    {
        var message = (int)wParam;
        if (message is not (NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_LBUTTONUP
            or NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_MOUSEWHEEL or NativeMethods.WM_MOUSEHWHEEL))
        {
            return;
        }

        var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
        var now = DateTimeOffset.UtcNow;
        if (message == NativeMethods.WM_LBUTTONUP)
        {
            HandleLeftButtonUp(data.Pt.X, data.Pt.Y, now);
            return;
        }

        if (_shouldIgnoreClick(data.Pt.X, data.Pt.Y))
        {
            return;
        }

        switch (message)
        {
            case NativeMethods.WM_RBUTTONDOWN:
                FlushPendingLeftClick();
                ClickCaptured?.Invoke(this, new ClickCapturedEventArgs(data.Pt.X, data.Pt.Y, StepActionType.RightClick, "Right", timestamp: now));
                break;
            case NativeMethods.WM_LBUTTONDOWN:
                _leftButtonDown = (data.Pt.X, data.Pt.Y, now);
                HandleLeftClick(data.Pt.X, data.Pt.Y, now);
                break;
            case NativeMethods.WM_MBUTTONDOWN:
                PointerActivityCaptured?.Invoke(this, new PointerActivityEventArgs(InputEventKind.MiddleClick, data.Pt.X, data.Pt.Y, now));
                break;
            case NativeMethods.WM_MOUSEWHEEL:
            case NativeMethods.WM_MOUSEHWHEEL:
                var delta = (short)((data.MouseData >> 16) & 0xFFFF);
                var kind = message == NativeMethods.WM_MOUSEWHEEL ? InputEventKind.Scroll : InputEventKind.HorizontalScroll;
                PointerActivityCaptured?.Invoke(this, new PointerActivityEventArgs(kind, data.Pt.X, data.Pt.Y, now, delta));
                break;
        }
    }

    private void HandleLeftButtonUp(int x, int y, DateTimeOffset now)
    {
        if (_leftButtonDown is not { } down)
        {
            return;
        }

        _leftButtonDown = null;
        if (Math.Abs(x - down.X) >= _dragThreshold || Math.Abs(y - down.Y) >= _dragThreshold)
        {
            PointerActivityCaptured?.Invoke(this, new PointerActivityEventArgs(InputEventKind.Drag, down.X, down.Y, down.Timestamp, endX: x, endY: y));
        }
    }

    private void HandleLeftClick(int x, int y, DateTimeOffset now)
    {
        ClickCapturedEventArgs? doubleClick = null;
        lock (_clickSync)
        {
            if (_pendingLeftClick is { } pending && IsDoubleClick(pending, x, y, now))
            {
                _pendingLeftClickTimer?.Dispose();
                _pendingLeftClickTimer = null;
                _pendingLeftClick = null;
                doubleClick = new ClickCapturedEventArgs(
                    x,
                    y,
                    StepActionType.DoubleClick,
                    "Left",
                    2,
                    $"Second left click within {_doubleClickTime.TotalMilliseconds:0} ms and {_doubleClickWidth}x{_doubleClickHeight}px system bounds.",
                    pending.Timestamp);
            }
            else
            {
                _pendingLeftClickTimer?.Dispose();
                _pendingLeftClick = new PendingLeftClick(x, y, now);
                _pendingLeftClickTimer = new System.Threading.Timer(_ => FlushPendingLeftClick(), null, _doubleClickTime, Timeout.InfiniteTimeSpan);
            }
        }

        if (doubleClick is not null)
        {
            ClickCaptured?.Invoke(this, doubleClick);
        }
    }

    private bool IsDoubleClick(PendingLeftClick pending, int x, int y, DateTimeOffset now)
    {
        return now - pending.Timestamp <= _doubleClickTime
            && Math.Abs(x - pending.X) <= _doubleClickWidth
            && Math.Abs(y - pending.Y) <= _doubleClickHeight;
    }

    private void FlushPendingLeftClick()
    {
        ClickCapturedEventArgs? click = null;
        lock (_clickSync)
        {
            if (_pendingLeftClick is null)
            {
                return;
            }

            _pendingLeftClickTimer?.Dispose();
            _pendingLeftClickTimer = null;
            var pending = _pendingLeftClick;
            _pendingLeftClick = null;
            click = new ClickCapturedEventArgs(pending.X, pending.Y, StepActionType.Click, "Left", timestamp: pending.Timestamp);
        }

        ClickCaptured?.Invoke(this, click);
    }

    private sealed record PendingLeftClick(int X, int Y, DateTimeOffset Timestamp);
}
