namespace OpenSteps.Core.Services;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
    Win = 8
}

public enum KeyCategory
{
    /// <summary>Produces text; grouped into a typing run.</summary>
    Text,

    /// <summary>A key worth its own guide step (Enter, Tab, Escape, F5, arrows...).</summary>
    SpecialKey,

    /// <summary>A Ctrl/Alt/Win combination; gets its own guide step.</summary>
    Shortcut,

    /// <summary>Recorded on the timeline only (Home, Page Down, Caps Lock, media keys...).</summary>
    TimelineOnly
}

public sealed record KeyClassification(KeyCategory Category, string Name);

/// <summary>Turns Windows virtual-key codes into readable names and decides how each key is recorded.</summary>
public static class KeyNames
{
    private static readonly Dictionary<int, string> Names = BuildNames();

    public static bool IsModifier(int vk)
    {
        return vk is 0x10 or 0x11 or 0x12 or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;
    }

    public static string GetName(int vk)
    {
        return Names.TryGetValue(vk, out var name) ? name : $"Key 0x{vk:X2}";
    }

    public static string FormatCombo(KeyModifiers modifiers, int vk)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(KeyModifiers.Ctrl))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(KeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(GetName(vk));
        return string.Join("+", parts);
    }

    /// <summary>
    /// Classifies a key press. <paramref name="producesText"/> says whether the keyboard layout turned the
    /// press into a printable character (used to recognise AltGr, which Windows reports as Ctrl+Alt).
    /// Returns null for presses that should be ignored (modifier keys on their own).
    /// </summary>
    public static KeyClassification? Classify(int vk, KeyModifiers modifiers, bool producesText)
    {
        if (IsModifier(vk))
        {
            return null;
        }

        var ctrl = modifiers.HasFlag(KeyModifiers.Ctrl);
        var alt = modifiers.HasFlag(KeyModifiers.Alt);
        var win = modifiers.HasFlag(KeyModifiers.Win);
        var isAltGr = ctrl && alt && !win && producesText;

        if ((ctrl || alt || win) && !isAltGr)
        {
            return new KeyClassification(KeyCategory.Shortcut, FormatCombo(modifiers, vk));
        }

        if (IsStepKey(vk))
        {
            var shiftOnly = modifiers & KeyModifiers.Shift;
            return new KeyClassification(KeyCategory.SpecialKey, FormatCombo(shiftOnly, vk));
        }

        if (producesText || IsPrintableKey(vk))
        {
            return new KeyClassification(KeyCategory.Text, GetName(vk));
        }

        return new KeyClassification(KeyCategory.TimelineOnly, FormatCombo(modifiers & KeyModifiers.Shift, vk));
    }

    public static bool IsPrintableKey(int vk)
    {
        return vk is 0x20
            or >= 0x30 and <= 0x39
            or >= 0x41 and <= 0x5A
            or >= 0x60 and <= 0x6F
            or >= 0xBA and <= 0xC0
            or >= 0xDB and <= 0xDF
            or 0xE2;
    }

    private static bool IsStepKey(int vk)
    {
        return vk is 0x08 // Backspace
            or 0x09 // Tab
            or 0x0D // Enter
            or 0x1B // Escape
            or 0x2E // Delete
            or >= 0x25 and <= 0x28 // arrows
            or 0x2C // Print Screen
            or >= 0x70 and <= 0x87; // F1-F24
    }

    private static Dictionary<int, string> BuildNames()
    {
        var names = new Dictionary<int, string>
        {
            [0x03] = "Break",
            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0C] = "Clear",
            [0x0D] = "Enter",
            [0x13] = "Pause",
            [0x14] = "Caps Lock",
            [0x1B] = "Escape",
            [0x20] = "Space",
            [0x21] = "Page Up",
            [0x22] = "Page Down",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left Arrow",
            [0x26] = "Up Arrow",
            [0x27] = "Right Arrow",
            [0x28] = "Down Arrow",
            [0x2C] = "Print Screen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
            [0x5D] = "Menu",
            [0x5F] = "Sleep",
            [0x6A] = "Num *",
            [0x6B] = "Num +",
            [0x6C] = "Num Separator",
            [0x6D] = "Num -",
            [0x6E] = "Num .",
            [0x6F] = "Num /",
            [0x90] = "Num Lock",
            [0x91] = "Scroll Lock",
            [0xA6] = "Browser Back",
            [0xA7] = "Browser Forward",
            [0xA8] = "Browser Refresh",
            [0xA9] = "Browser Stop",
            [0xAA] = "Browser Search",
            [0xAB] = "Browser Favorites",
            [0xAC] = "Browser Home",
            [0xAD] = "Volume Mute",
            [0xAE] = "Volume Down",
            [0xAF] = "Volume Up",
            [0xB0] = "Next Track",
            [0xB1] = "Previous Track",
            [0xB2] = "Stop Media",
            [0xB3] = "Play/Pause",
            [0xB4] = "Mail",
            [0xBA] = ";",
            [0xBB] = "=",
            [0xBC] = ",",
            [0xBD] = "-",
            [0xBE] = ".",
            [0xBF] = "/",
            [0xC0] = "`",
            [0xDB] = "[",
            [0xDC] = "\\",
            [0xDD] = "]",
            [0xDE] = "'",
            [0xE2] = "\\ (102)"
        };

        for (var vk = 0x30; vk <= 0x39; vk++)
        {
            names[vk] = ((char)vk).ToString();
        }

        for (var vk = 0x41; vk <= 0x5A; vk++)
        {
            names[vk] = ((char)vk).ToString();
        }

        for (var i = 0; i <= 9; i++)
        {
            names[0x60 + i] = $"Num {i}";
        }

        for (var i = 1; i <= 24; i++)
        {
            names[0x6F + i] = $"F{i}";
        }

        return names;
    }
}
