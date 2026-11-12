using System.IO;
using System.Windows;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.App;

/// <summary>Step-list actions beyond one step at a time (duplicate, merge, bulk delete) and guide-library actions.</summary>
public partial class MainWindow
{
    internal IReadOnlyList<RecordedStep> SelectedSteps => Steps.Where(step => step.IsSelected).ToList();

    internal async Task DuplicateStepFromEditorAsync(object sender)
    {
        if ((sender as FrameworkElement)?.DataContext is not RecordedStep step)
        {
            return;
        }

        try
        {
            StepEditorOperations.Duplicate(Steps, step, CopyStepImage);
            SyncSessionOrder();
            await SaveSessionAsync(showMessage: false);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(_editorWindow, $"The step could not be duplicated:\n{ex.Message}", "OpenSteps", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    internal async Task DeleteSelectedStepsFromEditorAsync()
    {
        var selected = SelectedSteps;
        if (selected.Count == 0)
        {
            return;
        }

        var what = selected.Count == 1 ? "the selected step" : $"the {selected.Count} selected steps";
        if (System.Windows.MessageBox.Show(_editorWindow, $"Delete {what}?", "OpenSteps", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        StepEditorOperations.RemoveMany(Steps, selected);
        SyncSessionOrder();
        UpdateSessionSummary();
        await SaveSessionAsync(showMessage: false);
    }

    internal async Task MergeSelectedStepsFromEditorAsync()
    {
        var selected = SelectedSteps;
        if (selected.Count < 2)
        {
            System.Windows.MessageBox.Show(_editorWindow, "Tick two or more steps to merge them into one.", "OpenSteps", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var first = Steps.First(step => step.IsSelected);
        var answer = System.Windows.MessageBox.Show(
            _editorWindow,
            $"Merge {selected.Count} steps into step {first.Index}?\n\nStep {first.Index} keeps its screenshot and title. The other steps' titles and descriptions are added to its description, and their screenshots are removed from the guide.",
            "OpenSteps",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        StepEditorOperations.Merge(Steps, selected);
        SyncSessionOrder();
        UpdateSessionSummary();
        await SaveSessionAsync(showMessage: false);
    }

    internal void ClearStepSelectionFromEditor()
    {
        foreach (var step in Steps)
        {
            step.IsSelected = false;
        }

        RefreshStepViews();
    }

    /// <summary>A private copy of a step image for a duplicated step, next to the original.</summary>
    private string? CopyStepImage(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        var directory = GetSessionImagesDirectory();
        Directory.CreateDirectory(directory);
        var name = $"{Path.GetFileNameWithoutExtension(sourcePath)}-copy-{Guid.NewGuid().ToString("N")[..8]}{Path.GetExtension(sourcePath)}";
        var destination = Path.Combine(directory, name);
        File.Copy(sourcePath, destination);
        return destination;
    }

    // ---- Guide library -----------------------------------------------------------------------------------------

    internal async Task RenameSavedSessionFromLibraryAsync(Guid sessionId, string title)
    {
        if (_session.Id == sessionId)
        {
            _session.Title = title;
            SetEditorTitle(title);
        }

        await _sessionStore.RenameSessionAsync(sessionId, title);
    }

    internal async Task SetSavedSessionTagsAsync(Guid sessionId, IReadOnlyList<string> tags)
    {
        if (_session.Id == sessionId)
        {
            _session.Tags = [.. tags];
        }

        await _sessionStore.SetTagsAsync(sessionId, tags);
    }
}
