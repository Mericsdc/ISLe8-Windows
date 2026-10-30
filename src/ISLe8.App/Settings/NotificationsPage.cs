using ISLe8.Core.Notifications;
using ISLe8.Localization;
using ISLe8.Notifications;
using ISLe8.Shell;
using ISLe8.UI;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Settings;

/// <summary>
/// Settings › Notifications, after the Mac's (SettingsView.swift, NotificationSettings). Windows asks
/// for permission where the Mac needs Full Disk Access. The Mac's "when" picker chooses between the
/// desktop and its lock-screen island; Windows has no lock-screen island, so it isn't here.
/// </summary>
internal static class NotificationsPage
{
    public static FrameworkElement Build(SettingsContext context)
    {
        var settings = context.Settings;
        var palette = context.Palette;
        var service = context.Island.Notifications;
        var page = new StackPanel { MaxWidth = 860 };
        page.Children.Add(SettingsUi.PageTitle(L.Get("Settings_Notifications")));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Notifications_Show"), SettingsUi.Switch(settings.NotificationsEnabled, on =>
        {
            settings.NotificationsEnabled = on;
            context.Save();
            service.ApplySettings();
        })));

        var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var allow = new Button { Content = L.Get("Notifications_Allow") };
        allow.Click += async (_, _) => await service.RequestAccessAsync();
        var windows = new Button { Content = L.Get("Notifications_WindowsSettings") };
        windows.Click += async (_, _) => await NotificationService.OpenWindowsSettingsAsync();
        var retry = new Button { Content = L.Get("Notifications_Retry") };
        retry.Click += (_, _) => service.ApplySettings();
        var access = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { status, allow, windows, retry } };
        page.Children.Add(SettingsUi.Card(palette, L.Get("Notifications_Access"), access, L.Get("Notifications_AccessNote")));
        if (!AppPackage.IsInstalled) page.Children.Add(MoveCard(context));

        page.Children.Add(SettingsUi.Gap());
        page.Children.Add(SettingsUi.Card(palette, L.Get("Notifications_Focus"), SettingsUi.Switch(settings.RespectFocus, on =>
        {
            settings.RespectFocus = on;
            context.Save();
        }), L.Get("Notifications_FocusNote")));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Notifications_Duration"), SettingsUi.SliderRow(
            settings.NotificationDuration, 2, 10, 0.5,
            seconds => L.Format("Settings_Seconds", seconds.ToString("0.0", L.Culture)),
            seconds =>
            {
                settings.NotificationDuration = seconds;
                context.Save();
            }, standard: AppearancePage.Standard.NotificationDuration)));

        page.Children.Add(SettingsUi.SectionHeader(L.Get("Notifications_Apps")));
        var apps = new StackPanel();
        page.Children.Add(apps);

        void Refresh()
        {
            var state = service.Access;
            status.Text = L.Get(state switch
            {
                NotificationAccess.Off => "Notifications_Off",
                NotificationAccess.NeedsPackage => "Notifications_NeedsPackage",
                NotificationAccess.NotAsked => "Notifications_NotAsked",
                NotificationAccess.Denied => "Notifications_Denied",
                NotificationAccess.Reading => "Notifications_Reading",
                _ => "Notifications_Unavailable",
            });
            status.Foreground = state is NotificationAccess.Reading or NotificationAccess.Off
                ? palette.SecondaryText
                : new SolidColorBrush(Colors.DarkOrange);
            allow.Visibility = state == NotificationAccess.NotAsked ? Visibility.Visible : Visibility.Collapsed;
            windows.Visibility = state == NotificationAccess.Denied ? Visibility.Visible : Visibility.Collapsed;
            retry.Visibility = state is NotificationAccess.Denied or NotificationAccess.Unavailable ? Visibility.Visible : Visibility.Collapsed;
            FillApps(context, apps);
        }
        Refresh();
        // Windows' answer, or a new app, can come while the page is open.
        service.Changed += Refresh;
        page.Unloaded += (_, _) => service.Changed -= Refresh;
        return page;
    }

    /// <summary>One switch per app that sent notifications: on shows them, off mutes it.</summary>
    private static void FillApps(SettingsContext context, StackPanel apps)
    {
        var settings = context.Settings;
        apps.Children.Clear();
        if (settings.NotificationSeenApps.Count == 0)
        {
            apps.Children.Add(SettingsUi.Description(context.Palette, L.Get("Notifications_AppsEmpty")));
            return;
        }
        foreach (var app in settings.NotificationSeenApps.ToList())
        {
            var icon = new AppIcon(16, context.Palette.CardStroke, context.Palette.SecondaryText);
            icon.Show(context.Island.Notifications.LogoFor(app.Id));
            var name = new TextBlock { Text = app.Name is { Length: > 0 } ? app.Name : app.Id, VerticalAlignment = VerticalAlignment.Center };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { icon.Root, name } };
            apps.Children.Add(SettingsUi.Card(context.Palette, heading, SettingsUi.Switch(!NotificationRules.IsMuted(settings, app.Id), show =>
            {
                NotificationRules.SetMuted(settings, app.Id, !show);
                context.Save();
            })));
        }
    }

    /// <summary>
    /// The zip's ISLe8 can't read notifications; this card moves it to the installed one (Windows only):
    /// Developer Mode first, then the package from the latest release (<see cref="Updater.MoveToPackageAsync"/>).
    /// </summary>
    private static Border MoveCard(SettingsContext context)
    {
        var updater = context.Island.Updater;
        var state = SettingsUi.Description(context.Palette, "");
        var developer = new Button { Content = L.Get("Notifications_DeveloperMode") };
        developer.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:developers"));
        var move = new Button { Content = L.Get("Notifications_MoveToPackage"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        void Refresh()
        {
            var busy = updater.State is Shell.UpdateState.Checking or Shell.UpdateState.Downloading or Shell.UpdateState.Installing;
            var ready = Updater.DeveloperModeOn;
            developer.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
            move.IsEnabled = ready && !busy;
            state.Text = updater.State switch
            {
                Shell.UpdateState.Downloading => L.Format("Update_Downloading", (int)(updater.Progress * 100)),
                Shell.UpdateState.Installing or Shell.UpdateState.Checking => L.Get("Notifications_Moving"),
                Shell.UpdateState.Failed when updater.Problem is { } problem => problem,
                _ => L.Get(ready ? "Notifications_MoveReady" : "Notifications_MoveNeedsDeveloperMode"),
            };
        }
        move.Click += async (_, _) =>
        {
            Refresh();
            if (Updater.DeveloperModeOn) await updater.MoveToPackageAsync();
        };
        developer.Click += (_, _) => Refresh();
        updater.Changed += Refresh;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { developer, move } };
        var card = SettingsUi.Card(context.Palette, L.Get("Notifications_Install"), buttons, state);
        card.Unloaded += (_, _) => updater.Changed -= Refresh;
        // Back from Windows Settings with Developer Mode on, the card says so.
        card.GotFocus += (_, _) => Refresh();
        card.PointerEntered += (_, _) => Refresh();
        Refresh();
        return card;
    }
}
