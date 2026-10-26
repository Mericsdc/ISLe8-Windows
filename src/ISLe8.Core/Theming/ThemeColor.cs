using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISLe8.Core.Theming;

/// <summary>
/// A colour kept as "#RRGGBBAA", so a theme reads well as JSON and moves between the Mac and
/// Windows versions of ISLe8 unchanged.
/// </summary>
[JsonConverter(typeof(ThemeColorConverter))]
public readonly record struct ThemeColor(byte R, byte G, byte B, byte A = 255)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
    public bool IsClear => A == 0;

    public ThemeColor WithAlpha(byte alpha) => this with { A = alpha };

    public override string ToString() => Hex;

    public static ThemeColor Parse(string text) =>
        TryParse(text, out var color) ? color : throw new FormatException($"Not a colour: {text}");

    /// <summary>"#RGB", "#RRGGBB" or "#RRGGBBAA", with or without "#".</summary>
    public static bool TryParse(string? text, out ThemeColor color)
    {
        color = default;
        if (text is null) return false;

        var digits = text.Trim();
        if (digits.StartsWith('#')) digits = digits[1..];
        if (!digits.All(char.IsAsciiHexDigit)) return false;

        digits = digits.Length switch
        {
            3 => string.Concat(digits.Select(c => new string(c, 2))) + "FF",
            6 => digits + "FF",
            8 => digits,
            _ => "",
        };
        if (digits.Length == 0) return false;

        var value = uint.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        color = new ThemeColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }
}

internal sealed class ThemeColorConverter : JsonConverter<ThemeColor>
{
    public override ThemeColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return ThemeColor.TryParse(text, out var color) ? color : throw new JsonException($"Not a colour: {text}");
    }

    public override void Write(Utf8JsonWriter writer, ThemeColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Hex);
}

/// <summary>
/// Reads enum names in any case. An unknown name (say, from a newer ISLe8) becomes the default
/// value instead of failing the whole file, as the Mac version does.
/// </summary>
public sealed class LenientEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            reader.Skip();
            return default;
        }
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        return Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value) ? value : default;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var name = value.ToString();
        writer.WriteStringValue(char.ToLowerInvariant(name[0]) + name[1..]);
    }
}
