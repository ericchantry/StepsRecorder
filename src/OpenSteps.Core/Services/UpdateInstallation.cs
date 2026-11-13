using System.Globalization;
using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>
/// Where OpenSteps is installed and whether it can replace itself there. An installed copy sits in a folder
/// that holds the per-user install script shipped in the release package; a build run from Visual Studio or a
/// folder someone unzipped by hand has no script, and OpenSteps then points at the releases page instead of
/// overwriting files it does not own.
/// </summary>
public sealed record UpdateInstallation
{
    public const string InstallerScriptName = "Install-OpenSteps.ps1";
    public const string UninstallerScriptName = "Uninstall-OpenSteps.ps1";

    public required string InstallDirectory { get; init; }

    /// <summary>The install script in the installed folder, when it is there.</summary>
    public string? InstallerScriptPath { get; init; }

    /// <summary>True when this copy was put here by the installer, so replacing it is the installer's job.</summary>
    public bool IsInstalled { get; init; }

    /// <summary>Why this copy cannot update itself, for the message shown to the user. Null when it can.</summary>
    public string? Blocker { get; init; }

    public bool CanSelfUpdate => IsInstalled && Blocker is null;

    /// <summary>
    /// Works out how this copy of OpenSteps is installed. <paramref name="fileExists"/> and
    /// <paramref name="directoryWritable"/> are taken as arguments so the decision can be tested.
    /// </summary>
    public static UpdateInstallation Describe(
        string baseDirectory,
        Func<string, bool> fileExists,
        Func<string, bool> directoryWritable)
    {
        var directory = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var installer = Path.Combine(directory, InstallerScriptName);
        var uninstaller = Path.Combine(directory, UninstallerScriptName);
        var installed = fileExists(installer) || fileExists(uninstaller);

        string? blocker = null;
        if (!installed)
        {
            blocker = "OpenSteps is running from a folder it was not installed into, so it will not replace the files there.";
        }
        else if (!directoryWritable(directory))
        {
            blocker = "OpenSteps cannot write to its own folder, so an administrator or your IT department needs to install the update.";
        }

        return new UpdateInstallation
        {
            InstallDirectory = directory,
            InstallerScriptPath = fileExists(installer) ? installer : null,
            IsInstalled = installed,
            Blocker = blocker
        };
    }

    /// <summary>
    /// The command that installs the downloaded package: the per-user install script, pointed at the zip, the
    /// folder to install into and the checksum to verify, and told to start OpenSteps again when it is done.
    /// Returned as separate arguments so no quoting is needed at the call site.
    /// </summary>
    public static IReadOnlyList<string> BuildInstallerArguments(
        string installerScriptPath,
        string packagePath,
        string installDirectory,
        string? expectedSha256 = null,
        bool launchWhenDone = true)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            installerScriptPath,
            "-Source",
            packagePath,
            "-InstallDir",
            installDirectory,
            "-Force"
        };

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            arguments.Add("-ExpectedSha256");
            arguments.Add(expectedSha256.Trim());
        }

        if (launchWhenDone)
        {
            arguments.Add("-Launch");
        }

        return arguments;
    }
}

/// <summary>What OpenSteps was updating to when it closed, so a failed update can be reported on the next start.</summary>
public sealed record UpdateAttemptInfo(string Version, DateTimeOffset StartedAt, string LogPath);

/// <summary>
/// A small file written just before OpenSteps closes to let the installer replace it. The new version clears it
/// on start-up; if the old version starts instead, the update did not finish and OpenSteps can say so and point
/// at the install log.
/// </summary>
public sealed class UpdateAttemptMarker
{
    public UpdateAttemptMarker(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenSteps",
            "update-in-progress.json");
    }

    public string Path { get; }

    public void Write(ReleaseVersion version, string logPath)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(
                Path,
                System.Text.Json.JsonSerializer.Serialize(
                    new UpdateAttemptInfo(version.ToString(), DateTimeOffset.Now, logPath)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The marker only improves the message after a failure; never block the update for it.
        }
    }

    public void Clear()
    {
        try
        {
            File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public UpdateAttemptInfo? Read()
    {
        try
        {
            return File.Exists(Path)
                ? System.Text.Json.JsonSerializer.Deserialize<UpdateAttemptInfo>(File.ReadAllText(Path))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a marker left behind means the update failed. It did if the running version is still older than
    /// the version being installed; if the running version matches or is newer, the update worked.
    /// </summary>
    public static bool IsFailedAttempt(UpdateAttemptInfo? attempt, ReleaseVersion runningVersion)
    {
        return attempt is not null
            && ReleaseVersion.TryParse(attempt.Version, out var wanted)
            && runningVersion < wanted;
    }
}

/// <summary>The SHA-256 file published next to a release package: "&lt;hash&gt;  &lt;file name&gt;".</summary>
public static class PackageChecksum
{
    /// <summary>The hash from a .sha256 file's contents, upper-cased, or null when there is none to read.</summary>
    public static string? Parse(string? contents)
    {
        if (string.IsNullOrWhiteSpace(contents))
        {
            return null;
        }

        foreach (var line in contents.ReplaceLineEndings("\n").Split('\n'))
        {
            var candidate = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (candidate is { Length: 64 } && candidate.All(Uri.IsHexDigit))
            {
                return candidate.ToUpperInvariant();
            }
        }

        return null;
    }

    public static bool Matches(string? expected, string? actual)
    {
        return !string.IsNullOrWhiteSpace(expected)
            && !string.IsNullOrWhiteSpace(actual)
            && string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A download size as "73.7 MB", for the progress line.</summary>
    public static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return string.Empty;
        }

        return bytes >= 1024 * 1024
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.#} MB", bytes / 1024d / 1024d)
            : string.Format(CultureInfo.CurrentCulture, "{0:0} KB", bytes / 1024d);
    }
}
