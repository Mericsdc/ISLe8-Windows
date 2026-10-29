using ISLe8.Core.Timer;

namespace ISLe8.Core.Island;

/// <summary>
/// A short-lived banner on the island (IslandModel.swift, IslandAlert). Each kind arrives with its
/// module: the song now, battery and timer later in W1, notifications and agents after that.
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

/// <summary>How long each banner stays, from the Mac (AppDelegate.swift; PORTING.md §2).</summary>
public static class AlertDurations
{
    public static readonly TimeSpan NowPlaying = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan Battery = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Timer = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan Meeting = TimeSpan.FromSeconds(10);
}
