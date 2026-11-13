using System.Net.Http;
using System.Text;
using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v0.6.0", 0, 6, 0)]
    [InlineData("0.6.0", 0, 6, 0)]
    [InlineData("V1.2.3", 1, 2, 3)]
    [InlineData("0.6", 0, 6, 0)]
    [InlineData("2", 2, 0, 0)]
    [InlineData("0.5.0.0", 0, 5, 0)]
    [InlineData("  v0.5.1  ", 0, 5, 1)]
    [InlineData("0.6.0+build.7", 0, 6, 0)]
    public void ReadsTheNumbers(string text, int major, int minor, int patch)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.False(version.IsPrerelease);
    }

    [Theory]
    [InlineData("v0.6.0-beta.2", "beta.2")]
    [InlineData("0.6.0-RC1", "rc1")]
    public void ReadsAPrereleaseTag(string text, string expected)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version));
        Assert.True(version.IsPrerelease);
        Assert.Equal(expected, version.Prerelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("latest")]
    [InlineData("v")]
    [InlineData("release-candidate")]
    public void RefusesWhatIsNotAVersion(string? text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
        Assert.Null(ReleaseVersion.Parse(text));
    }

    [Fact]
    public void ComparesByNumberFromTheLeft()
    {
        Assert.True(Version("0.6.0") > Version("0.5.9"));
        Assert.True(Version("1.0.0") > Version("0.99.0"));
        Assert.True(Version("0.5.1") > Version("0.5.0"));
        Assert.Equal(Version("0.5.0"), Version("v0.5.0"));
    }

    [Fact]
    public void ComparesNumbersAboveAThousandProperly()
    {
        // A naive "major*1000000 + minor*1000 + patch" would get these the wrong way round.
        Assert.True(Version("0.1001.0") > Version("0.999.0"));
        Assert.True(Version("0.0.1200") > Version("0.0.999"));
    }

    [Fact]
    public void APrereleaseComesBeforeTheRealThing()
    {
        Assert.True(Version("0.6.0-beta") < Version("0.6.0"));
        Assert.True(Version("0.6.0-alpha") < Version("0.6.0-beta"));
        Assert.True(Version("0.6.0-beta") > Version("0.5.9"));
    }

    [Fact]
    public void ReadsTheRunningAssemblyVersion()
    {
        Assert.Equal(Version("1.2.3"), ReleaseVersion.FromAssembly(new Version(1, 2, 3, 4)));
        Assert.Equal(Version("0.0.0"), ReleaseVersion.FromAssembly(null));
    }

    [Fact]
    public void WritesItselfBackAsTextAndAsATag()
    {
        Assert.Equal("0.6.0", Version("v0.6.0").ToString());
        Assert.Equal("v0.6.0", Version("0.6.0").ToTagName());
        Assert.Equal("0.6.0-beta.2", Version("v0.6.0-beta.2").ToString());
    }

    private static ReleaseVersion Version(string text) => ReleaseVersion.Parse(text)!.Value;
}

public class UpdateCheckScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewInstallChecksStraightAway()
    {
        Assert.True(UpdateCheckSchedule.IsDue(null, Now));
    }

    [Fact]
    public void ChecksOnceADay()
    {
        Assert.False(UpdateCheckSchedule.IsDue(Now.AddHours(-2), Now));
        Assert.False(UpdateCheckSchedule.IsDue(Now.AddHours(-23), Now));
        Assert.True(UpdateCheckSchedule.IsDue(Now.AddHours(-25), Now));
        Assert.True(UpdateCheckSchedule.IsDue(Now.AddDays(-30), Now));
    }

    [Fact]
    public void AClockThatWentBackwardsDoesNotStopTheChecks()
    {
        Assert.True(UpdateCheckSchedule.IsDue(Now.AddDays(3), Now));
    }

    [Fact]
    public void SaysWhenTheNextCheckFallsDue()
    {
        Assert.Equal(Now.AddHours(-2).AddDays(1), UpdateCheckSchedule.NextDue(Now.AddHours(-2), Now));
        Assert.Equal(Now, UpdateCheckSchedule.NextDue(null, Now));
    }
}

