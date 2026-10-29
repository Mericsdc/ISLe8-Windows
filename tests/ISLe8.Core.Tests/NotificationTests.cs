using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Notifications;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Tests;

/// <summary>The notification mirror: banners, the queue behind them and the mute list (PORTING.md §3.11).</summary>
public class NotificationTests
{
    private readonly ManualClock _clock = new();
    private readonly AppSettings _settings = new();
    private readonly IslandModel _model;

    public NotificationTests()
    {
        _model = new IslandModel(_settings, _clock);
    }

    private void Wait(double milliseconds) => _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));

    private static AppNotification Mail(uint id, string app = "Chrome") =>
        AppNotification.FromLines(id, app, app, ["Ayşe", "Toplantı", "Yarın 10:00"], DateTimeOffset.UnixEpoch);

    [Fact]
    public void AToastsFirstLineIsTheTitleAndTheRestOneLine()
    {
        var mail = Mail(1);
        Assert.Equal("Ayşe", mail.Title);
        Assert.Equal("Toplantı · Yarın 10:00", mail.Detail);

        var bare = AppNotification.FromLines(2, "x", "X", ["  ", null, " Yalnız başlık "], DateTimeOffset.UnixEpoch);
        Assert.Equal("Yalnız başlık", bare.Title);
        Assert.Equal("", bare.Detail);
    }

    [Fact]
    public void ANotificationShowsForTheChosenTime()
    {
        Assert.Equal(4, _settings.NotificationDuration);
        _model.ShowNotification(Mail(1));
        Assert.Equal(IslandState.Alert, _model.State);
        Assert.Equal(1u, Assert.IsType<NotificationAlert>(_model.Alert).Notification.Id);

        Wait(3999);
        Assert.Equal(IslandState.Alert, _model.State);
        Wait(1);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void OthersWaitBehindTheBannerAndFollowIt()
    {
        var counts = new List<int>();
        _model.PendingNotificationsChanged += () => counts.Add(_model.PendingNotificationCount);
        _model.ShowNotification(Mail(1));
        _model.ShowNotification(Mail(2));
        _model.ShowNotification(Mail(3));
        Assert.Equal(2, _model.PendingNotificationCount);
        Assert.Equal(1u, ((NotificationAlert)_model.Alert!).Notification.Id);

        Wait(4000);
        Wait(0);
        Assert.Equal(IslandState.Alert, _model.State);
        Assert.Equal(2u, ((NotificationAlert)_model.Alert!).Notification.Id);
        Assert.Equal(1, _model.PendingNotificationCount);
        Assert.Equal([1, 2, 1], counts);
    }

    [Fact]
    public void AtMostFiveWaitAndTheOldestGiveWay()
    {
        _model.ShowNotification(Mail(1));
        for (uint id = 2; id <= 8; id++) _model.ShowNotification(Mail(id));
        Assert.Equal(IslandModel.NotificationQueueLimit, _model.PendingNotificationCount);

        Wait(4000);
        Wait(0);
        Assert.Equal(4u, ((NotificationAlert)_model.Alert!).Notification.Id);
    }

    [Fact]
    public void TheyWaitWhileTheIslandIsOpenOrUnderThePointer()
    {
        _model.Click();
        _model.ShowNotification(Mail(1));
        Assert.Equal(IslandState.Expanded, _model.State);
        Assert.Equal(1, _model.PendingNotificationCount);

        _model.Close();
        Wait(0);
        Assert.Equal(IslandState.Alert, _model.State);
        Assert.Equal(0, _model.PendingNotificationCount);

        Wait(4000);
        // The pointer passes over the pill too briefly to open it: the notification waits for it to leave.
        _model.SetHovering(true);
        _model.ShowNotification(Mail(2));
        Assert.Equal(1, _model.PendingNotificationCount);
        _model.SetHovering(false);
        Wait(0);
        Assert.Equal(2u, ((NotificationAlert)_model.Alert!).Notification.Id);
    }

    [Fact]
    public void ANewSongDoesNotWaitButANotificationDoesNotReplaceIt()
    {
        _model.ShowAlert(new NowPlayingAlert(), AlertDurations.NowPlaying);
        _model.ShowNotification(Mail(1));
        // A banner that is not a notification gives way at once, as on the Mac.
        Assert.IsType<NotificationAlert>(_model.Alert);
        Assert.Equal(0, _model.PendingNotificationCount);
    }

    [Fact]
    public void LockingDropsWhatWaitsAndShowsNothing()
    {
        _model.ShowNotification(Mail(1));
        _model.ShowNotification(Mail(2));
        _model.SetLocked(true);
        Assert.Equal(IslandState.Pill, _model.State);
        Assert.Equal(0, _model.PendingNotificationCount);

        _model.ShowNotification(Mail(3));
        _model.ShowAlert(new NowPlayingAlert(), AlertDurations.NowPlaying);
        Assert.Equal(IslandState.Pill, _model.State);

        _model.SetLocked(false);
        _model.ShowNotification(Mail(4));
        Assert.Equal(4u, ((NotificationAlert)_model.Alert!).Notification.Id);
    }

    [Fact]
    public void NotificationsAndMeetingsGetTheBiggerBanner()
    {
        Assert.Equal(new DipSize(400, 106), IslandSizes.Alert(new NotificationAlert(Mail(1))));
        Assert.Equal(new DipSize(340, 78), IslandSizes.Alert(new NowPlayingAlert()));
    }

    [Fact]
    public void SettingsListsTheAppsThatSentNotificationsNewestFirst()
    {
        var seen = new List<NotificationApp>();
        Assert.True(NotificationRules.Remember(seen, Mail(1, "Chrome")));
        Assert.True(NotificationRules.Remember(seen, Mail(2, "Teams")));
        Assert.False(NotificationRules.Remember(seen, Mail(3, "Chrome")));
        Assert.Equal(["Teams", "Chrome"], seen.Select(app => app.Id));

        for (var i = 0; i < 40; i++) NotificationRules.Remember(seen, Mail(10, $"App{i}"));
        Assert.Equal(NotificationRules.SeenLimit, seen.Count);
        Assert.Equal("App39", seen[0].Id);
    }

    [Fact]
    public void AMutedAppOrFocusKeepsTheBannerAway()
    {
        var mail = Mail(1, "Chrome");
        Assert.True(NotificationRules.Shows(_settings, mail, focusActive: false));
        Assert.False(NotificationRules.Shows(_settings, mail, focusActive: true));

        NotificationRules.SetMuted(_settings, "chrome", true);
        Assert.False(NotificationRules.Shows(_settings, mail, focusActive: false));
        NotificationRules.SetMuted(_settings, "CHROME", false);
        Assert.Empty(_settings.MutedNotificationApps);

        _settings.RespectFocus = false;
        Assert.True(NotificationRules.Shows(_settings, mail, focusActive: true));
        _settings.NotificationsEnabled = false;
        Assert.False(NotificationRules.Shows(_settings, mail, focusActive: false));
    }

    [Fact]
    public void TheDurationKeepsToTheMacsSlider()
    {
        var settings = new AppSettings { NotificationDuration = 12 };
        settings.Normalize();
        Assert.Equal(10, settings.NotificationDuration);
        settings.NotificationDuration = 3.3;
        settings.Normalize();
        Assert.Equal(3.5, settings.NotificationDuration);
        settings.NotificationDuration = double.NaN;
        settings.Normalize();
        Assert.Equal(4, settings.NotificationDuration);
    }
}
