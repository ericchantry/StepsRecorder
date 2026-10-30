using OpenSteps.Core.Services;

namespace OpenSteps.Capture;

public sealed class KeyboardInputEventArgs(
    KeyboardInputKind kind,
    string? keyName,
    string? shortcutName,
    int virtualKey = 0,
    KeyModifiers modifiers = KeyModifiers.None,
    string? text = null,
    DateTimeOffset? timestamp = null) : EventArgs
{
    public KeyboardInputKind Kind { get; } = kind;

    /// <summary>Readable key name ("Enter", "Shift+Tab", "A").</summary>
    public string? KeyName { get; } = keyName;

    /// <summary>Shortcut name for <see cref="KeyboardInputKind.Shortcut"/> ("Ctrl+Shift+N").</summary>
    public string? ShortcutName { get; } = shortcutName;

    public int VirtualKey { get; } = virtualKey;

    public KeyModifiers Modifiers { get; } = modifiers;

    /// <summary>The character(s) produced. Only set when typed-text capture is on.</summary>
    public string? Text { get; } = text;

    /// <summary>When the key was physically pressed (UTC).</summary>
    public DateTimeOffset Timestamp { get; } = timestamp ?? DateTimeOffset.UtcNow;
}
