using System.Text.Json;
using System.Text.Json.Serialization;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Theming;

[JsonConverter(typeof(LenientEnumConverter<IslandMaterial>))]
public enum IslandMaterial
{
    /// <summary>Pure black.</summary>
    Black,
    Solid,
    /// <summary><see cref="IslandTheme.Background"/> at the top fading into <see cref="IslandTheme.BackgroundBottom"/>.</summary>
    Gradient,
    /// <summary>Acrylic on Windows (Liquid Glass on the Mac). Until W5 it draws as a translucent solid.</summary>
    Glass,
    /// <summary>A wash of the playing song's artwork colour. Until W5 it draws like <see cref="Gradient"/>.</summary>
    Artwork,
}

[JsonConverter(typeof(LenientEnumConverter<IslandTypeface>))]
public enum IslandTypeface
{
    System,
    Rounded,
    Monospaced,
    Serif,
}

/// <summary>
/// How the open island looks. The JSON shape matches ISLe8 for Mac, so a theme exported on one
/// opens on the other. Missing fields take Notch Black's values.
/// </summary>
// Setters, not init: the JSON source generator resets missing init-only properties to default(T).
public sealed record IslandTheme
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Notch Black";
    public IslandMaterial Material { get; set; } = IslandMaterial.Black;
    public ThemeColor Background { get; set; } = new(0, 0, 0);

    /// <summary>The bottom of the gradient; missing means the same as <see cref="Background"/>.</summary>
    public ThemeColor? BackgroundBottom { get; set; }

    /// <summary>Cards and tiles.</summary>
    public ThemeColor Surface { get; set; } = new(255, 255, 255, 0x0F);

    /// <summary>Hairline around cards; fully transparent for none.</summary>
    public ThemeColor Border { get; set; } = new(255, 255, 255, 0);

    public ThemeColor Accent { get; set; } = new(0x0A, 0x84, 0xFF);
    public double CornerRadius { get; set; } = 16;
    public IslandTypeface Typeface { get; set; } = IslandTypeface.System;

    [JsonIgnore]
    public ThemeColor GradientBottom => BackgroundBottom ?? Background;

    [JsonIgnore]
    public bool IsPreset => Presets.Any(preset => preset.Id == Id);

    /// <summary>Clamps values a hand-edited file may have out of range.</summary>
    public IslandTheme Normalized() => this with
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString() : Id,
        Name = string.IsNullOrWhiteSpace(Name) ? NotchBlack.Name : Name.Trim(),
        CornerRadius = double.IsFinite(CornerRadius) ? Math.Clamp(CornerRadius, 0, 26) : NotchBlack.CornerRadius,
    };

    public string ToJson() => JsonSerializer.Serialize(this, IsleJsonContext.Default.IslandTheme);

    public static IslandTheme FromJson(string json) =>
        (JsonSerializer.Deserialize(json, IsleJsonContext.Default.IslandTheme) ?? throw new JsonException("Empty theme"))
        .Normalized();

    // MARK: Built-in themes, the same seven as on the Mac.

    public static IslandTheme NotchBlack { get; } = new() { Id = "notch-black", Name = "Notch Black" };

    public static IReadOnlyList<IslandTheme> Presets { get; } =
    [
        NotchBlack,
        // After componest's agent card: graphite cards with a hairline, a soft blue.
        new()
        {
            Id = "graphite", Name = "Graphite", Material = IslandMaterial.Solid,
            Background = ThemeColor.Parse("#0E0E10"), Surface = ThemeColor.Parse("#18181B"),
            Border = ThemeColor.Parse("#FFFFFF17"), Accent = ThemeColor.Parse("#7AA7FF"), CornerRadius = 14,
        },
        new()
        {
            Id = "glass", Name = "Glass", Material = IslandMaterial.Glass,
            Background = ThemeColor.Parse("#000000"), Surface = ThemeColor.Parse("#FFFFFF14"),
            Border = ThemeColor.Parse("#FFFFFF1F"), Accent = ThemeColor.Parse("#64D2FF"), CornerRadius = 18,
        },
        new()
        {
            Id = "artwork", Name = "Kapak rengi", Material = IslandMaterial.Artwork,
            Background = ThemeColor.Parse("#000000"), Surface = ThemeColor.Parse("#FFFFFF12"),
            Accent = ThemeColor.Parse("#FF9F0A"),
        },
        // LRXe8: monochrome, sharp corners, technical type.
        new()
        {
            Id = "lrx-mono", Name = "LRX Mono", Material = IslandMaterial.Black,
            Background = ThemeColor.Parse("#000000"), Surface = ThemeColor.Parse("#0B0B0C"),
            Border = ThemeColor.Parse("#FFFFFF2B"), Accent = ThemeColor.Parse("#FFFFFF"),
            CornerRadius = 6, Typeface = IslandTypeface.Monospaced,
        },
        new()
        {
            Id = "midnight", Name = "Midnight", Material = IslandMaterial.Gradient,
            Background = ThemeColor.Parse("#0B1022"), BackgroundBottom = ThemeColor.Parse("#16204A"),
            Surface = ThemeColor.Parse("#FFFFFF0D"), Border = ThemeColor.Parse("#8FA6FF24"),
            Accent = ThemeColor.Parse("#8FA6FF"), CornerRadius = 18, Typeface = IslandTypeface.Rounded,
        },
        new()
        {
            Id = "ember", Name = "Ember", Material = IslandMaterial.Gradient,
            Background = ThemeColor.Parse("#110A08"), BackgroundBottom = ThemeColor.Parse("#2E140B"),
            Surface = ThemeColor.Parse("#FFFFFF0D"), Border = ThemeColor.Parse("#FF8A4C26"),
            Accent = ThemeColor.Parse("#FF8A4C"),
        },
    ];
}
