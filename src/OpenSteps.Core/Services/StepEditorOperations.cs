using System.Text.Json;
using System.Text.Json.Nodes;
using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

public static class StepEditorOperations
{
    public static RecordedStep CreateManualStep()
    {
        return new RecordedStep
        {
            Index = 1,
            Timestamp = DateTimeOffset.Now,
            ActionType = StepActionType.Manual,
            GeneratedTitle = "Manual step",
            UserTitle = "New step",
            UserDescription = string.Empty,
            ScreenshotRelativePath = null
        };
    }

    public static RecordedStep AddManualStepAtEnd(IList<RecordedStep> steps)
    {
        return InsertManualStep(steps, steps.Count);
    }

    public static RecordedStep InsertManualStepBelow(IList<RecordedStep> steps, RecordedStep currentStep)
    {
        var index = steps.IndexOf(currentStep);
        return InsertManualStep(steps, index < 0 ? steps.Count : index + 1);
    }

    public static RecordedStep InsertManualStep(IList<RecordedStep> steps, int insertIndex)
    {
        var step = CreateManualStep();
        var clampedIndex = Math.Clamp(insertIndex, 0, steps.Count);
        steps.Insert(clampedIndex, step);
        RecalculateIndexes(steps);
        return step;
    }

    public static void RecalculateIndexes(IList<RecordedStep> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            steps[i].Index = i + 1;
        }
    }

    /// <summary>A deep copy of a step with its own identity. Image paths are left pointing at the same files.</summary>
    public static RecordedStep Clone(RecordedStep step)
    {
        var node = JsonSerializer.SerializeToNode(step, CloneOptions)!.AsObject();
        node.Remove(nameof(RecordedStep.Id));
        var copy = node.Deserialize<RecordedStep>(CloneOptions)!;
        copy.ScreenshotPath = step.ScreenshotPath;
        copy.EditedScreenshotPath = step.EditedScreenshotPath;
        copy.ActiveWindowHandle = step.ActiveWindowHandle;
        return copy;
    }

    /// <summary>
    /// Inserts a copy of <paramref name="step"/> right below it, with its own copies of the screenshot files
    /// (made by <paramref name="copyImage"/>, which returns the new path or null) so the two can be edited separately.
    /// </summary>
    public static RecordedStep DuplicateBelow(IList<RecordedStep> steps, RecordedStep step, Func<string, string?> copyImage)
    {
        var copy = Clone(step);
        (copy.ScreenshotPath, copy.ScreenshotRelativePath) = CopyImage(step.ScreenshotPath, copyImage);
        (copy.EditedScreenshotPath, copy.EditedScreenshotRelativePath) = CopyImage(step.EditedScreenshotPath, copyImage);
        copy.ScreenshotCaptured = copy.ScreenshotPath is not null;
        InsertBelow(steps, step, copy);
        return copy;
    }

    /// <summary>
    /// Folds the step after <paramref name="step"/> into it: this step keeps its screenshot and title, and gains the
    /// next step's title and description as extra description lines. Returns the removed step, or null if there is no next step.
    /// </summary>
    public static RecordedStep? MergeWithNext(IList<RecordedStep> steps, RecordedStep step)
    {
        var index = steps.IndexOf(step);
        if (index < 0 || index >= steps.Count - 1)
        {
            return null;
        }

        var next = steps[index + 1];
        var lines = new[] { step.UserDescription, next.DisplayTitle, next.UserDescription }
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line!.Trim());
        step.UserDescription = string.Join(Environment.NewLine, lines);
        if (string.IsNullOrWhiteSpace(step.TypedText))
        {
            step.TypedText = next.TypedText;
        }

        steps.RemoveAt(index + 1);
        RecalculateIndexes(steps);
        return next;
    }

    /// <summary>
    /// Splits a step in two. The new step below gets its own copy of the original screenshot (without edits) and the
    /// part of the description after the first paragraph, so each half can show and say something different.
    /// </summary>
    public static RecordedStep SplitBelow(IList<RecordedStep> steps, RecordedStep step, Func<string, string?> copyImage)
    {
        var (keep, move) = SplitDescription(step.UserDescription);
        var second = Clone(step);
        (second.ScreenshotPath, second.ScreenshotRelativePath) = CopyImage(step.ScreenshotPath, copyImage);
        second.ClearScreenshotEdits();
        second.ScreenshotCaptured = second.ScreenshotPath is not null;
        second.ActionType = StepActionType.Manual;
        second.UserTitle = $"{step.DisplayTitle} (continued)";
        second.UserDescription = move;
        second.TypedText = null;
        step.UserDescription = keep;
        InsertBelow(steps, step, second);
        return second;
    }

    /// <summary>The first paragraph (or first line) of a description, and everything after it.</summary>
    public static (string Keep, string Move) SplitDescription(string? description)
    {
        var text = (description ?? string.Empty).ReplaceLineEndings("\n").Trim();
        var breakAt = text.IndexOf("\n\n", StringComparison.Ordinal);
        if (breakAt < 0)
        {
            breakAt = text.IndexOf('\n');
        }

        return breakAt < 0
            ? (text, string.Empty)
            : (text[..breakAt].Trim().ReplaceLineEndings(), text[breakAt..].Trim().ReplaceLineEndings());
    }

    /// <summary>Removes every ticked step and returns them.</summary>
    public static IReadOnlyList<RecordedStep> RemoveSelected(IList<RecordedStep> steps)
    {
        var removed = steps.Where(step => step.IsSelected).ToList();
        foreach (var step in removed)
        {
            steps.Remove(step);
        }

        RecalculateIndexes(steps);
        return removed;
    }

    private static void InsertBelow(IList<RecordedStep> steps, RecordedStep step, RecordedStep added)
    {
        var index = steps.IndexOf(step);
        steps.Insert(index < 0 ? steps.Count : index + 1, added);
        RecalculateIndexes(steps);
    }

    private static (string? Path, string? RelativePath) CopyImage(string? source, Func<string, string?> copyImage)
    {
        if (string.IsNullOrWhiteSpace(source) || copyImage(source) is not { } copy)
        {
            return (null, null);
        }

        return (copy, $"images/{Path.GetFileName(copy)}");
    }

    private static readonly JsonSerializerOptions CloneOptions = new(JsonSerializerDefaults.Web);
}
