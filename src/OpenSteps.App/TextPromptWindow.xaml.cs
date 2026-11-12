using System.Windows;

namespace OpenSteps.App;

/// <summary>A small "type a value" dialog, used for renaming a guide and editing its tags.</summary>
public partial class TextPromptWindow : Window
{
    private TextPromptWindow(string title, string prompt, string? hint, string value)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        HintText.Text = hint ?? string.Empty;
        HintText.Visibility = string.IsNullOrWhiteSpace(hint) ? Visibility.Collapsed : Visibility.Visible;
        ValueBox.Text = value;
        System.Windows.Automation.AutomationProperties.SetName(ValueBox, prompt);
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    /// <summary>Shows the dialog and returns what was typed, or null when cancelled.</summary>
    public static string? Ask(Window owner, string title, string prompt, string? hint, string value)
    {
        var window = new TextPromptWindow(title, prompt, hint, value) { Owner = owner };
        return window.ShowDialog() == true ? window.ValueBox.Text.Trim() : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
