using System.Runtime.InteropServices;
using OpenSteps.Core.Services;

namespace OpenSteps.Capture;

public sealed class GlobalKeyboardHook : IDisposable
{
    // ToUnicodeEx flag: do not change the keyboard state (keeps dead keys working in the target app). Windows 10 1607+.
    private const uint ToUnicodeDoNotChangeState = 0x4;

    private readonly Func<bool> _shouldIgnoreKeyboard;
    private readonly Func<bool> _captureText;
    private readonly NativeMethods.LowLevelHookProc _callback;
    private readonly byte[] _keyState = new byte[256];
    private readonly char[] _charBuffer = new char[8];
    private IntPtr _hookHandle;

    public GlobalKeyboardHook(Func<bool>? shouldIgnoreKeyboard = null, Func<bool>? captureText = null)
    {
        _shouldIgnoreKeyboard = shouldIgnoreKeyboard ?? (() => false);
        _captureText = captureText ?? (() => false);
        _callback = HookCallback;
    }

    public event EventHandler<KeyboardInputEventArgs>? KeyboardInputCaptured;

    public bool IsRunning => _hookHandle != IntPtr.Zero;

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _callback, IntPtr.Zero, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to install the global keyboard hook.");
        }
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        Stop();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0
            && (wParam == NativeMethods.WM_KEYDOWN || wParam == NativeMethods.WM_SYSKEYDOWN)
            && !_shouldIgnoreKeyboard())
        {
            try
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                var captured = Classify(data);
                if (captured is not null)
                {
                    KeyboardInputCaptured?.Invoke(this, captured);
                }
            }
            catch
            {
                // Keyboard capture should never interrupt the target application or the hook chain.
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private KeyboardInputEventArgs? Classify(NativeMethods.KBDLLHOOKSTRUCT data)
    {
        var vk = (int)data.VkCode;
        if (KeyNames.IsModifier(vk))
        {
            return null;
        }

        var timestamp = DateTimeOffset.UtcNow;
        var modifiers = ReadModifiers();
        var text = TranslateToText(data, modifiers);
        var classification = KeyNames.Classify(vk, modifiers, producesText: text is not null);
        if (classification is null)
        {
            return null;
        }

        return classification.Category switch
        {
            KeyCategory.Shortcut => new KeyboardInputEventArgs(KeyboardInputKind.Shortcut, null, classification.Name, vk, modifiers, null, timestamp),
            KeyCategory.SpecialKey => new KeyboardInputEventArgs(KeyboardInputKind.SpecialKey, classification.Name, null, vk, modifiers, null, timestamp),
            KeyCategory.Text => new KeyboardInputEventArgs(
                KeyboardInputKind.Text,
                modifiers.HasFlag(KeyModifiers.Shift) ? "Shift+Text" : "Text",
                null,
                vk,
                modifiers,
                _captureText() ? text : null,
                timestamp),
            _ => new KeyboardInputEventArgs(KeyboardInputKind.TimelineOnly, classification.Name, null, vk, modifiers, null, timestamp)
        };
    }

    /// <summary>
    /// Asks the foreground window's keyboard layout which character this press produces, without
    /// disturbing dead-key state. Returns null for non-printing keys.
    /// </summary>
    private string? TranslateToText(NativeMethods.KBDLLHOOKSTRUCT data, KeyModifiers modifiers)
    {
        var vk = (int)data.VkCode;
        if (modifiers.HasFlag(KeyModifiers.Win) || (modifiers.HasFlag(KeyModifiers.Ctrl) ^ modifiers.HasFlag(KeyModifiers.Alt)))
        {
            // Ctrl or Alt alone (not AltGr) means a shortcut, not text.
            return null;
        }

        if (!KeyNames.IsPrintableKey(vk))
        {
            return null;
        }

        try
        {
            Array.Clear(_keyState);
            SetState(0x10, IsDown(0x10));
            SetState(0x11, IsDown(0x11));
            SetState(0x12, IsDown(0x12));
            SetState(0xA2, IsDown(0xA2));
            SetState(0xA5, IsDown(0xA5));
            if ((NativeMethods.GetKeyState(0x14) & 1) != 0)
            {
                _keyState[0x14] = 0x01; // Caps Lock toggled
            }

            var foreground = NativeMethods.GetForegroundWindow();
            var threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
            var layout = NativeMethods.GetKeyboardLayout(threadId);
            var count = NativeMethods.ToUnicodeEx(data.VkCode, data.ScanCode, _keyState, _charBuffer, _charBuffer.Length, ToUnicodeDoNotChangeState, layout);
            if (count <= 0)
            {
                return null;
            }

            var result = new string(_charBuffer, 0, count);
            return result.All(ch => !char.IsControl(ch)) ? result : null;
        }
        catch
        {
            return null;
        }
    }

    private void SetState(int vk, bool down)
    {
        if (down)
        {
            _keyState[vk] = 0x80;
        }
    }

    private static KeyModifiers ReadModifiers()
    {
        var modifiers = KeyModifiers.None;
        if (IsDown(0x11) || IsDown(0xA2) || IsDown(0xA3))
        {
            modifiers |= KeyModifiers.Ctrl;
        }

        if (IsDown(0x12) || IsDown(0xA4) || IsDown(0xA5))
        {
            modifiers |= KeyModifiers.Alt;
        }

        if (IsDown(0x10) || IsDown(0xA0) || IsDown(0xA1))
        {
            modifiers |= KeyModifiers.Shift;
        }

        if (IsDown(0x5B) || IsDown(0x5C))
        {
            modifiers |= KeyModifiers.Win;
        }

        return modifiers;
    }

    private static bool IsDown(int virtualKey)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
    }
}
