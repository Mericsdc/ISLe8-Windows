using ISLe8.Core.Settings;

namespace ISLe8.Core.Notifications;

/// <summary>
/// A notification another app posted, as Windows' notification center holds it
/// (NotificationDatabase.swift, AppNotification). <paramref name="AppId"/> is the sender's
/// AppUserModelID, which opens it again.
/// </summary>
public sealed record AppNotification(uint Id, string AppId, string AppName, string Title, string Detail, DateTimeOffset Date)
{
    /// <summary>
    /// From a toast's text lines: the first is the title; the others, the Mac's subtitle and body,
    /// go on one line the way a banner shows them.
    /// </summary>
    public static AppNotification FromLines(uint id, string appId, string appName, IEnumerable<string?> lines, DateTimeOffset date)
    {
        var texts = lines.Select(line => line?.Trim() ?? "").Where(line => line.Length > 0).ToList();
        return new(id, appId, appName, texts.Count > 0 ? texts[0] : "", string.Join(" · ", texts.Skip(1)), date);
    }
}

/// <summary>An app that sent notifications, for the mute list in Settings › Notifications.</summary>
public sealed class NotificationApp
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Which notifications reach the island, and the apps Settings lists (NotificationModule.swift).</summary>
public static class NotificationRules
{
    /// <summary>Settings remembers this many apps, newest first.</summary>
    public const int SeenLimit = 30;

    /// <summary>
    /// Keeps a short list of apps that sent notifications, for muting them in Settings: a new app goes
    /// first, a known one keeps its place (and its newest name). Returns whether the list changed.
    /// </summary>
    public static bool Remember(List<NotificationApp> seen, AppNotification notification)
    {
        var known = seen.Find(app => Same(app.Id, notification.AppId));
        if (known is not null)
        {
            if (notification.AppName.Length == 0 || known.Name == notification.AppName) return false;
            known.Name = notification.AppName;
            return true;
        }
        seen.Insert(0, new NotificationApp { Id = notification.AppId, Name = notification.AppName });
        if (seen.Count > SeenLimit) seen.RemoveRange(SeenLimit, seen.Count - SeenLimit);
        return true;
    }

    public static bool IsMuted(AppSettings settings, string appId) => settings.MutedNotificationApps.Exists(id => Same(id, appId));

    /// <summary>Settings › Notifications › Apps: an app's switch.</summary>
    public static void SetMuted(AppSettings settings, string appId, bool muted)
    {
        settings.MutedNotificationApps.RemoveAll(id => Same(id, appId));
        if (muted) settings.MutedNotificationApps.Add(appId);
    }

    /// <summary>Whether a notification gets a banner: the mirror is on, its app isn't muted and no Focus mode says otherwise.</summary>
    public static bool Shows(AppSettings settings, AppNotification notification, bool focusActive) =>
        settings.NotificationsEnabled && !IsMuted(settings, notification.AppId) && !(settings.RespectFocus && focusActive);

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
