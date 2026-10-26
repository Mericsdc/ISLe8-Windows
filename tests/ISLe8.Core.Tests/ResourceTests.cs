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

    private static string[] Placeholders(string text) =>
        Placeholder().Matches(text).Select(match => match.Value).Order().ToArray();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex("""L\.(?:Get|Format)\("([A-Za-z_]+)"[,)]""")]
    private static partial Regex KeyUse();
}
