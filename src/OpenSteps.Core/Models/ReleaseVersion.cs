using System.Globalization;

namespace OpenSteps.Core.Models;

/// <summary>
/// A released OpenSteps version, as it appears in a tag ("v0.6.0"), an assembly version ("0.6.0.0") or a
/// pre-release tag ("v0.6.0-beta.2"). Comparing two of these decides whether an update is newer, so the rules
/// are the usual ones: numbers are compared left to right, a missing number counts as zero, and a pre-release
/// comes before the same version without one (0.6.0-beta is older than 0.6.0).
/// </summary>
public readonly record struct ReleaseVersion : IComparable<ReleaseVersion>
{
    private ReleaseVersion(int major, int minor, int patch, string? prerelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = prerelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The part after "-", lower-cased, or null for a normal release.</summary>
    public string? Prerelease { get; }

    public bool IsPrerelease => Prerelease is not null;

    /// <summary>
    /// Reads a version from a tag name or version string. A leading "v", build metadata after "+" and a fourth
    /// number (assembly versions carry one) are ignored. Returns false when there is no number to read.
    /// </summary>
    public static bool TryParse(string? value, out ReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var plus = text.IndexOf('+');
        if (plus >= 0)
        {
            text = text[..plus];
        }

        string? prerelease = null;
        var dash = text.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = text[(dash + 1)..].Trim().ToLowerInvariant();
            text = text[..dash];
            if (prerelease.Length == 0)
            {
                prerelease = null;
            }
        }

        var parts = text.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (i >= parts.Length)
            {
                continue;
            }

            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            numbers[i] = number;
        }

        version = new ReleaseVersion(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    /// <summary>The version, or null when <paramref name="value"/> does not hold one.</summary>
    public static ReleaseVersion? Parse(string? value)
    {
        return TryParse(value, out var version) ? version : null;
    }

    /// <summary>This process's own version, from the running assembly.</summary>
    public static ReleaseVersion FromAssembly(Version? assemblyVersion)
    {
        return assemblyVersion is null
            ? new ReleaseVersion(0, 0, 0, null)
            : new ReleaseVersion(
                Math.Max(assemblyVersion.Major, 0),
                Math.Max(assemblyVersion.Minor, 0),
                Math.Max(assemblyVersion.Build, 0),
                null);
    }

    public int CompareTo(ReleaseVersion other)
    {
        var numbers = Major.CompareTo(other.Major);
        if (numbers == 0)
        {
            numbers = Minor.CompareTo(other.Minor);
        }

        if (numbers == 0)
        {
            numbers = Patch.CompareTo(other.Patch);
        }

        if (numbers != 0)
        {
            return numbers;
        }

        return (Prerelease, other.Prerelease) switch
        {
            (null, null) => 0,
            (null, not null) => 1,
            (not null, null) => -1,
            var (mine, theirs) => string.CompareOrdinal(mine, theirs)
        };
    }

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public override string ToString()
    {
        var number = $"{Major}.{Minor}.{Patch}";
        return Prerelease is null ? number : $"{number}-{Prerelease}";
    }

    /// <summary>The tag name this version is published under ("v0.6.0").</summary>
    public string ToTagName() => "v" + ToString();
}
