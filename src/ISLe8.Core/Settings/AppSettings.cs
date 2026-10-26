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
    /// <summary>Wherever you last clicked or brought a window forward.</summary>
    Active,
}

/// <summary>How much Home shows; the Mac's names. Shown as Wide, Compact and Minimal ("Pano" is a tab).</summary>
[JsonConverter(typeof(LenientEnumConverter<HomeStyle>))]
public enum HomeStyle
{
    /// <summary>"Geniş": the full dashboard.</summary>
    Dashboard,
    Compact,
    /// <summary>A single strip at Home; other tabs grow the island.</summary>
    Minimal,
}

/// <summary>How the island opens: on its own when the pointer rests on it, or only when clicked.</summary>
[JsonConverter(typeof(LenientEnumConverter<OpenMode>))]
public enum OpenMode
{
    Hover,
    /// <summary>Resting on the island only swells it a little (peek); a click opens it.</summary>
    Click,
}

public sealed class AppSettings
{
    /// <summary>2: delays in seconds, as on the Mac. 1 kept them in milliseconds.</summary>
    public const int CurrentVersion = 2;

    public static IReadOnlyList<string> Languages { get; } = ["system", "tr-TR", "en-US"];

    public int Version { get; set; } = CurrentVersion;
    public string ThemeId { get; set; } = "ember";
    public List<IslandTheme> CustomThemes { get; set; } = [];

    /// <summary>"system", "tr-TR" or "en-US".</summary>
    public string Language { get; set; } = "system";

    public OpenMode OpenMode { get; set; } = OpenMode.Hover;

    /// <summary>
    /// Seconds the pointer rests on the pill before it opens (hover mode). 0 opens at once; the island
    /// still waits <see cref="Island.IslandModel.MinimumOpenDelay"/> so a pointer passing over doesn't open it.
    /// </summary>
    public double OpenDelay { get; set; }

    /// <summary>Seconds after the pointer leaves the open island before it closes.</summary>
    public double CloseDelay { get; set; } = 0.35;

    public double AnimationSpeed { get; set; } = 1;

    public HomeStyle HomeStyle { get; set; } = HomeStyle.Dashboard;

    /// <summary>The open island's width in the wide layout, 680–980 DIPs (Settings › Appearance).</summary>
    public double ExpandedWidth { get; set; } = 820;

    /// <summary>The dock's tabs by id (<see cref="Island.IslandTabs.AllIds"/>), left to right.</summary>
    public List<string> TabOrder { get; set; } = [.. Island.IslandTabs.AllIds];

    public List<string> HiddenTabs { get; set; } = [];
    public bool SwipeToChangeTabs { get; set; } = true;

    /// <summary>Kept out of screenshots, recordings and shared screens.</summary>
    public bool HideFromScreenCapture { get; set; }

    public bool HideInFullscreen { get; set; } = true;
    public bool AudioBars { get; set; } = true;
    public MonitorChoice Monitor { get; set; } = MonitorChoice.Primary;

    /// <summary>A Win32 device name such as <c>\\.\DISPLAY2</c>.</summary>
    public string? MonitorDevice { get; set; }

    /// <summary>Version 1's open delay in milliseconds. Read once by <see cref="Normalize"/>, then dropped.</summary>
    public int? HoverDelayMs { get; set; }

    /// <summary>Version 1's close delay in milliseconds. Read once by <see cref="Normalize"/>, then dropped.</summary>
    public int? CloseDelayMs { get; set; }

    [JsonIgnore]
    public IslandTheme Theme =>
        CustomThemes.FirstOrDefault(theme => theme.Id == ThemeId)
        ?? IslandTheme.Presets.FirstOrDefault(theme => theme.Id == ThemeId)
        ?? IslandTheme.NotchBlack;

    /// <summary>Brings a hand-edited or older file up to date and back into range.</summary>
    public void Normalize()
    {
        Migrate();
        // The ranges of the Mac's settings sliders; animation speed as far as its model allows.
        OpenDelay = Clamp(OpenDelay, 0, 1, 0);
        CloseDelay = Clamp(CloseDelay, 0, 1.5, 0.35);
        AnimationSpeed = Clamp(AnimationSpeed, 0.5, 2, 1);
        ExpandedWidth = Clamp(ExpandedWidth, 680, 980, 820);
        // Known tabs in the saved order, then any the file doesn't mention; at least one shown.
        var known = Island.IslandTabs.AllIds;
        TabOrder = (TabOrder ?? []).Where(known.Contains).Distinct().Concat(known).Distinct().ToList();
        HiddenTabs = (HiddenTabs ?? []).Where(known.Contains).Distinct().ToList();
        if (HiddenTabs.Count >= known.Count) HiddenTabs.Clear();
        if (!Languages.Contains(Language)) Language = "system";
        CustomThemes = (CustomThemes ?? []).Where(theme => theme is not null).Select(theme => theme.Normalized()).ToList();
        if (Monitor == MonitorChoice.Named && string.IsNullOrWhiteSpace(MonitorDevice)) Monitor = MonitorChoice.Primary;
    }

    /// <summary>
    /// Version 1 wrote 150 and 250 ms because those were its defaults; they become the Mac's 0 and
    /// 0.35 s. A delay someone chose by hand is kept.
    /// </summary>
    private void Migrate()
    {
        if (HoverDelayMs is { } open) OpenDelay = open == 150 ? 0 : open / 1000.0;
        if (CloseDelayMs is { } close) CloseDelay = close == 250 ? 0.35 : close / 1000.0;
        HoverDelayMs = null;
        CloseDelayMs = null;
        Version = CurrentVersion;
    }

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
