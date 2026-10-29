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

/// <summary>
/// Where the island sits (Settings › Appearance › Island; Windows only): hanging from the top of the
/// screen as on the Mac, or standing on the taskbar at its left, centre or right, opening upwards.
/// </summary>
[JsonConverter(typeof(LenientEnumConverter<IslandPlacement>))]
public enum IslandPlacement
{
    Top,
    TaskbarLeft,
    TaskbarCenter,
    TaskbarRight,
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

    /// <summary>Home's columns by id (<see cref="Island.HomeColumns.AllIds"/>), left to right; may be empty.</summary>
    public List<string> HomeTiles { get; set; } = ["calendar", "media", "weather"];

    /// <summary>A short banner when a new song starts (Settings › Media).</summary>
    public bool ShowTrackChangeAlert { get; set; } = true;

    /// <summary>A sound when a countdown or Pomodoro phase ends (Settings › Widgets › Timer).</summary>
    public bool TimerSound { get; set; } = true;

    /// <summary>Pomodoro focus, 5–90 minutes in steps of 5.</summary>
    public int PomodoroWorkMinutes { get; set; } = 25;

    /// <summary>Pomodoro break, 1–30 minutes.</summary>
    public int PomodoroBreakMinutes { get; set; } = 5;

    /// <summary>The weather's city; empty for the one in the time zone (Settings › Widgets).</summary>
    public string WeatherCity { get; set; } = "";
    public double WeatherLatitude { get; set; }
    public double WeatherLongitude { get; set; }
    public Weather.TemperatureUnit WeatherUnit { get; set; } = Weather.TemperatureUnit.Celsius;

    /// <summary>
    /// The calendars on Home: ICS subscription links (https or webcal), read-only. Windows has no
    /// EventKit; this is how Outlook, Google and iCloud calendars get in (PORTING.md §3.6).
    /// </summary>
    public List<string> CalendarFeeds { get; set; } = [];

    /// <summary>A banner five minutes before a meeting (Settings › Widgets).</summary>
    public bool MeetingHeadsUp { get; set; } = true;

    /// <summary>Settings › Shelf: files dropped on the shelf are copied into ISLe8's folder first.</summary>
    public bool CopyFilesToShelf { get; set; }

    /// <summary>Settings › Clipboard: keep a history of what is copied.</summary>
    public bool RecordClipboard { get; set; } = true;

    /// <summary>At most this many entries, 50–1000 in steps of 50; pinned ones on top of it.</summary>
    public int ClipboardLimit { get; set; } = 200;

    /// <summary>Entries older than this many days go, 1–365; pinned ones stay.</summary>
    public int ClipboardMaxAgeDays { get; set; } = 30;

    /// <summary>Programs, by .exe name, whose copies are never recorded (password managers by default).</summary>
    public List<string> ClipboardExcludedApps { get; set; } = [.. Clipboard.ClipboardRules.DefaultExcludedApps];

    /// <summary>Settings › Notifications: other apps' notifications show on the island (PORTING.md §3.11).</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Seconds a notification banner stays up, 2–10 in steps of 0.5.</summary>
    public double NotificationDuration { get; set; } = 4;

    /// <summary>Not while Do not disturb or a focus session is on.</summary>
    public bool RespectFocus { get; set; } = true;

    /// <summary>AppUserModelIDs whose notifications never show.</summary>
    public List<string> MutedNotificationApps { get; set; } = [];

    /// <summary>Apps that recently sent notifications, newest first, for the mute list (<see cref="Notifications.NotificationRules.Remember"/>).</summary>
    public List<Notifications.NotificationApp> NotificationSeenApps { get; set; } = [];

    /// <summary>Settings › Agents: a tool's permission request is asked on the island (Approve / Deny).</summary>
    public bool AgentApprovalsOnIsland { get; set; } = true;

    /// <summary>Seconds without an answer before the question goes back to the agent, 15–300 in steps of 15.</summary>
    public int AgentApprovalTimeout { get; set; } = 90;

    /// <summary>A banner when an agent finishes its turn.</summary>
    public bool AgentDoneAlert { get; set; } = true;

    /// <summary>Settings › Agents › Usage limits: off, nothing runs at all.</summary>
    public bool UsageEnabled { get; set; } = true;

    /// <summary>Codex and Antigravity every 15 minutes instead of 5; on by default (the CLIs alone cost about 0.3 % CPU at 5).</summary>
    public bool UsageSaverMode { get; set; } = true;

    /// <summary>Vendors turned off ("codex", "claude", "gemini"), so one added later starts out on.</summary>
    public List<string> UsageDisabledVendors { get; set; } = [];

    /// <summary>Settings › System › Wireless devices: a banner with the batteries when one connects.</summary>
    public bool ShowDeviceConnectedAlert { get; set; } = true;

