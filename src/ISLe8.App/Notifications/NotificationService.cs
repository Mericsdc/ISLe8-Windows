using ISLe8.Core.Diagnostics;
using ISLe8.Core.Notifications;
using ISLe8.Core.Settings;
using ISLe8.Shell;
using Microsoft.UI.Dispatching;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace ISLe8.Notifications;

/// <summary>What Settings › Notifications says about access (NotificationModule.swift, Access).</summary>
internal enum NotificationAccess
{
    /// <summary>The mirror is off in Settings.</summary>
    Off,
    /// <summary>ISLe8 runs from the zip: Windows lets only packaged apps read notifications.</summary>
    NeedsPackage,
    /// <summary>Windows has not asked yet.</summary>
    NotAsked,
    Denied,
    Reading,
    /// <summary>Allowed, but Windows would not report new notifications.</summary>
    Unavailable,
}

/// <summary>
/// Mirrors other apps' notifications on the island (NotificationModule.swift). The Mac reads
/// Notification Center's database; Windows has an API for it, <see cref="UserNotificationListener"/>,
/// which needs the package's identity and the user's permission once. Its event says when one arrives:
/// nothing runs in between. Gmail and other sites arrive through the browser's notifications.
/// </summary>
internal sealed class NotificationService : IDisposable
{
    private readonly AppSettings _settings;
    private readonly DispatcherQueue _queue;
    private readonly Action _save;
    /// <summary>Each app's logo, read once.</summary>
    private readonly Dictionary<string, RandomAccessStreamReference?> _logos = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _logosGate = new();
    private UserNotificationListener? _listener;
    private bool _listening;

    public NotificationService(AppSettings settings, DispatcherQueue queue, Action save)
    {
        _settings = settings;
        _queue = queue;
        _save = save;
    }

    public NotificationAccess Access { get; private set; } = NotificationAccess.Off;

    /// <summary>A notification that gets a banner, on the UI thread.</summary>
    public event Action<AppNotification>? Arrived;

    /// <summary><see cref="Access"/> or the list of apps that sent notifications changed.</summary>
    public event Action? Changed;

    /// <summary>At start and whenever Settings › Notifications turns the mirror on or off.</summary>
    public void ApplySettings()
    {
        if (!_settings.NotificationsEnabled)
        {
            StopListening();
            SetAccess(NotificationAccess.Off);
            return;
        }
        if (!AppPackage.IsInstalled)
        {
            SetAccess(NotificationAccess.NeedsPackage);
            return;
        }
        if (_listening) return;
        try
        {
            _listener ??= UserNotificationListener.Current;
            switch (_listener.GetAccessStatus())
            {
                case UserNotificationListenerAccessStatus.Allowed:
                    Listen();
                    break;
                case UserNotificationListenerAccessStatus.Denied:
                    SetAccess(NotificationAccess.Denied);
                    break;
                default:
                    SetAccess(NotificationAccess.NotAsked);
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "notifications");
            SetAccess(NotificationAccess.Unavailable);
        }
    }

    /// <summary>
    /// Windows asks once, on the UI thread. After a no, only Windows' Settings change the answer, so
    /// they open instead.
    /// </summary>
    public async Task RequestAccessAsync()
    {
        if (!AppPackage.IsInstalled) return;
        try
        {
            _listener ??= UserNotificationListener.Current;
            if (_listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Denied)
            {
                await OpenWindowsSettingsAsync();
                return;
            }
            var status = await _listener.RequestAccessAsync();
            Log.Info($"notification access: {status}");
        }
        catch (Exception e)
        {
            Log.Error(e, "notification access");
        }
        ApplySettings();
    }

    /// <summary>Windows' Settings › Privacy › Notifications, where access is turned on again.</summary>
    public static async Task OpenWindowsSettingsAsync() =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-notifications"));

    /// <summary>The app's logo for a banner or the Settings list; null when Windows has none.</summary>
    public RandomAccessStreamReference? LogoFor(string appId)
    {
        lock (_logosGate)
        {
            if (_logos.TryGetValue(appId, out var known)) return known;
        }
        RandomAccessStreamReference? logo = null;
        try
        {
            logo = Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(appId)?.DisplayInfo.GetLogo(AppIcon.LogoRequest);
        }
        catch (Exception e) when (e is ArgumentException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
        }
        lock (_logosGate) _logos[appId] = logo;
        return logo;
    }

    public void Dispose() => StopListening();

    private void Listen()
    {
        try
        {
            _listener!.NotificationChanged += OnNotificationChanged;
            _listening = true;
            SetAccess(NotificationAccess.Reading);
            Log.Info("notifications: listening");
        }
        catch (Exception e)
        {
            Log.Error(e, "notifications");
            SetAccess(NotificationAccess.Unavailable);
        }
    }

    private void StopListening()
    {
        if (!_listening || _listener is null) return;
        try
        {
            _listener.NotificationChanged -= OnNotificationChanged;
        }
        catch (Exception e)
        {
            Log.Error(e, "notifications");
        }
        _listening = false;
    }

    /// <summary>On a thread of Windows' choosing: reads the new notification, then hands it to the UI thread.</summary>
    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind != UserNotificationChangedKind.Added) return;
        try
        {
            if (sender.GetNotification(args.UserNotificationId) is not { } received || Read(received) is not { } notification) return;
            _queue.TryEnqueue(() => Deliver(notification));
        }
        catch (Exception e)
        {
            Log.Error(e, "notification");
        }
    }

    /// <summary>The sender and the toast's lines; ISLe8's own and empty ones are left out.</summary>
    private AppNotification? Read(UserNotification received)
    {
        string appId = "", name = "";
        try
        {
            appId = received.AppInfo.AppUserModelId ?? "";
            name = received.AppInfo.DisplayInfo.DisplayName ?? "";
            var logo = received.AppInfo.DisplayInfo.GetLogo(AppIcon.LogoRequest);
            lock (_logosGate) _logos.TryAdd(appId, logo);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException)
        {
        }
        if (appId.Length == 0 || appId.Contains(AppPackage.Name, StringComparison.OrdinalIgnoreCase)) return null;
        // Today's toasts use the generic binding; older apps still send the Windows 8 templates (ToastText02 and the like).
        var visual = received.Notification?.Visual;
        var binding = visual?.GetBinding(KnownNotificationBindings.ToastGeneric) ?? visual?.Bindings.FirstOrDefault();
        var lines = binding?.GetTextElements().Select(text => text.Text) ?? [];
        var notification = AppNotification.FromLines(received.Id, appId, name.Length > 0 ? name : appId, lines, received.CreationTime);
        return notification.Title.Length == 0 && notification.Detail.Length == 0 ? null : notification;
    }

    private void Deliver(AppNotification notification)
    {
        if (!_listening || !_settings.NotificationsEnabled) return;
        if (NotificationRules.Remember(_settings.NotificationSeenApps, notification))
        {
            _save();
            Changed?.Invoke();
        }
        if (NotificationRules.Shows(_settings, notification, FocusStatus.IsActive())) Arrived?.Invoke(notification);
    }

    private void SetAccess(NotificationAccess access)
    {
        if (access == Access) return;
        Access = access;
        Changed?.Invoke();
    }
}
