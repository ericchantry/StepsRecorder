using System.Text.RegularExpressions;
using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>
/// Writes short, plain step titles in the style of a how-to guide: <c>Click "Save"</c>, <c>Open the "File" menu</c>,
/// <c>Type "invoice 1042" in "Search"</c>, <c>Press Ctrl+S to save</c>, <c>Click in Excel</c>.
/// Labels are cleaned (shortcut hints, trailing "…" and extra whitespace removed) and long ones are shortened, and
/// window titles are reduced to the app name, so a title stays readable at a glance.
/// </summary>
public sealed partial class StepTitleGenerator
{
    /// <summary>Longest UI label kept in a title before it is shortened with "…".</summary>
    public const int MaxLabelLength = 40;

    /// <summary>Longest typed text kept in a title.</summary>
    public const int MaxTypedTextLength = 40;

    public string Generate(RecordedStep step)
    {
        return GenerateWithReason(step).Title;
    }

    public StepTitleResult GenerateWithReason(RecordedStep step)
    {
        if (step.ActionType == StepActionType.Shortcut && !string.IsNullOrWhiteSpace(step.ShortcutName))
        {
            var shortcut = step.ShortcutName.Trim();
            return ShortcutPurposes.Describe(shortcut) is { } purpose
                ? new StepTitleResult($"Press {shortcut} to {purpose}", "keyboard shortcut with known purpose")
                : new StepTitleResult($"Press {shortcut}", "keyboard shortcut");
        }

        if (step.ActionType == StepActionType.SpecialKey && !string.IsNullOrWhiteSpace(step.SpecialKeyName))
        {
            return new StepTitleResult($"Press {step.SpecialKeyName.Trim()}", "special key");
        }

        if (step.ActionType == StepActionType.TextEntry)
        {
            return TextEntryTitle(step);
        }

        return PointerTitle(step);
    }

    /// <summary>
    /// A UI label made fit for a title: whitespace collapsed, keyboard-shortcut hints ("Save (Ctrl+S)") and trailing
    /// "…" or ":" removed, and shortened at a word boundary when longer than <paramref name="maxLength"/>.
    /// Null when nothing useful is left.
    /// </summary>
    public static string? CleanLabel(string? value, int maxLength = MaxLabelLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var label = Whitespace().Replace(value, " ").Trim();
        label = ShortcutHintInParentheses().Replace(label, string.Empty);
        label = TrailingShortcut().Replace(label, string.Empty);
        label = label.TrimEnd(' ', '.', '…', ':').Trim();
        if (label.Length == 0)
        {
            return null;
        }

        return Shorten(label, maxLength);
    }

    /// <summary>
    /// The app's everyday name: "Excel", "Chrome", "File Explorer"… from the process name, otherwise the last part of
    /// the window title ("config.toml - Notepad" → "Notepad"), otherwise the window title or process name.
    /// </summary>
    public static string? GetAppName(string? processName, string? windowTitle)
    {
        if (!string.IsNullOrWhiteSpace(processName))
        {
            var key = processName.Trim();
            if (key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                key = key[..^4];
            }

            if (KnownApps.TryGetValue(key, out var known))
            {
                return known;
            }
        }

        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            var title = Whitespace().Replace(windowTitle, " ").Trim();
            var separator = Math.Max(title.LastIndexOf(" - ", StringComparison.Ordinal), title.LastIndexOf(" — ", StringComparison.Ordinal));
            if (separator > 0)
            {
                var suffix = title[(separator + 3)..].Trim();
                if (suffix.Length is > 0 and <= 30)
                {
                    return suffix;
                }
            }

            if (title.Length > 0)
            {
                return Shorten(title, MaxLabelLength);
            }
        }

