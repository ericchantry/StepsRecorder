using System.Windows;
using System.Windows.Automation;

namespace OpenSteps.App;

/// <summary>A small "type one line of text" dialog, used for renaming, tagging and text labels.</summary>
public partial class TextPromptWindow : Window
{
    private readonly bool _allowEmpty;

    public TextPromptWindow(string title, string prompt, string? initialText = null, string? hint = null, string okText = "OK", bool allowEmpty = false)
    {
        InitializeComponent();
        _allowEmpty = allowEmpty;
        Title = title;
        PromptLabel.Text = prompt;
        AutomationProperties.SetName(InputBox, prompt);
        InputBox.Text = initialText ?? string.Empty;
        OkButton.Content = okText;
        if (!string.IsNullOrWhiteSpace(hint))
        {
            HintLabel.Text = hint;
            HintLabel.Visibility = Visibility.Visible;
        }

        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    public string Value => InputBox.Text.Trim();

    /// <summary>Shows the dialog and returns the text, or null when cancelled.</summary>
    public static string? Ask(Window? owner, string title, string prompt, string? initialText = null, string? hint = null, string okText = "OK", bool allowEmpty = false)
    {
        var dialog = new TextPromptWindow(title, prompt, initialText, hint, okText, allowEmpty);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!_allowEmpty && Value.Length == 0)
        {
            InputBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
