using System.Text.Json;
using System.Text.Json.Serialization;
using ISLe8.Core.Theming;

namespace ISLe8.Core.Settings;

/// <summary>Source-generated, so JSON needs no reflection and works under Native AOT.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(IslandTheme))]
internal sealed partial class IsleJsonContext : JsonSerializerContext;
