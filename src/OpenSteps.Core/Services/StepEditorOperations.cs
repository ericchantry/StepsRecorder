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

    /// <summary>
    /// Inserts a copy of <paramref name="step"/> right below it and returns the copy. The copy has its own identity
    /// and its own image files: <paramref name="copyImage"/> is given each image path and returns the copy's path
    /// (so editing one step's screenshot later never touches the other's).
    /// </summary>
    public static RecordedStep Duplicate(IList<RecordedStep> steps, RecordedStep step, Func<string?, string?> copyImage)
    {
        var node = JsonSerializer.SerializeToNode(step)!.AsObject();
        node.Remove(nameof(RecordedStep.Id));
        var copy = node.Deserialize<RecordedStep>()!;
        copy.Annotations = copy.Annotations.Select(annotation =>
        {
            var clone = ScreenshotEditing.Clone(annotation);
            clone.Id = Guid.NewGuid();
            return clone;
        }).ToList();
        copy.ScreenshotPath = copyImage(step.ScreenshotPath);
        copy.EditedScreenshotPath = copyImage(step.EditedScreenshotPath);
        copy.ScreenshotRelativePath = ToRelative(copy.ScreenshotPath);
        copy.EditedScreenshotRelativePath = ToRelative(copy.EditedScreenshotPath);
        copy.IsSelected = false;

        var index = steps.IndexOf(step);
        steps.Insert(index < 0 ? steps.Count : index + 1, copy);
        RecalculateIndexes(steps);
        return copy;
    }

    /// <summary>
    /// Merges the steps into the first of them (in list order): it keeps its screenshot and title, and gains the
    /// others' titles and descriptions as lines of its description. The others are removed. Returns the kept step,
    /// or null when fewer than two of the given steps are in the list.
    /// </summary>
    public static RecordedStep? Merge(IList<RecordedStep> steps, IEnumerable<RecordedStep> toMerge)
    {
        var chosen = new HashSet<RecordedStep>(toMerge);
        var ordered = steps.Where(chosen.Contains).ToList();
        if (ordered.Count < 2)
        {
            return null;
        }

        var target = ordered[0];
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(target.UserDescription))
        {
            lines.Add(target.UserDescription.Trim());
        }

        foreach (var other in ordered.Skip(1))
        {
            lines.Add(other.DisplayTitle.Trim());
            if (!string.IsNullOrWhiteSpace(other.UserDescription))
            {
                lines.Add(other.UserDescription.Trim());
            }

            steps.Remove(other);
        }

        target.UserDescription = string.Join(Environment.NewLine, lines.Where(line => line.Length > 0));
        target.IsSelected = false;
        RecalculateIndexes(steps);
        return target;
    }

    /// <summary>Removes the given steps and renumbers the rest. Returns how many were removed.</summary>
    public static int RemoveMany(IList<RecordedStep> steps, IEnumerable<RecordedStep> toRemove)
    {
        var removed = 0;
        foreach (var step in toRemove.ToList())
        {
            if (steps.Remove(step))
            {
                removed++;
            }
        }

        RecalculateIndexes(steps);
        return removed;
    }

    private static string? ToRelative(string? imagePath)
    {
        return string.IsNullOrWhiteSpace(imagePath) ? null : $"images/{Path.GetFileName(imagePath)}";
    }

    public static void RecalculateIndexes(IList<RecordedStep> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            steps[i].Index = i + 1;
        }
    }
}
