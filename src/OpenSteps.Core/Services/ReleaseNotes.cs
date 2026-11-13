using System.Text;
using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

public enum ReleaseNoteLineKind
{
    Paragraph,
    Heading,
    Bullet
}

/// <summary>A run of text inside a release-note line, so bold and `code` survive into the window.</summary>
public sealed record ReleaseNoteSpan(string Text, bool Bold = false, bool Code = false);

/// <summary>One line of release notes, ready to be drawn.</summary>
public sealed record ReleaseNoteLine(ReleaseNoteLineKind Kind, int Indent, IReadOnlyList<ReleaseNoteSpan> Spans)
{
    public string Text => string.Concat(Spans.Select(span => span.Text));
}

/// <summary>
/// Turns the markdown GitHub holds for a release into plain lines OpenSteps can draw: headings, bullets (with
/// their nesting) and paragraphs, keeping <b>bold</b> and <c>code</c>. Markdown that has no meaning on screen
/// (links keep their text, horizontal rules and HTML comments go) is quietly dropped.
/// </summary>
public static class ReleaseNotes
{
    /// <summary>Longest set of notes kept for one release; anything past this is cut at a line.</summary>
    public const int MaxLines = 400;

    public static IReadOnlyList<ReleaseNoteLine> Format(string? markdown)
    {
        var lines = new List<ReleaseNoteLine>();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return lines;
        }

        var inFence = false;
        foreach (var raw in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            if (lines.Count >= MaxLines)
            {
                break;
            }

            var line = raw.TrimEnd();
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                if (trimmed.Length > 0)
                {
                    lines.Add(new ReleaseNoteLine(ReleaseNoteLineKind.Paragraph, 1, [new ReleaseNoteSpan(trimmed, Code: true)]));
                }

                continue;
            }

            if (trimmed.Length == 0)
            {
                // Keep one blank line between blocks, never a run of them.
                if (lines.Count > 0 && lines[^1].Spans.Count > 0)
                {
                    lines.Add(new ReleaseNoteLine(ReleaseNoteLineKind.Paragraph, 0, []));
                }

                continue;
            }

            if (IsRule(trimmed) || trimmed.StartsWith("<!--", StringComparison.Ordinal))
            {
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                var text = trimmed.TrimStart('#').Trim();
                if (text.Length > 0)
                {
                    lines.Add(new ReleaseNoteLine(ReleaseNoteLineKind.Heading, 0, ParseSpans(text)));
                }

                continue;
            }

            var indent = (line.Length - trimmed.Length) / 2;
            if (TryReadBullet(trimmed, out var bulletText))
            {
                lines.Add(new ReleaseNoteLine(ReleaseNoteLineKind.Bullet, indent, ParseSpans(bulletText)));
                continue;
            }

            lines.Add(new ReleaseNoteLine(ReleaseNoteLineKind.Paragraph, indent, ParseSpans(trimmed)));
        }

        while (lines.Count > 0 && lines[^1].Spans.Count == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    /// <summary>
    /// The notes for several releases, newest first, each under a heading naming its version, so the user sees
    /// everything that changed between the version they have and the one on offer.
    /// </summary>
    public static IReadOnlyList<ReleaseNoteLine> FormatAll(IEnumerable<ReleaseInfo> releases)
    {
        var lines = new List<ReleaseNoteLine>();
        foreach (var release in releases)
        {
            if (lines.Count > 0)
            {
                lines.Add(new ReleaseNoteLine(ReleaseNoteLineKind.Paragraph, 0, []));
            }

            var date = release.PublishedAt is { } published ? $" · {published.ToLocalTime():d MMMM yyyy}" : string.Empty;
            lines.Add(new ReleaseNoteLine(
                ReleaseNoteLineKind.Heading,
                0,
                [new ReleaseNoteSpan($"Version {release.Version}{date}", Bold: true)]));

            var body = Format(release.Notes);
            if (body.Count == 0)
            {
                lines.Add(new ReleaseNoteLine(
                    ReleaseNoteLineKind.Paragraph,
                    0,
                    [new ReleaseNoteSpan("No notes were published for this version.")]));
                continue;
            }

            lines.AddRange(body);
        }

        return lines;
    }

    /// <summary>The notes as plain text, for a log or a message box.</summary>
    public static string ToPlainText(IEnumerable<ReleaseNoteLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Spans.Count == 0)
            {
                builder.AppendLine();
                continue;
            }

            builder.Append(new string(' ', line.Indent * 2));
            if (line.Kind == ReleaseNoteLineKind.Bullet)
            {
                builder.Append("• ");
            }

            builder.AppendLine(line.Text);
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsRule(string text)
    {
        return text.Length >= 3 && (text.All(c => c == '-') || text.All(c => c == '*') || text.All(c => c == '_'));
    }

    private static bool TryReadBullet(string text, out string bulletText)
    {
        bulletText = string.Empty;
        if (text.StartsWith("- ", StringComparison.Ordinal) || text.StartsWith("* ", StringComparison.Ordinal))
        {
            bulletText = text[2..].Trim();
            return true;
        }

        // "1. something"
        var dot = text.IndexOf(". ", StringComparison.Ordinal);
        if (dot is > 0 and <= 3 && text[..dot].All(char.IsDigit))
        {
            bulletText = text[(dot + 2)..].Trim();
            return true;
        }

        return false;
    }

    /// <summary>Splits a line into runs, reading **bold**, `code` and [text](link), which keeps only its text.</summary>
    public static IReadOnlyList<ReleaseNoteSpan> ParseSpans(string text)
    {
        var spans = new List<ReleaseNoteSpan>();
        var plain = new StringBuilder();

        void FlushPlain()
        {
            if (plain.Length > 0)
            {
                spans.Add(new ReleaseNoteSpan(plain.ToString()));
                plain.Clear();
            }
        }

        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '*' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var end = text.IndexOf("**", index + 2, StringComparison.Ordinal);
                if (end > index + 2)
                {
                    FlushPlain();
                    spans.Add(new ReleaseNoteSpan(text[(index + 2)..end], Bold: true));
                    index = end + 2;
                    continue;
                }
            }

            if (text[index] == '`')
            {
                var end = text.IndexOf('`', index + 1);
                if (end > index + 1)
                {
                    FlushPlain();
                    spans.Add(new ReleaseNoteSpan(text[(index + 1)..end], Code: true));
                    index = end + 1;
                    continue;
                }
            }

            if (text[index] == '[')
            {
                var close = text.IndexOf(']', index + 1);
                if (close > index + 1 && close + 1 < text.Length && text[close + 1] == '(')
                {
                    var linkEnd = text.IndexOf(')', close + 2);
                    if (linkEnd > close)
                    {
                        plain.Append(text[(index + 1)..close]);
                        index = linkEnd + 1;
                        continue;
                    }
                }
            }

            plain.Append(text[index]);
            index++;
        }

        FlushPlain();
        return spans.Count > 0 ? spans : [new ReleaseNoteSpan(text)];
    }
}
