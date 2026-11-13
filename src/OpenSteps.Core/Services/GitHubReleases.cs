using System.Text.Json;
using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>
/// Reads the GitHub releases API answer and works out whether anything newer than the running version is on
/// offer. Kept apart from the download itself so the decision can be tested without a network.
/// </summary>
public static class GitHubReleases
{
    /// <summary>The release asset OpenSteps installs, and the checksum published next to it.</summary>
    public const string PackageSuffix = "-win-x64.zip";

    public static string ListUrl(string repository, int count = 30) =>
        $"https://api.github.com/repos/{repository}/releases?per_page={count}";

    /// <summary>
    /// Turns the JSON array returned by /releases into release records, newest version first. Drafts and
    /// releases whose tag holds no version are left out; anything unreadable is skipped rather than throwing,
    /// so one odd release cannot stop an update being found.
    /// </summary>
    public static IReadOnlyList<ReleaseInfo> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var elements = root.ValueKind switch
        {
            JsonValueKind.Array => root.EnumerateArray().ToList(),
            JsonValueKind.Object => [root],
            _ => []
        };

        var releases = new List<ReleaseInfo>();
        foreach (var element in elements)
        {
            if (ReadRelease(element) is { } release)
            {
                releases.Add(release);
            }
        }

        return releases
            .OrderByDescending(release => release.Version)
            .ThenByDescending(release => release.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private static ReleaseInfo? ReadRelease(JsonElement element)
    {
        try
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (GetBool(element, "draft"))
            {
                return null;
            }

            var tag = GetString(element, "tag_name");
            if (!ReleaseVersion.TryParse(tag, out var version))
            {
                return null;
            }

            string? packageUrl = null;
            string? packageName = null;
            string? checksumUrl = null;
            long packageSize = 0;
            if (element.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = GetString(asset, "name");
                    var url = GetString(asset, "browser_download_url");
                    if (name is null || url is null)
                    {
                        continue;
                    }

                    if (name.EndsWith(PackageSuffix, StringComparison.OrdinalIgnoreCase) && packageUrl is null)
                    {
                        packageUrl = url;
                        packageName = name;
                        packageSize = asset.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0;
                    }
                    else if (name.EndsWith(PackageSuffix + ".sha256", StringComparison.OrdinalIgnoreCase) && checksumUrl is null)
                    {
                        checksumUrl = url;
                    }
                }
            }

            return new ReleaseInfo
            {
                TagName = tag!,
                Version = version,
                Title = GetString(element, "name"),
                Notes = GetString(element, "body") ?? string.Empty,
                PublishedAt = GetDate(element, "published_at") ?? GetDate(element, "created_at"),
                IsPrerelease = GetBool(element, "prerelease") || version.IsPrerelease,
                PackageUrl = packageUrl,
                PackageName = packageName,
                PackageSize = packageSize,
                ChecksumUrl = checksumUrl
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Picks the release to offer. Pre-releases are left out unless <paramref name="includePrereleases"/> is on,
    /// and so are releases without a Windows package, since OpenSteps could not install those. Every release
    /// newer than the running version is returned as the list of changes, newest first.
    /// </summary>
    public static UpdateCheckResult SelectUpdate(
        IReadOnlyList<ReleaseInfo> releases,
        ReleaseVersion currentVersion,
        string? skippedVersion = null,
        bool includePrereleases = false)
    {
        var candidates = releases
            .Where(release => includePrereleases || !release.IsPrerelease)
            .Where(release => release.Version > currentVersion)
            .OrderByDescending(release => release.Version)
            .ToList();

        var newest = candidates.FirstOrDefault(release => release.CanInstall);
        if (newest is null)
        {
            return UpdateCheckResult.UpToDate(currentVersion);
        }

        // Everything the user has not seen yet, up to and including the one on offer.
        var changes = candidates.Where(release => release.Version <= newest.Version).ToList();
        var skipped = ReleaseVersion.Parse(skippedVersion);
        var status = skipped is { } ignored && newest.Version <= ignored
            ? UpdateStatus.Skipped
            : UpdateStatus.UpdateAvailable;

        return new UpdateCheckResult
        {
            Status = status,
            CurrentVersion = currentVersion,
            Newest = newest,
            Changes = changes
        };
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? GetDate(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), out var parsed)
            ? parsed
            : null;
}