public class GitHubReleasesTests
{
    private const string Json = """
    [
      {
        "tag_name": "v0.7.0",
        "name": "OpenSteps v0.7.0",
        "draft": true,
        "prerelease": false,
        "body": "Not published yet.",
        "assets": [{ "name": "OpenSteps-0.7.0-win-x64.zip", "browser_download_url": "https://example.test/0.7.0.zip", "size": 1 }]
      },
      {
        "tag_name": "v0.6.0",
        "name": "OpenSteps v0.6.0",
        "draft": false,
        "prerelease": false,
        "published_at": "2026-11-20T10:00:00Z",
        "body": "### Added\n- In-app updates",
        "assets": [
          { "name": "OpenSteps-0.6.0-win-x64.zip", "browser_download_url": "https://example.test/0.6.0.zip", "size": 77298765 },
          { "name": "OpenSteps-0.6.0-win-x64.zip.sha256", "browser_download_url": "https://example.test/0.6.0.zip.sha256", "size": 80 }
        ]
      },
      {
        "tag_name": "v0.6.1-beta.1",
        "draft": false,
        "prerelease": true,
        "published_at": "2026-11-25T10:00:00Z",
        "body": "Testing.",
        "assets": [{ "name": "OpenSteps-0.6.1-beta.1-win-x64.zip", "browser_download_url": "https://example.test/beta.zip", "size": 10 }]
      },
      {
        "tag_name": "v0.5.0",
        "draft": false,
        "prerelease": false,
        "published_at": "2026-11-12T10:00:00Z",
        "body": "### Changed\n- Keyboard hook",
        "assets": [{ "name": "OpenSteps-0.5.0-win-x64.zip", "browser_download_url": "https://example.test/0.5.0.zip", "size": 5 }]
      },
      { "tag_name": "nightly", "draft": false, "body": "No version in the tag." },
      "not an object"
    ]
    """;

    [Fact]
    public void ReadsThePublishedReleasesNewestFirst()
    {
        var releases = GitHubReleases.Parse(Json);

        Assert.Equal(["v0.6.1-beta.1", "v0.6.0", "v0.5.0"], releases.Select(release => release.TagName));
    }

    [Fact]
    public void LeavesOutDraftsAndTagsWithoutAVersion()
    {
        var releases = GitHubReleases.Parse(Json);

        Assert.DoesNotContain(releases, release => release.TagName == "v0.7.0");
        Assert.DoesNotContain(releases, release => release.TagName == "nightly");
    }

