using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ISLe8.Core.Theming;

namespace ISLe8.Core.Tests;

/// <summary>The Windows app's strings, checked here so a missing translation fails on any machine.</summary>
public partial class ResourceTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var folder = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(folder, "ISLe8.slnx")))
        {
            folder = Path.GetDirectoryName(folder) ?? throw new DirectoryNotFoundException("ISLe8.slnx not found above the test binaries");
        }
        return folder;
    }

    private static Dictionary<string, string> Strings(string language) =>
        XDocument.Load(Path.Combine(RepoRoot, "src", "ISLe8.App", "Strings", language, "Resources.resw"))
            .Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? "");

    [Fact]
    public void TurkishAndEnglishHaveTheSameKeys()
    {
        Assert.Equal(Strings("tr-TR").Keys.Order(), Strings("en-US").Keys.Order());
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("en-US")]
    public void NoStringIsEmpty(string language)
    {
        Assert.All(Strings(language), pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
    }

    [Fact]
    public void PlaceholdersMatch()
    {
        var english = Strings("en-US");
        foreach (var (key, turkish) in Strings("tr-TR"))
        {
            Assert.Equal(Placeholders(english[key]), Placeholders(turkish));
        }
    }

    [Fact]
    public void EveryPresetThemeHasAName()
    {
        foreach (var language in new[] { "tr-TR", "en-US" })
        {
            var strings = Strings(language);
            Assert.All(IslandTheme.Presets, theme => Assert.Contains("Theme_" + theme.Id.Replace('-', '_'), strings.Keys));
        }
    }

    [Fact]
    public void EveryKeyTheCodeAsksForExists()
    {
        var strings = Strings("en-US");
        var sources = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "ISLe8.App"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
        var used = sources
            .SelectMany(path => KeyUse().Matches(File.ReadAllText(path)))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(used);
        Assert.All(used, key => Assert.Contains(key, strings.Keys));
    }

    /// <summary>
    /// A text that also exists on the Mac carries <c>mac: &lt;its Turkish key&gt;</c> as its comment and must
    /// read exactly as there, in both languages. Checked where the Mac repo sits beside this one
    /// (PORTING.md); CI has no copy, so there the test has nothing to compare.
    /// </summary>
    [Fact]
    public void TextsFromTheMacReadAsOnTheMac()
    {
        var catalogPath = Path.Combine(RepoRoot, "..", "ISLe8-macOS", "ISLe8", "Resources", "Localizable.xcstrings");
        var mapped = MacKeys();
        Assert.NotEmpty(mapped);
        if (!File.Exists(catalogPath)) return;

        using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
        var macStrings = catalog.RootElement.GetProperty("strings");
        var mismatches = new List<string>();
        foreach (var (language, macLanguage) in new[] { ("tr-TR", "tr"), ("en-US", "en") })
        {
            var strings = Strings(language);
            foreach (var (key, macKey) in mapped)
            {
                if (!macStrings.TryGetProperty(macKey, out var entry))
                {
                    mismatches.Add($"{key}: \"{macKey}\" is not in the Mac catalog");
                    continue;
                }
                var expected = FromMacFormat(MacText(entry, macKey, macLanguage));
                if (strings[key] != expected) mismatches.Add($"{language} {key}: \"{strings[key]}\", Mac says \"{expected}\"");
            }
        }
        Assert.Empty(mismatches);
    }

    private static Dictionary<string, string> MacKeys() =>
        XDocument.Load(Path.Combine(RepoRoot, "src", "ISLe8.App", "Strings", "tr-TR", "Resources.resw"))
            .Root!.Elements("data")
            .Select(data => (Key: (string)data.Attribute("name")!, Comment: (string?)data.Element("comment") ?? ""))
            .Where(item => item.Comment.StartsWith("mac: ", StringComparison.Ordinal))
            .ToDictionary(item => item.Key, item => item.Comment["mac: ".Length..]);

    /// <summary>The source language is Turkish, so a Turkish text may be the key itself.</summary>
    private static string MacText(JsonElement entry, string key, string language) =>
        entry.TryGetProperty("localizations", out var localizations)
        && localizations.TryGetProperty(language, out var localization)
        && localization.TryGetProperty("stringUnit", out var unit)
            ? unit.GetProperty("value").GetString()!
            : language == "tr" ? key : throw new InvalidOperationException($"\"{key}\" has no {language} text on the Mac");

    /// <summary>"%@", "%lld" and "%1$@" become "{0}"; "%%" becomes "%".</summary>
    internal static string FromMacFormat(string text)
    {
        var next = 0;
        return MacPlaceholder().Replace(text, match => match.Value == "%%" ? "%"
            : match.Groups["position"].Success ? $"{{{int.Parse(match.Groups["position"].Value) - 1}}}"
            : $"{{{next++}}}");
    }

    [Fact]
    public void MacPlaceholdersBecomeWindowsOnes()
    {
        Assert.Equal("Ekran {0} · %{1}", FromMacFormat("Ekran %@ · %%%lld"));
        Assert.Equal("{1} / {0}", FromMacFormat("%2$@ / %1$@"));
        Assert.Equal("{0} dk", FromMacFormat("%.0f dk"));
    }

    private static string[] Placeholders(string text) =>
        Placeholder().Matches(text).Select(match => match.Value).Order().ToArray();

    [GeneratedRegex(@"%%|%(?:(?<position>\d+)\$)?(?:@|l{0,2}d|\.?\d*f)")]
    private static partial Regex MacPlaceholder();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex("""L\.(?:Get|Format)\("([A-Za-z_]+)"[,)]""")]
    private static partial Regex KeyUse();
}
