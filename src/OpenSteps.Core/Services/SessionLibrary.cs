using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>Searching and tagging saved guides.</summary>
public static class SessionLibrary
{
    public const int MaxTagLength = 30;

    /// <summary>
    /// Guides matching every word of <paramref name="query"/>. A plain word matches the title or a tag
    /// (anywhere, ignoring case); a word starting with # matches only a tag that starts with it.
    /// An empty query returns everything.
    /// </summary>
    public static IReadOnlyList<SessionSummary> Filter(IEnumerable<SessionSummary> sessions, string? query)
    {
        var words = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return sessions.ToList();
        }

        return sessions.Where(session => words.All(word => Matches(session, word))).ToList();
    }

    /// <summary>Tags from text such as "onboarding, HR; bug #urgent": trimmed, without #, de-duplicated ignoring case.</summary>
    public static IReadOnlyList<string> ParseTags(string? text)
    {
        return NormalizeTags((text ?? string.Empty).Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
    }

    public static IReadOnlyList<string> NormalizeTags(IEnumerable<string> tags)
    {
        var result = new List<string>();
        foreach (var raw in tags)
        {
            var tag = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimStart('#').Trim();
            if (tag.Length > MaxTagLength)
            {
                tag = tag[..MaxTagLength].TrimEnd();
            }

            if (tag.Length > 0 && !result.Contains(tag, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(tag);
            }
        }

        return result;
    }

    public static string FormatTags(IEnumerable<string> tags)
    {
        return string.Join(", ", tags);
    }

    private static bool Matches(SessionSummary session, string word)
    {
        if (word.StartsWith('#'))
        {
            var tag = word[1..];
            return tag.Length == 0 || session.Tags.Any(t => t.StartsWith(tag, StringComparison.OrdinalIgnoreCase));
        }

        return session.Title.Contains(word, StringComparison.OrdinalIgnoreCase)
            || session.Tags.Any(t => t.Contains(word, StringComparison.OrdinalIgnoreCase));
    }
}