    [Fact]
    public void ReadsThePackageAndItsChecksum()
    {
        var release = GitHubReleases.Parse(Json).Single(candidate => candidate.TagName == "v0.6.0");

        Assert.Equal("https://example.test/0.6.0.zip", release.PackageUrl);
        Assert.Equal("OpenSteps-0.6.0-win-x64.zip", release.PackageName);
        Assert.Equal("https://example.test/0.6.0.zip.sha256", release.ChecksumUrl);
        Assert.Equal(77298765, release.PackageSize);
        Assert.True(release.CanInstall);
        Assert.Equal("OpenSteps v0.6.0", release.Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), release.PublishedAt);
    }

    [Fact]
    public void MarksAPrereleaseEvenWhenOnlyTheTagSaysSo()
    {
        var releases = GitHubReleases.Parse(Json);

        Assert.True(releases.Single(release => release.TagName == "v0.6.1-beta.1").IsPrerelease);
        Assert.False(releases.Single(release => release.TagName == "v0.6.0").IsPrerelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyAnswerIsNoReleases(string json)
    {
        Assert.Empty(GitHubReleases.Parse(json));
    }

    [Fact]
    public void OffersTheNewestVersionAndEverythingThatChangedToGetThere()
    {
        var result = GitHubReleases.SelectUpdate(GitHubReleases.Parse(Json), Version("0.4.0"));

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal("v0.6.0", result.Newest!.TagName);
        Assert.Equal(["v0.6.0", "v0.5.0"], result.Changes.Select(release => release.TagName));
        Assert.True(result.HasUpdate);
    }

    [Fact]
    public void SaysNothingIsOnOfferWhenTheRunningVersionIsTheNewest()
    {
        var result = GitHubReleases.SelectUpdate(GitHubReleases.Parse(Json), Version("0.6.0"));

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
        Assert.Null(result.Newest);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public void ADevelopmentBuildAheadOfEveryReleaseIsUpToDate()
    {
        Assert.Equal(UpdateStatus.UpToDate, GitHubReleases.SelectUpdate(GitHubReleases.Parse(Json), Version("9.9.9")).Status);
    }

    [Fact]
    public void LeavesPrereleasesOutUnlessTheyAreAskedFor()
    {
        var releases = GitHubReleases.Parse(Json);

        Assert.Equal("v0.6.0", GitHubReleases.SelectUpdate(releases, Version("0.5.0")).Newest!.TagName);
        Assert.Equal(
            "v0.6.1-beta.1",
            GitHubReleases.SelectUpdate(releases, Version("0.5.0"), includePrereleases: true).Newest!.TagName);
    }

    [Fact]
    public void AVersionTheUserSkippedIsNotOfferedAgain()
    {
        var result = GitHubReleases.SelectUpdate(GitHubReleases.Parse(Json), Version("0.5.0"), skippedVersion: "0.6.0");

        Assert.Equal(UpdateStatus.Skipped, result.Status);
        Assert.False(result.HasUpdate);
        Assert.Equal("v0.6.0", result.Newest!.TagName);
    }

    [Fact]
    public void SkippingAnOlderVersionDoesNotHideANewerOne()
    {
        var result = GitHubReleases.SelectUpdate(GitHubReleases.Parse(Json), Version("0.4.0"), skippedVersion: "0.5.0");

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal("v0.6.0", result.Newest!.TagName);
    }

    [Fact]
    public void IgnoresANewerReleaseThatHasNoWindowsPackage()
    {
        var json = """
        [
          { "tag_name": "v0.9.0", "draft": false, "body": "Source only.", "assets": [] },
          { "tag_name": "v0.6.0", "draft": false, "body": "Installable.",
            "assets": [{ "name": "OpenSteps-0.6.0-win-x64.zip", "browser_download_url": "https://example.test/z.zip", "size": 3 }] }
        ]
        """;

        var result = GitHubReleases.SelectUpdate(GitHubReleases.Parse(json), Version("0.5.0"));

        Assert.Equal("v0.6.0", result.Newest!.TagName);
        Assert.Equal(["v0.6.0"], result.Changes.Select(release => release.TagName));
    }

    private static ReleaseVersion Version(string text) => ReleaseVersion.Parse(text)!.Value;
}

public class ReleaseNotesTests
{
    [Fact]
    public void ReadsHeadingsBulletsAndNesting()
    {
        var lines = ReleaseNotes.Format("### Added\n- A thing\n  - A smaller thing\n1. First\n\nA paragraph.");

        Assert.Equal(ReleaseNoteLineKind.Heading, lines[0].Kind);
        Assert.Equal("Added", lines[0].Text);
        Assert.Equal(ReleaseNoteLineKind.Bullet, lines[1].Kind);
        Assert.Equal(0, lines[1].Indent);
        Assert.Equal("A thing", lines[1].Text);
        Assert.Equal(1, lines[2].Indent);
        Assert.Equal("A smaller thing", lines[2].Text);
        Assert.Equal(ReleaseNoteLineKind.Bullet, lines[3].Kind);
        Assert.Equal("First", lines[3].Text);
        Assert.Equal(ReleaseNoteLineKind.Paragraph, lines[^1].Kind);
        Assert.Equal("A paragraph.", lines[^1].Text);
    }

    [Fact]
    public void KeepsBoldAndCodeAndTheTextOfALink()
    {
        var spans = ReleaseNotes.ParseSpans("**Keyboard** uses `Ctrl+S`, see [the changelog](https://example.test/c).");

        Assert.Contains(spans, span => span is { Text: "Keyboard", Bold: true });
        Assert.Contains(spans, span => span is { Text: "Ctrl+S", Code: true });
        Assert.Contains("the changelog", string.Concat(spans.Select(span => span.Text)));
        Assert.DoesNotContain("https://example.test/c", string.Concat(spans.Select(span => span.Text)));
    }

    [Fact]
    public void DropsMarkdownThatMeansNothingOnScreen()
    {
        var lines = ReleaseNotes.Format("---\n<!-- a comment -->\n## Real heading\n***\n");

        Assert.Single(lines);
        Assert.Equal("Real heading", lines[0].Text);
    }

    [Fact]
    public void KeepsAFencedCodeBlockAsCode()
    {
        var lines = ReleaseNotes.Format("Run:\n```\nInstall-OpenSteps.ps1\n```\n");

        Assert.Contains(lines, line => line.Spans.Any(span => span is { Text: "Install-OpenSteps.ps1", Code: true }));
    }

    [Fact]
    public void CollapsesRunsOfBlankLinesAndTrimsTheEnd()
    {
        var lines = ReleaseNotes.Format("One\n\n\n\nTwo\n\n\n");

        Assert.Equal(3, lines.Count);
        Assert.Equal("One", lines[0].Text);
        Assert.Empty(lines[1].Spans);
        Assert.Equal("Two", lines[2].Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoNotesIsNoLines(string? markdown)
    {
        Assert.Empty(ReleaseNotes.Format(markdown));
    }

    [Fact]
    public void PutsEachVersionUnderItsOwnHeadingNewestFirst()
    {
        var releases = new[]
        {
            Release("v0.6.0", "- New thing"),
            Release("v0.5.1", string.Empty)
        };

        var lines = ReleaseNotes.FormatAll(releases);
        var text = ReleaseNotes.ToPlainText(lines);

        Assert.Contains("Version 0.6.0", text);
        Assert.Contains("• New thing", text);
        Assert.Contains("Version 0.5.1", text);
        Assert.Contains("No notes were published for this version.", text);
        Assert.True(text.IndexOf("Version 0.6.0", StringComparison.Ordinal) < text.IndexOf("Version 0.5.1", StringComparison.Ordinal));
    }

    [Fact]
    public void StopsAtTheLineLimitSoOneReleaseCannotFloodTheWindow()
    {
        var markdown = string.Join("\n", Enumerable.Range(0, ReleaseNotes.MaxLines * 2).Select(i => $"- line {i}"));

        Assert.Equal(ReleaseNotes.MaxLines, ReleaseNotes.Format(markdown).Count);
    }

    private static ReleaseInfo Release(string tag, string notes) => new()
    {
        TagName = tag,
        Version = ReleaseVersion.Parse(tag)!.Value,
        Notes = notes
    };
}

public class PackageChecksumTests
{
    private const string Hash = "67DF6CD607DD471F42D14853FBC16B51C36337E3694D346AD5E2881FF6999606";

    [Fact]
    public void ReadsTheHashFromTheFilePublishedWithThePackage()
    {
        Assert.Equal(Hash, PackageChecksum.Parse($"{Hash}  OpenSteps-0.6.0-win-x64.zip"));
        Assert.Equal(Hash, PackageChecksum.Parse($"{Hash.ToLowerInvariant()}  OpenSteps-0.6.0-win-x64.zip\n"));
        Assert.Equal(Hash, PackageChecksum.Parse($"# a comment\n{Hash} *file.zip"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no hash here")]
    [InlineData("67DF6CD607DD471F42D14853FBC16B51C36337E3694D346AD5E2881FF699960")]
    [InlineData("ZZDF6CD607DD471F42D14853FBC16B51C36337E3694D346AD5E2881FF6999606")]
    public void RefusesWhatIsNotASha256(string? contents)
    {
        Assert.Null(PackageChecksum.Parse(contents));
    }

    [Fact]
    public void ComparesHashesIgnoringCase()
    {
        Assert.True(PackageChecksum.Matches(Hash, Hash.ToLowerInvariant()));
        Assert.True(PackageChecksum.Matches($" {Hash} ", Hash));
        Assert.False(PackageChecksum.Matches(Hash, Hash.Replace('6', '7')));
        Assert.False(PackageChecksum.Matches(null, Hash));
        Assert.False(PackageChecksum.Matches(Hash, null));
    }

    [Fact]
    public void WritesDownloadSizesForPeople()
    {
        Assert.Equal(string.Empty, PackageChecksum.FormatSize(0));
        Assert.Contains("KB", PackageChecksum.FormatSize(64 * 1024));
        Assert.Contains("MB", PackageChecksum.FormatSize(73_700_000));
    }
}

public class UpdateInstallationTests
{
    private const string InstallDir = @"C:\Users\someone\AppData\Local\Programs\OpenSteps";

    [Fact]
    public void AnInstalledCopyCanReplaceItself()
    {
        var installation = UpdateInstallation.Describe(
            InstallDir,
            path => path.EndsWith(UpdateInstallation.InstallerScriptName, StringComparison.Ordinal),
            _ => true);

        Assert.True(installation.IsInstalled);
        Assert.True(installation.CanSelfUpdate);
        Assert.Null(installation.Blocker);
        Assert.Equal(InstallDir, installation.InstallDirectory);
        Assert.NotNull(installation.InstallerScriptPath);
    }

    [Fact]
    public void AFolderWithOnlyTheUninstallScriptStillCountsAsInstalled()
    {
        var installation = UpdateInstallation.Describe(
            InstallDir,
            path => path.EndsWith(UpdateInstallation.UninstallerScriptName, StringComparison.Ordinal),
            _ => true);

        Assert.True(installation.CanSelfUpdate);
        Assert.Null(installation.InstallerScriptPath);
    }

    [Fact]
    public void ABuildRunFromSomewhereElseWillNotOverwriteItsFolder()
    {
        var installation = UpdateInstallation.Describe(@"C:\repo\src\OpenSteps.App\bin\Release", _ => false, _ => true);

        Assert.False(installation.IsInstalled);
        Assert.False(installation.CanSelfUpdate);
        Assert.Contains("not installed into", installation.Blocker!);
    }

    [Fact]
    public void AFolderItCannotWriteToIsAJobForIt()
    {
        var installation = UpdateInstallation.Describe(@"C:\Program Files\OpenSteps", _ => true, _ => false);

        Assert.True(installation.IsInstalled);
        Assert.False(installation.CanSelfUpdate);
        Assert.Contains("IT department", installation.Blocker!);
    }

    [Fact]
    public void TrailingSlashesDoNotChangeTheInstallFolder()
    {
        Assert.Equal(InstallDir, UpdateInstallation.Describe(InstallDir + @"\", _ => true, _ => true).InstallDirectory);
    }

    [Fact]
    public void BuildsTheInstallerCommandWithThePackageFolderAndChecksum()
    {
        var arguments = UpdateInstallation.BuildInstallerArguments(
            @"C:\temp\Install-OpenSteps.ps1",
            @"C:\temp\OpenSteps-0.6.0-win-x64.zip",
            InstallDir,
            "ABC123");

        Assert.Equal(["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", @"C:\temp\Install-OpenSteps.ps1"], arguments.Take(5));
        AssertPair(arguments, "-Source", @"C:\temp\OpenSteps-0.6.0-win-x64.zip");
        AssertPair(arguments, "-InstallDir", InstallDir);
        AssertPair(arguments, "-ExpectedSha256", "ABC123");
        Assert.Contains("-Force", arguments);
        Assert.Contains("-Launch", arguments);
    }

    [Fact]
    public void LeavesOutTheChecksumWhenThereIsNoneAndCanBeToldNotToRelaunch()
    {
        var arguments = UpdateInstallation.BuildInstallerArguments(
            "installer.ps1", "package.zip", InstallDir, expectedSha256: null, launchWhenDone: false);

        Assert.DoesNotContain("-ExpectedSha256", arguments);
        Assert.DoesNotContain("-Launch", arguments);
    }

    private static void AssertPair(IReadOnlyList<string> arguments, string name, string value)
    {
        var index = arguments.ToList().IndexOf(name);
        Assert.True(index >= 0, $"{name} is missing");
        Assert.Equal(value, arguments[index + 1]);
    }
}

public class UpdateAttemptMarkerTests
{
    [Fact]
    public void RemembersWhatWasBeingInstalledAndForgetsItOnRequest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensteps-update-{Guid.NewGuid():N}.json");
        var marker = new UpdateAttemptMarker(path);
        try
        {
            Assert.Null(marker.Read());

            marker.Write(ReleaseVersion.Parse("0.6.0")!.Value, @"C:\log\install.log");
            var attempt = marker.Read();

            Assert.NotNull(attempt);
            Assert.Equal("0.6.0", attempt.Version);
            Assert.Equal(@"C:\log\install.log", attempt.LogPath);

            marker.Clear();
            Assert.Null(marker.Read());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StillOnTheOldVersionMeansTheUpdateFailed()
    {
        var attempt = new UpdateAttemptInfo("0.6.0", DateTimeOffset.Now, "log");

        Assert.True(UpdateAttemptMarker.IsFailedAttempt(attempt, ReleaseVersion.Parse("0.5.0")!.Value));
    }

    [Fact]
    public void RunningTheNewVersionMeansItWorked()
    {
        var attempt = new UpdateAttemptInfo("0.6.0", DateTimeOffset.Now, "log");

        Assert.False(UpdateAttemptMarker.IsFailedAttempt(attempt, ReleaseVersion.Parse("0.6.0")!.Value));
        Assert.False(UpdateAttemptMarker.IsFailedAttempt(attempt, ReleaseVersion.Parse("0.7.0")!.Value));
    }

    [Fact]
    public void NoMarkerAndAnUnreadableOneAreBothFine()
    {
        var running = ReleaseVersion.Parse("0.5.0")!.Value;

        Assert.False(UpdateAttemptMarker.IsFailedAttempt(null, running));
        Assert.False(UpdateAttemptMarker.IsFailedAttempt(new UpdateAttemptInfo("not-a-version", DateTimeOffset.Now, "log"), running));
    }
}

public class UpdateServiceTests
{
    private const string Hash = "67DF6CD607DD471F42D14853FBC16B51C36337E3694D346AD5E2881FF6999606";

    private static string ReleasesJson(string tag = "v0.6.0", bool withChecksum = true)
    {
        var checksum = withChecksum
            ? """, { "name": "OpenSteps-0.6.0-win-x64.zip.sha256", "browser_download_url": "https://example.test/sum" }"""
            : string.Empty;
        return $$"""
        [{
          "tag_name": "{{tag}}",
          "draft": false,
          "prerelease": false,
          "body": "- Something new",
          "assets": [
            { "name": "OpenSteps-0.6.0-win-x64.zip", "browser_download_url": "https://example.test/package", "size": 12 }{{checksum}}
          ]
        }]
        """;
    }

    [Fact]
    public async Task FindsAnUpdateOnTheReleasesPage()
    {
        var service = new UpdateService(new FakeClient { Text = ReleasesJson() }, downloadDirectory: TempDirectory());

        var result = await service.CheckAsync(ReleaseVersion.Parse("0.5.0")!.Value);

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal("v0.6.0", result.Newest!.TagName);
    }

    [Fact]
    public async Task AFailedCheckIsReportedQuietlyRatherThanThrown()
    {
        var service = new UpdateService(
            new FakeClient { Failure = new HttpRequestException("the network is not there") },
            downloadDirectory: TempDirectory());

        var result = await service.CheckAsync(ReleaseVersion.Parse("0.5.0")!.Value);

        Assert.Equal(UpdateStatus.CheckFailed, result.Status);
        Assert.Contains("the network is not there", result.Error!);
        Assert.False(result.HasUpdate);
    }

    [Fact]
    public async Task DownloadsThePackageAndChecksItAgainstThePublishedHash()
    {
        var directory = TempDirectory();
        var payload = Encoding.UTF8.GetBytes("pretend this is a zip");
        var client = new FakeClient { Download = payload, Text = $"{Sha256Of(payload)}  OpenSteps-0.6.0-win-x64.zip" };
        var service = new UpdateService(client, downloadDirectory: directory);

        try
        {
            var download = await service.DownloadAsync(Release());

            Assert.True(File.Exists(download.PackagePath));
            Assert.True(download.ChecksumVerified);
            Assert.Equal(Sha256Of(payload), download.Sha256);
            Assert.Equal("OpenSteps-0.6.0-win-x64.zip", Path.GetFileName(download.PackagePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ThrowsAwayADownloadThatDoesNotMatchItsChecksum()
    {
        var directory = TempDirectory();
        var client = new FakeClient
        {
            Download = Encoding.UTF8.GetBytes("damaged"),
            Text = $"{Hash}  OpenSteps-0.6.0-win-x64.zip"
        };
        var service = new UpdateService(client, downloadDirectory: directory);

        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync(Release()));

            Assert.Contains("checksum", error.Message);
            Assert.Empty(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task APackageWithNoPublishedChecksumIsStillInstalledButNotMarkedVerified()
    {
        var directory = TempDirectory();
        var service = new UpdateService(
            new FakeClient { Download = Encoding.UTF8.GetBytes("zip") },
            downloadDirectory: directory);

        try
        {
            var download = await service.DownloadAsync(Release(withChecksum: false));

            Assert.False(download.ChecksumVerified);
            Assert.Null(download.Sha256);
            Assert.True(File.Exists(download.PackagePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RefusesAReleaseWithNothingToInstall()
    {
        var service = new UpdateService(new FakeClient(), downloadDirectory: TempDirectory());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.DownloadAsync(new ReleaseInfo { TagName = "v0.6.0", Version = ReleaseVersion.Parse("0.6.0")!.Value }));
    }

    [Fact]
    public void PointsAtTheProjectsReleasesPage()
    {
        var service = new UpdateService(new FakeClient(), "someone/SomeRepo", TempDirectory());

        Assert.Equal("https://github.com/someone/SomeRepo/releases", service.ReleasesPageUrl);
        Assert.Equal("someone/SomeRepo", service.Repository);
    }

    private static ReleaseInfo Release(bool withChecksum = true) => new()
    {
        TagName = "v0.6.0",
        Version = ReleaseVersion.Parse("0.6.0")!.Value,
        PackageUrl = "https://example.test/package",
        PackageName = "OpenSteps-0.6.0-win-x64.zip",
        ChecksumUrl = withChecksum ? "https://example.test/sum" : null
    };

    private static string Sha256Of(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), $"opensteps-updates-{Guid.NewGuid():N}");

    private sealed class FakeClient : IUpdateClient
    {
        public string Text { get; init; } = "[]";

        public byte[] Download { get; init; } = [];

        public Exception? Failure { get; init; }

        public Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
        {
            return Failure is not null ? Task.FromException<string>(Failure) : Task.FromResult(Text);
        }

        public async Task DownloadFileAsync(
            string url,
            string destinationPath,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, Download, cancellationToken);
            progress?.Report(new DownloadProgress(Download.Length, Download.Length));
        }
    }
}

public class UpdatePolicyAndSettingsTests
{
    [Fact]
    public void ItCanTurnOffUpdateChecksByPolicy()
    {
        var policy = RecordingPolicy.FromValues(name => name == RecordingPolicy.DisableUpdateChecksValue ? 1 : null);

        Assert.True(policy.DisableUpdateChecks);
        Assert.False(policy.DisableTypedTextCapture);
        Assert.False(policy.DisableVideoRecording);
    }

    [Fact]
    public void NoPolicyLeavesUpdateChecksOn()
    {
        Assert.False(RecordingPolicy.None.DisableUpdateChecks);
        Assert.False(RecordingPolicy.FromValues(_ => null).DisableUpdateChecks);
        Assert.False(RecordingPolicy.FromValues(_ => 0).DisableUpdateChecks);
    }

    [Fact]
    public async Task UpdateSettingsSurviveBeingSavedAndLoaded()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensteps-settings-{Guid.NewGuid():N}.json");
        var service = new SettingsService(path);
        try
        {
            var checkedAt = new DateTimeOffset(2026, 11, 12, 8, 30, 0, TimeSpan.Zero);
            await service.SaveAsync(new AppSettings
            {
                CheckForUpdates = false,
                LastUpdateCheck = checkedAt,
                SkippedUpdateVersion = "0.6.0",
                IncludePrereleaseUpdates = true
            });

            var loaded = await service.LoadAsync();

            Assert.False(loaded.CheckForUpdates);
            Assert.Equal(checkedAt, loaded.LastUpdateCheck);
            Assert.Equal("0.6.0", loaded.SkippedUpdateVersion);
            Assert.True(loaded.IncludePrereleaseUpdates);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ANewInstallChecksForUpdatesAndHasSkippedNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensteps-settings-{Guid.NewGuid():N}.json");
        var settings = await new SettingsService(path).LoadAsync();

        Assert.True(settings.CheckForUpdates);
        Assert.Null(settings.LastUpdateCheck);
        Assert.Null(settings.SkippedUpdateVersion);
        Assert.False(settings.IncludePrereleaseUpdates);
    }
}
