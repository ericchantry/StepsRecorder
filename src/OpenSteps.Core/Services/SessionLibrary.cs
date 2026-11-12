using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>Searching and tagging saved guides.</summary>
public static class SessionLibrary
{
    public const int MaxTags = 12;
    public const int MaxTagLength = 30;

    /// <summary>
    /// Tags from what the user typed ("billing, month end; Onboarding"): split on commas and semicolons, trimmed,
    /// a leading # dropped, duplicates removed without regard to case, and capped in number and length.
    /// </summary>
    public static List<string> ParseTags(string? text)
    {
        var tags = new List<string>();
        foreach (var part in (text ?? string.Empty).Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tag = string.Join(' ', part.TrimStart('#').Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (tag.Length > MaxTagLength)
            {
                tag = tag[..MaxTagLength].TrimEnd();
            }

            if (tag.Length > 0 && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase) && tags.Count < MaxTags)
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    public static string FormatTags(IEnumerable<string>? tags)
    {
        return string.Join(", ", tags ?? []);
    }

    /// <summary>
    /// Guides matching every word of <paramref name="query"/> in their title or tags (any order, any case).
    /// A word starting with # must match a tag. An empty query matches everything.
    /// </summary>
    public static IEnumerable<SessionSummary> Filter(IEnumerable<SessionSummary> sessions, string? query)
    {
        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length == 0 ? sessions : sessions.Where(session => words.All(word => Matches(session, word)));
    }

    private static bool Matches(SessionSummary session, string word)
    {
        if (word.StartsWith('#'))
        {
            var tag = word[1..];
            return tag.Length == 0 || session.Tags.Any(t => t.Contains(tag, StringComparison.OrdinalIgnoreCase));
        }

        return session.Title.Contains(word, StringComparison.OrdinalIgnoreCase)
            || session.Tags.Any(t => t.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
