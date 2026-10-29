using ISLe8.Core.Settings;
using ISLe8.Core.Updates;

namespace ISLe8.Core.Tests;

/// <summary>Updates from the GitHub releases (Settings › General › Updates).</summary>
public class UpdateTests
{
    private const string Latest = """
        {
          "tag_name": "v0.2.0",
          "html_url": "https://github.com/Mericsdc/ISLe8-Windows/releases/tag/v0.2.0",
          "draft": false,
          "prerelease": false,
          "body": "## Kurulum",
          "assets": [
            { "name": "ISLe8-0.2.0-win-x64.zip", "size": 40000000, "digest": "sha256:ABCDEF0123",
              "browser_download_url": "https://github.com/Mericsdc/ISLe8-Windows/releases/download/v0.2.0/ISLe8-0.2.0-win-x64.zip" },
            { "name": "ISLe8-0.2.0-win-x64-package.zip", "size": 41000000,
              "browser_download_url": "https://github.com/Mericsdc/ISLe8-Windows/releases/download/v0.2.0/ISLe8-0.2.0-win-x64-package.zip" },
            { "name": "ISLe8-0.2.0-win-arm64.zip", "size": 39000000,
              "browser_download_url": "https://github.com/Mericsdc/ISLe8-Windows/releases/download/v0.2.0/ISLe8-0.2.0-win-arm64.zip" }
          ]
        }
        """;

    [Fact]
    public void ALatestReleaseIsReadWithItsFiles()
    {
        var release = UpdateRules.Parse(Latest)!;

        Assert.Equal(new Version(0, 2, 0), release.Version);
        Assert.Equal("v0.2.0", release.Tag);
        Assert.Equal(3, release.Assets.Count);
        Assert.Equal("abcdef0123", release.Assets[0].Sha256);
        Assert.Null(release.Assets[1].Sha256);
        Assert.Equal(40000000, release.Assets[0].Size);
    }

    [Fact]
    public void OnlyANewerVersionIsAnUpdate()
    {
        var release = UpdateRules.Parse(Latest)!;

        Assert.True(UpdateRules.IsNewer(release, new Version(0, 1, 1)));
        Assert.True(UpdateRules.IsNewer(release, new Version(0, 1, 9, 0)));
        Assert.False(UpdateRules.IsNewer(release, new Version(0, 2, 0, 0)));
        Assert.False(UpdateRules.IsNewer(release, new Version(0, 3, 0)));
    }

    [Fact]
    public void TheInstalledAppTakesThePackageAndTheZipTheZip()
    {
        var release = UpdateRules.Parse(Latest)!;

        Assert.Equal("ISLe8-0.2.0-win-x64-package.zip", UpdateRules.AssetFor(release, "win-x64", packaged: true)?.Name);
        Assert.Equal("ISLe8-0.2.0-win-x64.zip", UpdateRules.AssetFor(release, "win-x64", packaged: false)?.Name);
        Assert.Equal("ISLe8-0.2.0-win-arm64.zip", UpdateRules.AssetFor(release, "win-arm64", packaged: false)?.Name);
        Assert.Null(UpdateRules.AssetFor(release, "win-arm64", packaged: true));
    }

    [Fact]
    public void DraftsPrereleasesAndOddTagsAreNotUpdates()
    {
        Assert.Null(UpdateRules.Parse(Latest.Replace("\"draft\": false", "\"draft\": true")));
        Assert.Null(UpdateRules.Parse(Latest.Replace("\"prerelease\": false", "\"prerelease\": true")));
        Assert.Null(UpdateRules.Parse(Latest.Replace("v0.2.0\",\n  \"html_url", "nightly\",\n  \"html_url")));
        Assert.Null(UpdateRules.Parse("not json"));
        Assert.Null(UpdateRules.Parse("[]"));
        Assert.Equal(new Version(1, 4, 0), UpdateRules.VersionOf("V1.4"));
        Assert.Null(UpdateRules.VersionOf("nightly"));
    }

    [Fact]
    public void OnlyTheProjectsOwnReleaseFilesAreDownloaded()
    {
        Assert.True(UpdateRules.IsTrusted(new Uri("https://github.com/Mericsdc/ISLe8-Windows/releases/download/v0.2.0/ISLe8-0.2.0-win-x64.zip")));
        Assert.False(UpdateRules.IsTrusted(new Uri("http://github.com/Mericsdc/ISLe8-Windows/releases/download/v0.2.0/a.zip")));
        Assert.False(UpdateRules.IsTrusted(new Uri("https://github.com/someone/ISLe8-Windows/releases/download/v0.2.0/a.zip")));
        Assert.False(UpdateRules.IsTrusted(new Uri("https://example.com/Mericsdc/ISLe8-Windows/releases/download/v0.2.0/a.zip")));
    }

    [Fact]
    public void TheCheckIsDailyAndOnByDefault()
    {
        var now = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        Assert.True(new AppSettings().CheckForUpdates);
        Assert.True(UpdateRules.IsDue(null, now));
        Assert.False(UpdateRules.IsDue(now.AddHours(-23), now));
        Assert.True(UpdateRules.IsDue(now.AddDays(-1), now));
        // A clock set back doesn't silence it for good.
        Assert.True(UpdateRules.IsDue(now.AddDays(3), now));
    }
}