    /// <summary>A warning when a device's battery drops to 15 %.</summary>
    public bool ShowDeviceLowBatteryAlert { get; set; } = true;

    /// <summary>The open island's header shows the device whose battery runs out first.</summary>
    public bool ShowDevicesChip { get; set; } = true;

    /// <summary>Settings › System › HUDs: the volume in the ears when it changes.</summary>
    public bool ShowVolumeHud { get; set; } = true;

    /// <summary>The brightness in the ears, only when changed with the brightness keys.</summary>
    public bool ShowBrightnessHud { get; set; } = true;

    /// <summary>ISLe8 takes the volume keys, so Windows' own volume HUD stays away (the Mac's replaceSystemHUD, off by default).</summary>
    public bool ReplaceSystemHud { get; set; }

    /// <summary>Settings › System › Battery: plugged in, unplugged, and the battery at 20 % and 10 %.</summary>
    public bool ShowBatteryAlerts { get; set; } = true;

    /// <summary>Settings › Appearance › Header: this computer's battery, and the power going into it while it charges.</summary>
    public bool ShowBatteryChip { get; set; } = true;

    /// <summary>A green LAN chip while online by cable, and the ears when the cable goes in or out.</summary>
    public bool ShowNetworkChip { get; set; } = true;
    public bool SwipeToChangeTabs { get; set; } = true;

    /// <summary>Kept out of screenshots, recordings and shared screens.</summary>
    public bool HideFromScreenCapture { get; set; }

    /// <summary>Settings › General: a full-screen app (a game, a video, a presentation) hides the island.</summary>
    public bool HideInFullscreen { get; set; } = true;

    /// <summary>Settings › Appearance › Island: where the island sits.</summary>
    public IslandPlacement Placement { get; set; } = IslandPlacement.Top;

    /// <summary>At the top: no gap above the pill, its top corners square, like the Mac's notch.</summary>
    public bool FlushTop { get; set; }

    /// <summary>The idle pill shows the time.</summary>
    public bool ShowIdleClock { get; set; }

    /// <summary>While music plays, the wide pill shows the song's title between the artwork and the bars, scrolling if it is long.</summary>
    public bool ShowPillTitle { get; set; }

    /// <summary>
    /// Settings › General: a strip at the top of the island's screen is kept free, as the taskbar keeps
    /// its own, so maximised windows (a browser's tabs) start below the pill instead of under it.
    /// </summary>
    public bool ReserveTopStrip { get; set; } = true;
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
        PomodoroWorkMinutes = Math.Clamp((int)Math.Round(PomodoroWorkMinutes / 5.0) * 5, 5, 90);
        PomodoroBreakMinutes = Math.Clamp(PomodoroBreakMinutes, 1, 30);
        WeatherCity = (WeatherCity ?? "").Trim();
        ClipboardLimit = Math.Clamp((int)Math.Round(ClipboardLimit / 50.0) * 50, 50, 1000);
        ClipboardMaxAgeDays = Math.Clamp(ClipboardMaxAgeDays, 1, 365);
        NotificationDuration = Math.Round(Clamp(NotificationDuration, 2, 10, 4) * 2) / 2;
        AgentApprovalTimeout = Math.Clamp((int)Math.Round(AgentApprovalTimeout / 15.0) * 15, 15, 300);
        ClipboardExcludedApps = (ClipboardExcludedApps ?? []).Select(app => (app ?? "").Trim()).Where(app => app.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        MutedNotificationApps = (MutedNotificationApps ?? []).Where(app => !string.IsNullOrWhiteSpace(app))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        NotificationSeenApps = (NotificationSeenApps ?? []).Where(app => app is not null && !string.IsNullOrWhiteSpace(app.Id))
            .DistinctBy(app => app.Id, StringComparer.OrdinalIgnoreCase).Take(Notifications.NotificationRules.SeenLimit).ToList();
        CalendarFeeds = (CalendarFeeds ?? []).Select(feed => (feed ?? "").Trim())
            .Where(feed => Uri.TryCreate(feed, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "webcal")
            .Distinct(StringComparer.Ordinal).ToList();
        if (!double.IsFinite(WeatherLatitude) || !double.IsFinite(WeatherLongitude)) WeatherLatitude = WeatherLongitude = 0;
        // Known tabs in the saved order, then any the file doesn't mention; at least one shown.
        var known = Island.IslandTabs.AllIds;
        TabOrder = (TabOrder ?? []).Where(known.Contains).Distinct().Concat(known).Distinct().ToList();
        HiddenTabs = (HiddenTabs ?? []).Where(known.Contains).Distinct().ToList();
        if (HiddenTabs.Count >= known.Count) HiddenTabs.Clear();
        HomeTiles = (HomeTiles ?? []).Where(Island.HomeColumns.AllIds.Contains).Distinct().ToList();
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
