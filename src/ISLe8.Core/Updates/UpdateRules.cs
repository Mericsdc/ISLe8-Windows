using System.Text.Json;

namespace ISLe8.Core.Updates;

/// <summary>One file of a release: its name, where to download it, its size and, when GitHub gives it, its SHA-256.</summary>
public sealed record ReleaseAsset(string Name, Uri Url, long Size, string? Sha256);

/// <summary>A GitHub release: its version (from the tag, "v0.2.0"), its page and its files.</summary>
public sealed record ReleaseInfo(Version Version, string Tag, Uri Page, string Notes, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>
/// Updates from the project's GitHub releases (Windows only; the Mac version has none). The
/// release's files are named as scripts/publish.ps1 and scripts/package.ps1 make them:
/// <c>ISLe8-0.2.0-win-x64.zip</c> for the zip, <c>ISLe8-0.2.0-win-x64-package.zip</c> for the installed app.
/// </summary>
public static class UpdateRules
{
    public const string Repository = "Mericsdc/ISLe8-Windows";
    public static readonly Uri LatestRelease = new($"https://api.github.com/repos/{Repository}/releases/latest");

    /// <summary>An automatic check at most this often.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    /// <summary>The GitHub API's answer for one release; null when it isn't one, or its tag isn't a version.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;
            var tag = Text(root, "tag_name");
            if (tag is null || VersionOf(tag) is not { } version) return null;
            if (!Uri.TryCreate(Text(root, "html_url"), UriKind.Absolute, out var page)) page = new Uri($"https://github.com/{Repository}/releases/tag/{tag}");
            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in list.EnumerateArray())
                {
                    if (Text(asset, "name") is not { } name || !Uri.TryCreate(Text(asset, "browser_download_url"), UriKind.Absolute, out var url)) continue;
                    var size = asset.TryGetProperty("size", out var bytes) && bytes.TryGetInt64(out var n) ? n : 0;
                    var digest = Text(asset, "digest");
                    var sha = digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..].ToLowerInvariant() : null;
                    assets.Add(new ReleaseAsset(name, url, size, sha));
                }
            }
            return new ReleaseInfo(version, tag, page, Text(root, "body") ?? "", assets);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>"v0.2.0" or "0.2.0" as a version of three parts; null for anything else.</summary>
    public static Version? VersionOf(string tag)
    {
        var text = tag.Trim().TrimStart('v', 'V');
        if (!Version.TryParse(text, out var version)) return null;
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    /// <summary>Newer than the running ISLe8, comparing major, minor and patch only.</summary>
    public static bool IsNewer(ReleaseInfo release, Version current) =>
        release.Version > new Version(current.Major, current.Minor, Math.Max(current.Build, 0));

    /// <summary>The file this ISLe8 updates from: the installed app's package, or the zip, for its processor.</summary>
    public static ReleaseAsset? AssetFor(ReleaseInfo release, string rid, bool packaged)
    {
        var name = $"ISLe8-{release.Version.ToString(3)}-{rid}{(packaged ? "-package" : "")}.zip";
        return release.Assets.FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Only files from the project's own releases are downloaded.</summary>
    public static bool IsTrusted(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps && url.Host == "github.com"
        && url.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase);

    public static bool IsDue(DateTimeOffset? lastCheck, DateTimeOffset now) => lastCheck is not { } last || now - last >= CheckInterval || now < last;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
