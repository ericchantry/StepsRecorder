using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;
using WinForms = System.Windows.Forms;

namespace OpenSteps.App;

public partial class SessionPickerWindow : Window, INotifyPropertyChanged
{
    private readonly MainWindow _controller;
    private int _sessionGridColumns = 1;
    private IReadOnlyList<SessionSummary> _allSessions = [];

    public SessionPickerWindow(MainWindow controller)
    {
        _controller = controller;
        Sessions = [];
        InitializeComponent();
        DataContext = this;
        Loaded += async (_, _) => await RefreshSessionsAsync();
        Closed += (_, _) => _controller.SessionPickerClosed(this);
    }

    public ObservableCollection<SessionSummary> Sessions { get; }

    public int SessionGridColumns
    {
        get => _sessionGridColumns;
        private set
        {
            if (_sessionGridColumns == value)
            {
                return;
            }

            _sessionGridColumns = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task RefreshSessionsAsync()
    {
        _allSessions = await _controller.ListSavedSessionsAsync();
        ApplyFilter();
    }

    /// <summary>Shows the guides matching the search box (all of them when it is empty).</summary>
    private void ApplyFilter()
    {
        var query = SearchBox.Text;
        var matches = SessionLibrary.Filter(_allSessions, query);
        Sessions.Clear();
        foreach (var session in matches)
        {
            Sessions.Add(session);
        }

        SessionGridColumns = Math.Clamp(Math.Max(Sessions.Count, Math.Min(_allSessions.Count, 3)), 1, 3);
        var searching = !string.IsNullOrWhiteSpace(query);
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility = Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHeading.Text = searching && _allSessions.Count > 0 ? "No guides match your search" : "No saved guides yet";
        EmptyDetail.Text = searching && _allSessions.Count > 0 ? "Try a different word, or clear the search box." : "Capture a recording and it will appear here.";
        CountText.Text = _allSessions.Count == 0
            ? "Open a previous capture or browse the folder on disk."
            : searching
                ? $"Showing {Sessions.Count} of {_allSessions.Count} guides."
                : _allSessions.Count == 1 ? "1 guide." : $"{_allSessions.Count} guides.";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyFilter();
        }
    }

    private void Tag_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is string tag)
        {
            SearchBox.Text = "#" + tag.Split(' ')[0];
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SessionSummary summary)
        {
            return;
        }

        var title = TextPromptWindow.Ask(this, "Rename guide", "Guide title", summary.Title, okText: "Rename");
        if (title is null || title == summary.Title)
        {
            return;
        }

        await _controller.RenameSavedSessionFromLibraryAsync(summary.Id, title);
        await RefreshSessionsAsync();
    }

    private async void EditTags_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SessionSummary summary)
        {
            return;
        }

        var text = TextPromptWindow.Ask(
            this,
            "Edit tags",
            $"Tags for \"{summary.Title}\"",
            SessionLibrary.FormatTags(summary.Tags),
            hint: "Separate tags with commas, for example: onboarding, finance. Leave empty to remove all tags.",
            okText: "Save",
            allowEmpty: true);
        if (text is null)
        {
            return;
        }

        await _controller.SetSavedSessionTagsAsync(summary.Id, SessionLibrary.ParseTags(text));
        await RefreshSessionsAsync();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_controller.SessionsRootDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = _controller.SessionsRootDirectory,
            UseShellExecute = true
        });
    }

    private async void SessionCard_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FindAncestor<System.Windows.Controls.Button>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        await OpenSessionFromElementAsync(sender as FrameworkElement);
    }

    private async Task OpenSessionFromElementAsync(FrameworkElement? element)
    {
        if (element?.DataContext is not SessionSummary summary)
        {
            return;
        }

        await _controller.OpenSavedSessionAsync(summary.Id);
        Close();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SessionSummary summary)
        {
            return;
        }

        var result = WinForms.MessageBox.Show(
            $"Delete \"{summary.Title}\"?\n\nThis removes the saved session folder from this computer.",
            "OpenSteps",
            WinForms.MessageBoxButtons.YesNo,
            WinForms.MessageBoxIcon.Warning);
        if (result != WinForms.DialogResult.Yes)
        {
            return;
        }

        await _controller.DeleteSavedSessionAsync(summary.Id);
        await RefreshSessionsAsync();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
