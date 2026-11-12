using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OpenSteps.App;

/// <summary>
/// Gives every control a name for screen readers. Many buttons show only an icon, which a screen reader would
/// otherwise announce as a meaningless character; their tooltip already says what they do, so it becomes the
/// accessible name. Controls that set <see cref="AutomationProperties.NameProperty"/> themselves are left alone.
/// </summary>
public static class Accessibility
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        foreach (var type in new[] { typeof(ButtonBase), typeof(System.Windows.Controls.TextBox), typeof(System.Windows.Controls.ComboBox), typeof(System.Windows.Controls.ListBox), typeof(Slider) })
        {
            EventManager.RegisterClassHandler(type, FrameworkElement.LoadedEvent, new RoutedEventHandler(OnControlLoaded));
        }

        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window window)
            {
                ThemeManager.ApplyTitleBar(window);
            }
        }));
    }

    /// <summary>
    /// The name a control should be announced with: its tooltip's first sentence when its own content is an icon
    /// or it has no text, otherwise null (its visible text is already the name).
    /// </summary>
    public static string? GetNameFromToolTip(object? content, object? toolTip)
    {
        if (toolTip is not string tip || string.IsNullOrWhiteSpace(tip))
        {
            return null;
        }

        if (content is string text && text.Trim().Length > 0 && !IsIconGlyph(text))
        {
            return null;
        }

        var firstLine = tip.Split('\n', '\r')[0].Trim();
        var sentenceEnd = firstLine.IndexOf(". ", StringComparison.Ordinal);
        var colon = firstLine.IndexOf(": ", StringComparison.Ordinal);
        var end = new[] { sentenceEnd, colon }.Where(i => i > 0).DefaultIfEmpty(-1).Min();
        var name = end > 0 ? firstLine[..end] : firstLine;
        return name.TrimEnd('.', ' ');
    }

    /// <summary>True for Segoe icon-font characters (private-use area), which mean nothing when read aloud.</summary>
    public static bool IsIconGlyph(string text)
    {
        return text.Trim().All(c => c is >= '' and <= '');
    }

    private static string GetVisibleText(DependencyObject root)
    {
        var parts = new List<string>();
        Collect(root);
        return string.Join(" ", parts);

        void Collect(object node)
        {
            if (node is TextBlock block)
            {
                if (!string.IsNullOrWhiteSpace(block.Text) && !IsIconGlyph(block.Text))
                {
                    parts.Add(block.Text.Trim());
                }

                return;
            }

            if (node is DependencyObject dependencyObject)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(dependencyObject))
                {
                    Collect(child);
                }
            }
        }
    }

    private static void OnControlLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || !string.IsNullOrEmpty(AutomationProperties.GetName(element)))
        {
            return;
        }

        var content = (element as ContentControl)?.Content;
        if (content is DependencyObject tree && GetVisibleText(tree) is { Length: > 0 } label)
        {
            // Icon-plus-text buttons: the text is the name, the tooltip the description.
            AutomationProperties.SetName(element, label);
            if (element.ToolTip is string help)
            {
                AutomationProperties.SetHelpText(element, help);
            }

            return;
        }

        if (GetNameFromToolTip(content, element.ToolTip) is { Length: > 0 } name)
        {
            AutomationProperties.SetName(element, name);
            if (element.ToolTip is string tip && tip.Trim().TrimEnd('.') != name)
            {
                AutomationProperties.SetHelpText(element, tip);
            }
        }
    }
}
