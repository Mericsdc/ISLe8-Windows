using System.Text.Json;
using ISLe8.Core.Theming;

namespace ISLe8.Core.Tests;

public class ThemeTests
{
    [Theory]
    [InlineData("#FF8A4C", 0xFF, 0x8A, 0x4C, 0xFF)]
    [InlineData("ff8a4c26", 0xFF, 0x8A, 0x4C, 0x26)]
    [InlineData("#abc", 0xAA, 0xBB, 0xCC, 0xFF)]
    [InlineData("  #000000FF ", 0, 0, 0, 0xFF)]
    public void ParsesColours(string text, byte r, byte g, byte b, byte a)
    {
        Assert.Equal(new ThemeColor(r, g, b, a), ThemeColor.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void RejectsNonColours(string text)
    {
        Assert.False(ThemeColor.TryParse(text, out _));
    }

    [Fact]
    public void HasTheSameSevenPresetsAsTheMac()
    {
        Assert.Equal(
            ["notch-black", "graphite", "glass", "artwork", "lrx-mono", "midnight", "ember"],
            IslandTheme.Presets.Select(theme => theme.Id));
    }

    [Fact]
    public void EmberMatchesTheMac()
    {
        var ember = IslandTheme.Presets.Single(theme => theme.Id == "ember");

        Assert.Equal(IslandMaterial.Gradient, ember.Material);
        Assert.Equal("#110A08FF", ember.Background.Hex);
        Assert.Equal("#2E140BFF", ember.GradientBottom.Hex);
        Assert.Equal("#FF8A4CFF", ember.Accent.Hex);
        Assert.Equal("#FF8A4C26", ember.Border.Hex);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        foreach (var preset in IslandTheme.Presets)
        {
            Assert.Equal(preset, IslandTheme.FromJson(preset.ToJson()));
        }
    }

    [Fact]
    public void ReadsAThemeExportedOnTheMac()
    {
        // The Mac writes every field, including backgroundBottom, in this shape.
        const string json = """
            {
              "accent" : "#FF8A4CFF",
              "background" : "#110A08FF",
              "backgroundBottom" : "#2E140BFF",
              "border" : "#FF8A4C26",
              "cornerRadius" : 16,
              "id" : "8C3F2A9E-1111-2222-3333-444455556666",
              "material" : "gradient",
              "name" : "My Ember",
              "surface" : "#FFFFFF0D",
              "typeface" : "rounded"
            }
            """;

        var theme = IslandTheme.FromJson(json);

        Assert.Equal("My Ember", theme.Name);
        Assert.Equal(IslandMaterial.Gradient, theme.Material);
        Assert.Equal(IslandTypeface.Rounded, theme.Typeface);
        Assert.Equal("#2E140BFF", theme.GradientBottom.Hex);
    }

    [Fact]
    public void MissingFieldsTakeNotchBlacksValues()
    {
        var theme = IslandTheme.FromJson("""{ "name": "Bare", "background": "#202020" }""");

        Assert.Equal(IslandTheme.NotchBlack.Accent, theme.Accent);
        Assert.Equal(IslandTheme.NotchBlack.Material, theme.Material);
        Assert.Equal(theme.Background, theme.GradientBottom);
        Assert.False(string.IsNullOrEmpty(theme.Id));
    }

    [Fact]
    public void UnknownEnumValuesFallBackInsteadOfFailing()
    {
        var theme = IslandTheme.FromJson("""{ "material": "hologram", "typeface": { "x": 1 } }""");

        Assert.Equal(IslandMaterial.Black, theme.Material);
        Assert.Equal(IslandTypeface.System, theme.Typeface);
    }

    [Fact]
    public void ClampsTheCornerRadius()
    {
        Assert.Equal(26, IslandTheme.FromJson("""{ "cornerRadius": 400 }""").CornerRadius);
        Assert.Equal(0, IslandTheme.FromJson("""{ "cornerRadius": -3 }""").CornerRadius);
    }

    [Fact]
    public void ABadColourFailsTheImport()
    {
        Assert.Throws<JsonException>(() => IslandTheme.FromJson("""{ "accent": "orange" }"""));
    }

    [Fact]
    public void WritesEnumsInLowerCamelCaseLikeTheMac()
    {
        var json = IslandTheme.Presets.Single(theme => theme.Id == "lrx-mono").ToJson();

        Assert.Contains("\"material\": \"black\"", json);
        Assert.Contains("\"typeface\": \"monospaced\"", json);
    }
}
