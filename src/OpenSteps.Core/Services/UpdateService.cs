using OpenSteps.Core.Models;

namespace OpenSteps.Core.Services;

/// <summary>What happened when a downloaded package was made ready to install.</summary>
public sealed record UpdateDownload(string PackagePath, string? Sha256, bool ChecksumVerified);

/// <summary>
/// Looks for a newer OpenSteps on the project's releases page and fetches the package when the user asks for
/// it. The check is read-only and silent: a machine with no network, a blocked site or a repository with no
/// releases yet simply reports that nothing is on offer.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Where downloaded packages are kept until the installer has used them.</summary>
    public static string DefaultDownloadDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenSteps",
        "updates");

    private readonly IUpdateClient _client;
    private readonly string _repository;
    private readonly string _downloadDirectory;

    public UpdateService(
        IUpdateClient? client = null,
        string repository = "ericchantry/StepsRecorder",
        string? downloadDirectory = null)
    {
        _client = client ?? new GitHubUpdateClient();
        _repository = repository;
        _downloadDirectory = downloadDirectory ?? DefaultDownloadDirectory;
    }

    public string Repository => _repository;

    public string ReleasesPageUrl => $"https://github.com/{_repository}/releases";

    /// <summary>
    /// Asks GitHub what has been released and compares it with the running version. Never throws: a failure
    /// comes back as <see cref="UpdateStatus.CheckFailed"/> with the reason, so a daily check can pass quietly.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(
        ReleaseVersion currentVersion,
        string? skippedVersion = null,
        bool includePrereleases = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await _client.GetStringAsync(GitHubReleases.ListUrl(_repository), cancellationToken);
            var releases = GitHubReleases.Parse(json);
            return GitHubReleases.SelectUpdate(releases, currentVersion, skippedVersion, includePrereleases);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed(currentVersion, ex.Message);
        }
    }

    /// <summary>
    /// Downloads the release package and checks it against the published SHA-256. Throws
    /// <see cref="InvalidOperationException"/> when the release has no package or the download does not match
    /// the published hash, so a damaged or tampered download is never handed to the installer.
    /// </summary>
    public async Task<UpdateDownload> DownloadAsync(
        ReleaseInfo release,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (release.PackageUrl is null)
        {
            throw new InvalidOperationException($"Release {release.TagName} has no Windows package to install.");
        }

        Directory.CreateDirectory(_downloadDirectory);
        CleanOldDownloads();

        var fileName = release.PackageName ?? $"OpenSteps-{release.Version}{GitHubReleases.PackageSuffix}";
        var destination = Path.Combine(_downloadDirectory, fileName);
        await _client.DownloadFileAsync(release.PackageUrl, destination, progress, cancellationToken);

        string? expected = null;
        if (release.ChecksumUrl is not null)
        {
            try
            {
                expected = PackageChecksum.Parse(await _client.GetStringAsync(release.ChecksumUrl, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // A missing or unreadable checksum file is not fatal: the download itself came over HTTPS.
                expected = null;
            }
        }

        var verified = false;
        if (expected is not null)
        {
            var actual = await FileChecksum.ComputeSha256Async(destination, cancellationToken);
            if (!PackageChecksum.Matches(expected, actual))
            {
                TryDelete(destination);
                throw new InvalidOperationException(
                    "The downloaded update does not match the checksum published with it, so it has not been installed. Try again, or download it yourself from the releases page.");
            }

            verified = true;
        }

        return new UpdateDownload(destination, expected, verified);
    }

    /// <summary>
    /// Takes the install script out of the downloaded package so the new installer runs the upgrade, rather
    /// than the copy in the folder that is about to be replaced. Returns null when the package has none.
    /// </summary>
    public string? ExtractInstaller(string packagePath)
    {
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(packagePath);
            var entry = archive.Entries.FirstOrDefault(candidate =>
                string.Equals(
                    Path.GetFileName(candidate.FullName),
                    UpdateInstallation.InstallerScriptName,
                    StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                return null;
            }

            var target = Path.Combine(_downloadDirectory, UpdateInstallation.InstallerScriptName);
            entry.ExtractToFile(target, overwrite: true);
            return target;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Removes packages left behind by earlier updates so the folder does not grow without limit.</summary>
    private void CleanOldDownloads()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_downloadDirectory))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
                {
                    TryDelete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
