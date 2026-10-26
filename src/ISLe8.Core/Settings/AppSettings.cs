using System.Text.Json.Serialization;
using ISLe8.Core.Theming;

namespace ISLe8.Core.Settings;

[JsonConverter(typeof(LenientEnumConverter<MonitorChoice>))]
public enum MonitorChoice
{
    /// <summary>Windows' main display.</summary>
    Primary,
    /// <summary>The display in <see cref="AppSettings.MonitorDevice"/>; the main one if it is unplugged.</summary>
    Named,
}

public sealed class AppSettings
{
    public static IReadOnlyList<string> Languages { get; } = ["system", "tr-TR", "en-US"];

    public int Version { get; set; } = 1;
    public string ThemeId { get; set; } = "ember";
    public List<IslandTheme> CustomThemes { get; set; } = [];

    /// <summary>"system", "tr-TR" or "en-US".</summary>
    public string Language { get; set; } = "system";

    /// <summary>
    /// The pointer rests this long on the pill before it opens. On Windows the top of the screen is
    /// also where title bars and browser tabs are, so it is not 0 as on the Mac.
    /// </summary>
    public int HoverDelayMs { get; set; } = 150;

    public int CloseDelayMs { get; set; } = 250;
    public double AnimationSpeed { get; set; } = 1;
    public bool HideInFullscreen { get; set; } = true;
    public bool AudioBars { get; set; } = true;
    public MonitorChoice Monitor { get; set; } = MonitorChoice.Primary;

    /// <summary>A Win32 device name such as <c>\\.\DISPLAY2</c>.</summary>
    public string? MonitorDevice { get; set; }

    [JsonIgnore]
    public IslandTheme Theme =>
        CustomThemes.FirstOrDefault(theme => theme.Id == ThemeId)
        ?? IslandTheme.Presets.FirstOrDefault(theme => theme.Id == ThemeId)
        ?? IslandTheme.NotchBlack;

    /// <summary>Brings a hand-edited or older file back into range.</summary>
    public void Normalize()
    {
        HoverDelayMs = Math.Clamp(HoverDelayMs, 0, 2000);
        CloseDelayMs = Math.Clamp(CloseDelayMs, 0, 3000);
        AnimationSpeed = double.IsFinite(AnimationSpeed) ? Math.Clamp(AnimationSpeed, 0.25, 4) : 1;
        if (!Languages.Contains(Language)) Language = "system";
        CustomThemes = (CustomThemes ?? []).Where(theme => theme is not null).Select(theme => theme.Normalized()).ToList();
        if (Monitor == MonitorChoice.Named && string.IsNullOrWhiteSpace(MonitorDevice)) Monitor = MonitorChoice.Primary;
    }
}
