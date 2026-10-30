using System.Text;
using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

public sealed class MarkdownBuilder
{
    private const int TitleTextLimit = 60;

    public string BuildMarkdown(RecordingSession session)
    {
        return BuildMarkdown(session, GuideExportOptions.Default, new Dictionary<int, string>());
    }

    /// <param name="videoLinks">Relative link for each exported segment video, keyed by segment number.</param>
    public string BuildMarkdown(RecordingSession session, GuideExportOptions options, IReadOnlyDictionary<int, string> videoLinks)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine($"# {EscapeHeading(session.Title)}");
        markdown.AppendLine();
        markdown.AppendLine("Created with OpenSteps.");
        markdown.AppendLine();

        if (options.IncludeEnvironment && session.Environment is { } environment)
        {
            markdown.AppendLine("## Environment");
            markdown.AppendLine();
            markdown.AppendLine("| | |");
            markdown.AppendLine("|---|---|");
            foreach (var row in environment.ToRows())
            {
                markdown.AppendLine($"| {EscapeCell(row.Key)} | {EscapeCell(row.Value)} |");
            }

            markdown.AppendLine();
        }

        for (var i = 0; i < session.Steps.Count; i++)
        {
            var step = session.Steps[i];
            var number = i + 1;
            var title = string.IsNullOrWhiteSpace(step.DisplayTitle) ? $"Step {number}" : step.DisplayTitle;
            markdown.AppendLine($"## Step {number}: {title}");
            markdown.AppendLine();

            var screenshotPath = GetExportScreenshotPath(step);
            if (!string.IsNullOrWhiteSpace(screenshotPath) && File.Exists(screenshotPath))
            {
                markdown.AppendLine($"![Step {number}](images/step-{number:000}.png)");
                markdown.AppendLine();
            }

            if (GetFullTypedText(step) is { } typed)
            {
                markdown.AppendLine($"Text typed: {CodeSpan(typed)}");
                markdown.AppendLine();
            }

            if (step is { VideoSegment: { } segment, VideoOffsetSeconds: { } offset } && videoLinks.TryGetValue(segment, out var link))
            {
                markdown.AppendLine($"_Video: [recording {segment}]({link}) at {TimeFormat.Format(offset)}_");
                markdown.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(step.UserDescription))
            {
                markdown.AppendLine(step.UserDescription.Trim());
                markdown.AppendLine();
            }
        }

        AppendRecordingSection(markdown, session, options, videoLinks);
        return markdown.ToString();
    }

    /// <summary>Typed text worth showing in full: present, not sensitive, and too long for the step title.</summary>
    public static string? GetFullTypedText(RecordedStep step)
    {
        return !step.IsSensitiveInput && step.TypedText is { Length: > TitleTextLimit } text ? text : null;
    }

    private static void AppendRecordingSection(StringBuilder markdown, RecordingSession session, GuideExportOptions options, IReadOnlyDictionary<int, string> videoLinks)
    {
        var entries = options.IncludeInputLog
            ? InputTimelineBuilder.BuildWithSteps(session.InputEvents, session.Steps)
            : [];
        var segments = session.Segments.Select(s => s.Number)
            .Concat(entries.Select(e => e.Segment))
            .Distinct()
            .Order()
            .ToList();
        if (segments.Count == 0 || (entries.Count == 0 && videoLinks.Count == 0))
        {
            return;
        }

        markdown.AppendLine("## Recording");
        markdown.AppendLine();
        if (session.Segments.Count > 0 && !session.Segments.Any(s => s.TypedTextCaptured))
        {
            markdown.AppendLine("Typed characters were not recorded for this session.");
            markdown.AppendLine();
        }

        foreach (var number in segments)
        {
            var segment = session.Segments.FirstOrDefault(s => s.Number == number);
            if (segments.Count > 1)
            {
                var duration = segment is null ? string.Empty : $" ({TimeFormat.Format(segment.DurationSeconds)})";
                markdown.AppendLine($"### Recording {number}{duration}");
                markdown.AppendLine();
            }

            if (videoLinks.TryGetValue(number, out var link))
            {
                markdown.AppendLine($"[Watch the video]({link})");
                markdown.AppendLine();
            }

            var rows = entries.Where(e => e.Segment == number).ToList();
            if (rows.Count == 0)
            {
                continue;
            }

            markdown.AppendLine("| Time | Input | Window |");
            markdown.AppendLine("|---:|---|---|");
            foreach (var entry in rows)
            {
                var label = entry.IsStep ? $"**{EscapeCell(entry.Label)}**" : EscapeCell(entry.Label);
                if (entry.RawKeys is { } raw)
                {
                    label += $" (keys: {EscapeCell(raw)})";
                }

                markdown.AppendLine($"| {entry.TimeLabel} | {label} | {EscapeCell(entry.Detail ?? string.Empty)} |");
            }

            markdown.AppendLine();
        }
    }

    private static string EscapeHeading(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Untitled OpenSteps Guide" : value.Trim();
    }

    private static string EscapeCell(string value)
    {
        return value.Replace("\\", "\\\\").Replace("|", "\\|").ReplaceLineEndings(" ");
    }

    private static string CodeSpan(string value)
    {
        var text = value.ReplaceLineEndings(" ");
        return text.Contains('`') ? $"`` {text} ``" : $"`{text}`";
    }

    private static string? GetExportScreenshotPath(RecordedStep step)
    {
        if (!string.IsNullOrWhiteSpace(step.EditedScreenshotPath) && File.Exists(step.EditedScreenshotPath))
        {
            return step.EditedScreenshotPath;
        }

        return step.ScreenshotPath;
    }
}