        return string.IsNullOrWhiteSpace(processName) ? null : processName.Trim();
    }

    private static StepTitleResult TextEntryTitle(RecordedStep step)
    {
        var target = CleanLabel(step.InputTargetName);
        if (step.IsSensitiveInput)
        {
            if (target is null)
            {
                return new StepTitleResult("Enter hidden text", "sensitive text entry");
            }

            return target.Contains("pass", StringComparison.OrdinalIgnoreCase)
                ? new StepTitleResult("Enter the password", "sensitive text entry")
                : new StepTitleResult($"Enter hidden text in {Quote(target)}", "sensitive text entry with named target");
        }

        var app = GetAppName(step.ProcessName, step.WindowTitle);
        if (!string.IsNullOrWhiteSpace(step.TypedText))
        {
            var typed = Quote(TypedTextBuffer.Truncate(Whitespace().Replace(step.TypedText.ReplaceLineEndings(" "), " ").Trim(), MaxTypedTextLength));
            if (target is not null)
            {
                return new StepTitleResult($"Type {typed} in {Quote(target)}", "typed text with named input target");
            }

            return app is not null
                ? new StepTitleResult($"Type {typed} in {app}", "typed text with app name")
                : new StepTitleResult($"Type {typed}", "typed text");
        }

        if (target is not null)
        {
            return new StepTitleResult($"Type in {Quote(target)}", "named text input target");
        }

        return app is not null
            ? new StepTitleResult($"Type in {app}", "text entry app name fallback")
            : new StepTitleResult("Type text", "text entry fallback");
    }

    private static StepTitleResult PointerTitle(RecordedStep step)
    {
        var verb = step.ActionType switch
        {
            StepActionType.RightClick => "Right-click",
            StepActionType.DoubleClick => "Double-click",
            _ => "Click"
        };

        var label = step.UsefulElementFound ? CleanLabel(step.ElementName) : null;
        if (label is not null && !IsGenericName(label, step.ControlType))
        {
            return ElementTitle(step, verb, label);
        }

        if (GetAppName(step.ProcessName, step.WindowTitle) is { } app)
        {
            return new StepTitleResult($"{verb} in {app}", "app name fallback");
        }

        return new StepTitleResult($"{verb} on the screen", "no element or window information");
    }

    private static StepTitleResult ElementTitle(RecordedStep step, string verb, string label)
    {
        var quoted = Quote(label);
        var isClick = step.ActionType == StepActionType.Click;
        var kind = ClassifyControl(step.ControlType);
        var reason = $"{kind} element name for {step.ActionType}";

        return (kind, isClick) switch
        {
            (ControlKind.MenuItem, true) when IsTopLevelMenu(step.ParentElementName) => new($"Open the {quoted} menu", reason),
            (ControlKind.MenuItem, true) => new($"Choose {quoted}", reason),
            (ControlKind.Tab, true) => new($"Open the {quoted} tab", reason),
            (ControlKind.ComboBox, true) => new($"Open the {quoted} list", reason),
            (ControlKind.Item, true) or (ControlKind.RadioButton, true) => new($"Select {quoted}", reason),
            (ControlKind.CheckBox, true) => new($"Click the {quoted} checkbox", reason),
            (ControlKind.Field, _) => new($"{verb} in {quoted}", reason),
            _ => new($"{verb} {quoted}", reason)
        };
    }

    private static ControlKind ClassifyControl(string? controlType)
    {
        var type = controlType?.Replace("ControlType.", string.Empty, StringComparison.OrdinalIgnoreCase).Replace(" ", string.Empty).Trim().ToLowerInvariant() ?? string.Empty;
        return type switch
        {
            "menuitem" => ControlKind.MenuItem,
            "tabitem" => ControlKind.Tab,
            "combobox" => ControlKind.ComboBox,
            "listitem" or "treeitem" or "dataitem" => ControlKind.Item,
            "radiobutton" => ControlKind.RadioButton,
            "checkbox" => ControlKind.CheckBox,
            "edit" or "document" => ControlKind.Field,
            "hyperlink" => ControlKind.Link,
            _ => ControlKind.Other
        };
    }

    /// <summary>Menu-bar items sit under the window's menu bar (UI Automation names it "Application" or "Menu Bar").</summary>
    private static bool IsTopLevelMenu(string? parentName)
    {
        return string.IsNullOrWhiteSpace(parentName)
            || parentName.Contains("menu bar", StringComparison.OrdinalIgnoreCase)
            || parentName.Contains("menubar", StringComparison.OrdinalIgnoreCase)
            || parentName.Equals("Application", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Names that say nothing to a reader, such as an element simply called "Button" or "Pane".</summary>
    private static bool IsGenericName(string label, string? controlType)
    {
        var type = controlType?.Replace("ControlType.", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return label.Equals(type, StringComparison.OrdinalIgnoreCase)
            || label.Equals("Pane", StringComparison.OrdinalIgnoreCase)
            || label.Equals("Window", StringComparison.OrdinalIgnoreCase)
            || label.Equals("Custom", StringComparison.OrdinalIgnoreCase);
    }

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = value[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
        {
            cut = cut[..lastSpace];
        }

        return cut.TrimEnd(' ', ',', ';', '-', '.') + "…";
    }

    private static string Quote(string value)
    {
        return value.Contains('"') ? $"'{value}'" : $"\"{value}\"";
    }

    private static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["excel"] = "Excel",
        ["winword"] = "Word",
        ["powerpnt"] = "PowerPoint",
        ["outlook"] = "Outlook",
        ["olk"] = "Outlook",
        ["onenote"] = "OneNote",
        ["msaccess"] = "Access",
        ["ms-teams"] = "Teams",
        ["msteams"] = "Teams",
        ["teams"] = "Teams",
        ["chrome"] = "Chrome",
        ["msedge"] = "Edge",
        ["firefox"] = "Firefox",
        ["explorer"] = "File Explorer",
        ["notepad"] = "Notepad",
        ["mspaint"] = "Paint",
        ["code"] = "VS Code",
        ["devenv"] = "Visual Studio",
        ["cmd"] = "Command Prompt",
        ["powershell"] = "PowerShell",
        ["pwsh"] = "PowerShell",
        ["windowsterminal"] = "Terminal",
        ["systemsettings"] = "Settings",
        ["taskmgr"] = "Task Manager",
        ["control"] = "Control Panel",
        ["acrobat"] = "Acrobat",
        ["acrord32"] = "Acrobat Reader",
        ["slack"] = "Slack",
        ["zoom"] = "Zoom",
        ["snippingtool"] = "Snipping Tool",
        ["calculatorapp"] = "Calculator"
    };

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\s*\((?:Ctrl|Alt|Shift|Win|F\d{1,2}\b)[^)]*\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ShortcutHintInParentheses();

    [GeneratedRegex(@"\s+(?:(?:Ctrl|Alt|Shift|Win)\+)+\S+$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingShortcut();

    private enum ControlKind
    {
        Other,
        MenuItem,
        Tab,
        ComboBox,
        Item,
        RadioButton,
        CheckBox,
        Field,
        Link
    }
}

public sealed record StepTitleResult(string Title, string Reason);
