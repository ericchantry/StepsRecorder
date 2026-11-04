namespace OpenSteps.Core.Services;

/// <summary>
/// What common Windows shortcuts do, so a step can read "Press Ctrl+S to save". Only shortcuts that mean the same
/// thing in nearly every app are listed; anything else is shown as just the keys.
/// </summary>
public static class ShortcutPurposes
{
    private static readonly Dictionary<string, string> Purposes = Build(new Dictionary<string, string>
    {
        ["Ctrl+C"] = "copy",
        ["Ctrl+X"] = "cut",
        ["Ctrl+V"] = "paste",
        ["Ctrl+Z"] = "undo",
        ["Ctrl+Y"] = "redo",
        ["Ctrl+Shift+Z"] = "redo",
        ["Ctrl+S"] = "save",
        ["Ctrl+A"] = "select all",
        ["Ctrl+F"] = "find",
        ["Ctrl+P"] = "print",
        ["Ctrl+T"] = "open a new tab",
        ["Ctrl+W"] = "close the tab",
        ["Ctrl+Shift+T"] = "reopen the closed tab",
        ["Ctrl+Tab"] = "switch tabs",
        ["Ctrl+Shift+Esc"] = "open Task Manager",
        ["Alt+Tab"] = "switch windows",
        ["Alt+F4"] = "close the window",
        ["Win+D"] = "show the desktop",
        ["Win+E"] = "open File Explorer",
        ["Win+R"] = "open Run",
        ["Win+I"] = "open Settings",
        ["Win+L"] = "lock the computer",
        ["Win+V"] = "open clipboard history",
        ["Win+Shift+S"] = "take a screenshot",
        ["Win+Tab"] = "open Task View"
    });

    /// <summary>
    /// The purpose of <paramref name="shortcut"/> (e.g. "Ctrl+S" → "save"), whatever order the modifiers are written
    /// in; null for shortcuts whose meaning depends on the app.
    /// </summary>
    public static string? Describe(string? shortcut)
    {
        return Normalize(shortcut) is { } key && Purposes.TryGetValue(key, out var purpose) ? purpose : null;
    }

    private static Dictionary<string, string> Build(Dictionary<string, string> source)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (shortcut, purpose) in source)
        {
            result[Normalize(shortcut)!] = purpose;
        }

        return result;
    }

    /// <summary>Modifiers in a fixed order and the key last, so "Shift+Win+S" and "Win+Shift+S" match.</summary>
    private static string? Normalize(string? shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
        {
            return null;
        }

        var parts = shortcut.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return null;
        }

        var key = parts[^1];
        if (key.Equals("Escape", StringComparison.OrdinalIgnoreCase))
        {
            key = "Esc";
        }

        var order = new[] { "Ctrl", "Alt", "Shift", "Win" };
        var modifiers = parts[..^1]
            .Select(p => p.Equals("Control", StringComparison.OrdinalIgnoreCase) ? "Ctrl" : p.Equals("Windows", StringComparison.OrdinalIgnoreCase) ? "Win" : p)
            .OrderBy(p => Array.FindIndex(order, o => o.Equals(p, StringComparison.OrdinalIgnoreCase)));
        return string.Join("+", modifiers.Append(key)).ToUpperInvariant();
    }
}
