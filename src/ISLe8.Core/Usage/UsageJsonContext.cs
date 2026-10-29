using System.Text.Json.Serialization;

namespace ISLe8.Core.Usage;

/// <summary>
/// The usage cache (<c>%LOCALAPPDATA%\LRXe8\ISLe8\usage\state.json</c>): percentages, times, plan
/// names and our own codes, by vendor. Never CLI output.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, UsageSnapshot>))]
public sealed partial class UsageJsonContext : JsonSerializerContext;
