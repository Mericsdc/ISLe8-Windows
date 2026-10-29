using ISLe8.Core.Timer;

namespace ISLe8.Core.Island;

/// <summary>
/// A short-lived banner on the island (IslandModel.swift, IslandAlert). Each kind arrives with its module.
/// </summary>
public abstract record IslandAlert;

/// <summary>A new song started playing.</summary>
public sealed record NowPlayingAlert : IslandAlert;

/// <summary>A countdown or Pomodoro phase ended.</summary>
public sealed record TimerAlert(TimerEvent Event) : IslandAlert;

/// <summary>A meeting starts in five minutes.</summary>
public sealed record MeetingAlert(Calendar.CalendarEntry Entry) : IslandAlert;

/// <summary>Another app's notification (PORTING.md §3.11). Its time comes from Settings › Notifications.</summary>
public sealed record NotificationAlert(Notifications.AppNotification Notification) : IslandAlert;

/// <summary>A wireless device connected, or its battery ran low (PORTING.md §3.14).</summary>
public sealed record DeviceAlert(Devices.WirelessDevice Device, bool IsLow) : IslandAlert;

/// <summary>An agent asks to run a tool: Approve / Deny / Ask in Terminal (PORTING.md §3.12).</summary>
public sealed record ApprovalAlert(Agents.AgentApproval Approval) : IslandAlert;

/// <summary>An agent finished its turn.</summary>
public sealed record AgentDoneAlert(Agents.AgentSession Session) : IslandAlert;

/// <summary>A usage limit crossed 90 % (PORTING.md §3.13).</summary>
public sealed record UsageAlert(Usage.UsageSnapshot Snapshot, Usage.UsageWindow Window) : IslandAlert;

/// <summary>Volume or brightness in the ears (PORTING.md §3.2).</summary>
public sealed record HudAlert(Status.HudValue Value) : IslandAlert;

/// <summary>This computer was plugged in or out (status ears), or its battery ran low (a banner) (PORTING.md §3.3).</summary>
public sealed record BatteryAlert(Status.BatteryEvent Event) : IslandAlert;

/// <summary>
/// An Ethernet cable plugged in or taken out: status ears. <paramref name="Link"/> is the connection
/// after the change, so a cable taken out says Wi-Fi or Offline (PORTING.md §3.20).
/// </summary>
public sealed record WiredAlert(bool Connected, Status.NetworkLink Link) : IslandAlert;

public static class IslandAlerts
{
    /// <summary>Charging, unplugged, or a cable in or out: the banners that open sideways (IslandModel.swift, isStatusAlert).</summary>
    public static bool IsStatus(this IslandAlert? alert) => alert is BatteryAlert { Event.IsStatus: true } or WiredAlert;

    /// <summary>Drawn in the pill's row, either side of its middle, instead of as a banner below it.</summary>
    public static bool IsEars(this IslandAlert? alert) => alert is HudAlert || alert.IsStatus();
}

/// <summary>How long each banner stays, from the Mac (AppDelegate.swift; PORTING.md §2).</summary>
public static class AlertDurations
{
    public static readonly TimeSpan NowPlaying = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan Battery = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Wired = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Hud = TimeSpan.FromSeconds(1.5);
    public static readonly TimeSpan Timer = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan Meeting = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Approval = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan AgentDone = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan UsageLimit = TimeSpan.FromSeconds(6);
}
