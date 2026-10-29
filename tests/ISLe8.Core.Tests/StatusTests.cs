using ISLe8.Core.Devices;
using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Motion;
using ISLe8.Core.Notifications;
using ISLe8.Core.Settings;
using ISLe8.Core.Status;

namespace ISLe8.Core.Tests;

/// <summary>Battery and cable status, and the volume and brightness HUDs (PORTING.md §2, §3.2, §3.3, §3.20).</summary>
public class StatusTests
{
    private readonly ManualClock _clock = new();
    private readonly AppSettings _settings = new();
    private readonly IslandModel _model;

    public StatusTests()
    {
        _model = new IslandModel(_settings, _clock);
    }

    private void Wait(double milliseconds) => _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));

    private static PcBattery OnBattery(int percent) => new(percent, IsCharging: false, IsPluggedIn: false);

    private static PcBattery Charging(int percent) => new(percent, IsCharging: true, IsPluggedIn: true);

    private static HudValue Volume(double level, bool muted = false) => new(HudKind.Volume, level, muted);

    // MARK: Battery events

    [Fact]
    public void TheFirstReadingSaysNothing()
    {
        var announcer = new BatteryAnnouncer();
        Assert.Null(announcer.Changed(OnBattery(15), alertsOn: true));
    }

    [Fact]
    public void PluggingInAndOutAreAnnouncedWithThePercent()
    {
        var announcer = new BatteryAnnouncer();
        announcer.Changed(OnBattery(61), alertsOn: true);

        Assert.Equal(new BatteryEvent(BatteryEventKind.PluggedIn, 61), announcer.Changed(Charging(61), alertsOn: true));
        Assert.Null(announcer.Changed(Charging(62), alertsOn: true));
        Assert.Equal(new BatteryEvent(BatteryEventKind.Unplugged, 62), announcer.Changed(OnBattery(62), alertsOn: true));
    }

    [Fact]
    public void TwentyAndTenAreAnnouncedOncePerDischarge()
    {
        var announcer = new BatteryAnnouncer();
        announcer.Changed(OnBattery(22), alertsOn: true);

        Assert.Null(announcer.Changed(OnBattery(21), alertsOn: true));
        Assert.Equal(new BatteryEvent(BatteryEventKind.Low, 20), announcer.Changed(OnBattery(20), alertsOn: true));
        Assert.Null(announcer.Changed(OnBattery(19), alertsOn: true));
        // Rising back over 20 on its own (a recalibration) and falling again says nothing more.
        announcer.Changed(OnBattery(21), alertsOn: true);
        Assert.Null(announcer.Changed(OnBattery(20), alertsOn: true));
        Assert.Equal(new BatteryEvent(BatteryEventKind.Critical, 10), announcer.Changed(OnBattery(10), alertsOn: true));
        Assert.Null(announcer.Changed(OnBattery(9), alertsOn: true));
    }

    [Fact]
    public void ADropPastBothSaysTheWorseOne()
    {
        var announcer = new BatteryAnnouncer();
        announcer.Changed(OnBattery(25), alertsOn: true);

        Assert.Equal(new BatteryEvent(BatteryEventKind.Critical, 8), announcer.Changed(OnBattery(8), alertsOn: true));
        Assert.Null(announcer.Changed(OnBattery(7), alertsOn: true));
    }

    [Fact]
    public void PluggingInForgetsTheWarnings()
    {
        var announcer = new BatteryAnnouncer();
        announcer.Changed(OnBattery(21), alertsOn: true);
        announcer.Changed(OnBattery(20), alertsOn: true);
        announcer.Changed(Charging(20), alertsOn: true);
        announcer.Changed(Charging(21), alertsOn: true);
        announcer.Changed(OnBattery(21), alertsOn: true);

        Assert.Equal(BatteryEventKind.Low, announcer.Changed(OnBattery(20), alertsOn: true)?.Kind);
    }

    [Fact]
    public void WithAlertsOffNothingIsAnnouncedButTheReadingIsKept()
    {
        var announcer = new BatteryAnnouncer();
        announcer.Changed(OnBattery(50), alertsOn: false);
        Assert.Null(announcer.Changed(Charging(50), alertsOn: false));
        // Already plugged in when they were turned back on: no news.
        Assert.Null(announcer.Changed(Charging(51), alertsOn: true));
    }

    [Fact]
    public void OnlyPluggingInAndOutAreStatusEars()
    {
        Assert.True(new BatteryEvent(BatteryEventKind.PluggedIn, 50).IsStatus);
        Assert.True(new BatteryEvent(BatteryEventKind.Unplugged, 50).IsStatus);
        Assert.False(new BatteryEvent(BatteryEventKind.Low, 20).IsStatus);
        Assert.False(new BatteryEvent(BatteryEventKind.Critical, 10).IsStatus);
        Assert.True(new BatteryAlert(new BatteryEvent(BatteryEventKind.PluggedIn, 50)).IsStatus());
        Assert.False(new BatteryAlert(new BatteryEvent(BatteryEventKind.Low, 20)).IsStatus());
        Assert.True(new WiredAlert(true, NetworkLink.Wired).IsStatus());
        Assert.True(new HudAlert(Volume(0.5)).IsEars());
        Assert.False(new HudAlert(Volume(0.5)).IsStatus());
    }

    [Fact]
    public void TheChipIsGreenOnPowerThenRedAt10AndOrangeAt20()
    {
        Assert.Equal(StatusTint.Green, BatteryLook.Chip(Charging(5)));
        Assert.Equal(StatusTint.Green, BatteryLook.Chip(new PcBattery(100, IsCharging: false, IsPluggedIn: true)));
        Assert.Equal(StatusTint.Red, BatteryLook.Chip(OnBattery(10)));
        Assert.Equal(StatusTint.Orange, BatteryLook.Chip(OnBattery(11)));
        Assert.Equal(StatusTint.Orange, BatteryLook.Chip(OnBattery(20)));
        Assert.Equal(StatusTint.White, BatteryLook.Chip(OnBattery(21)));
    }

    [Fact]
    public void TheSymbolFillsInQuarters()
    {
        Assert.Equal(4, BatteryLook.Quarters(88));
        Assert.Equal(3, BatteryLook.Quarters(87));
        Assert.Equal(3, BatteryLook.Quarters(63));
        Assert.Equal(2, BatteryLook.Quarters(62));
        Assert.Equal(2, BatteryLook.Quarters(38));
        Assert.Equal(1, BatteryLook.Quarters(37));
        Assert.Equal(1, BatteryLook.Quarters(13));
        Assert.Equal(0, BatteryLook.Quarters(12));
    }

    // MARK: Cable

    [Fact]
    public void AlreadyOnACableAtStartSaysNothing()
    {
        var watch = new WiredWatch();
        Assert.Null(watch.Changed(NetworkLink.Wired));
        Assert.Equal(NetworkLink.Wired, watch.Link);
    }

    [Fact]
    public void TheCableGoingInOrOutIsNewsButWifiChangesAreNot()
    {
        var watch = new WiredWatch();
        watch.Changed(NetworkLink.Wifi);

        Assert.Null(watch.Changed(NetworkLink.Offline));
        Assert.Null(watch.Changed(NetworkLink.Wifi));
        Assert.True(watch.Changed(NetworkLink.Wired));
        Assert.Null(watch.Changed(NetworkLink.Wired));
        Assert.False(watch.Changed(NetworkLink.Offline));
    }

    [Fact]
    public void TheAdapterTypeNamesTheLink()
    {
        Assert.Equal(NetworkLink.Wired, NetworkLinks.FromInterfaceType(6));
        Assert.Equal(NetworkLink.Wifi, NetworkLinks.FromInterfaceType(71));
        Assert.Equal(NetworkLink.Cellular, NetworkLinks.FromInterfaceType(243));
        Assert.Equal(NetworkLink.Other, NetworkLinks.FromInterfaceType(53));
        Assert.Equal(NetworkLink.Offline, NetworkLinks.FromInterfaceType(null));
    }

    // MARK: HUD values

    [Fact]
    public void TheSpeakerHasThreeWavesAndASlash()
    {
        Assert.Equal(SpeakerLevel.Muted, HudLook.Speaker(0.8, muted: true));
        Assert.Equal(SpeakerLevel.Muted, HudLook.Speaker(0, muted: false));
        Assert.Equal(SpeakerLevel.Low, HudLook.Speaker(0.33, muted: false));
        Assert.Equal(SpeakerLevel.Medium, HudLook.Speaker(0.34, muted: false));
        Assert.Equal(SpeakerLevel.Medium, HudLook.Speaker(0.66, muted: false));
        Assert.Equal(SpeakerLevel.High, HudLook.Speaker(0.67, muted: false));
        Assert.True(HudLook.IsDim(0.49));
        Assert.False(HudLook.IsDim(0.5));
    }

    [Fact]
    public void AMutedBarIsEmptyAndThePercentRounds()
    {
        Assert.Equal(0, Volume(0.7, muted: true).Shown);
        Assert.Equal(1, Volume(1.2).Shown);
        Assert.Equal(62, HudLook.Percent(0.62000000476837158));
        Assert.Equal(62, HudLook.Percent(0.625));
        Assert.Equal(63, HudLook.SliderPercent(0.625));
    }

    [Fact]
    public void OnlyARealVolumeChangeCounts()
    {
        var filter = new VolumeFilter();
        filter.Reset(0.5, muted: false);

        Assert.False(filter.IsChange(0.5004, muted: false));
        Assert.True(filter.IsChange(0.52, muted: false));
        Assert.False(filter.IsChange(0.52, muted: false));
        Assert.True(filter.IsChange(0.52, muted: true));
    }

    [Fact]
    public void BrightnessShowsOnlyRightAfterAKey()
    {
        var now = _clock.Now;
        Assert.False(BrightnessKeys.ShowsHud(now, null));
        Assert.True(BrightnessKeys.ShowsHud(now, now - TimeSpan.FromSeconds(1.1)));
        Assert.False(BrightnessKeys.ShowsHud(now, now - TimeSpan.FromSeconds(1.2)));
    }

    // MARK: On the island

    [Fact]
    public void EarsAreThePillsHeightAndAsWideAsTheMacs()
    {
        var hud = IslandSizes.Alert(new HudAlert(Volume(0.5)));
        var status = IslandSizes.Alert(new BatteryAlert(new BatteryEvent(BatteryEventKind.PluggedIn, 61)));
        var low = IslandSizes.Alert(new BatteryAlert(new BatteryEvent(BatteryEventKind.Low, 20)));

        Assert.Equal(new DipSize(IslandMetrics.IdlePillWidth + 2 * 92 + 2 * 6, IslandMetrics.PillHeight), hud);
        Assert.Equal(new DipSize(IslandMetrics.IdlePillWidth + 2 * 104 + 2 * 6, IslandMetrics.PillHeight), status);
        Assert.Equal(status, IslandSizes.Alert(new WiredAlert(false, NetworkLink.Wifi)));
        Assert.Equal(new DipSize(340, IslandMetrics.PillHeight + 46), low);
    }

    [Fact]
    public void TheHudShowsForASecondAndAHalfEvenUnderThePointer()
    {
        _model.SetHovering(true);
        _model.ShowHud(Volume(0.4));

        Assert.Equal(IslandState.Alert, _model.State);
        Assert.Equal(new HudAlert(Volume(0.4)), _model.Alert);
        // The hover that would have opened the island was called off.
        Wait(1499);
        Assert.Equal(IslandState.Alert, _model.State);
        Wait(1);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void EachPressRestartsTheHudInPlace()
    {
        var swaps = 0;
        _model.ShowHud(Volume(0.4));
        _model.AlertChanged += () => swaps++;
        Wait(1000);
        _model.ShowHud(Volume(0.46));
        Wait(1000);

        Assert.Equal(IslandState.Alert, _model.State);
        Assert.Equal(new HudAlert(Volume(0.46)), _model.Alert);
        Assert.Equal(1, swaps);
        Wait(500);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void TheHudNeverCoversAnOpenIslandOrANotification()
    {
        _model.Click();
        _model.ShowHud(Volume(0.4));
        Assert.Equal(IslandState.Expanded, _model.State);

        _model.Close();
        _model.ShowNotification(AppNotification.FromLines(1, "Chrome", "Chrome", ["Ayşe", "Merhaba"], DateTimeOffset.UnixEpoch));
        _model.ShowHud(Volume(0.4));
        Assert.IsType<NotificationAlert>(_model.Alert);

        _model.DismissAlert();
        _model.SetLocked(true);
        _model.ShowHud(Volume(0.4));
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void TheHudTakesABannersPlaceAndABannerTakesTheHuds()
    {
        _model.ShowAlert(new BatteryAlert(new BatteryEvent(BatteryEventKind.Low, 20)), AlertDurations.Battery);
        _model.ShowHud(Volume(0.4));
        Assert.IsType<HudAlert>(_model.Alert);
        // 340 wide banner to 396 wide ears.
        Assert.True(_model.IsGrowing);

        _model.ShowAlert(new BatteryAlert(new BatteryEvent(BatteryEventKind.Low, 19)), AlertDurations.Battery);
        Assert.IsType<BatteryAlert>(_model.Alert);
        Assert.True(_model.IsGrowing);
    }

    [Fact]
    public void StatusEarsSpringOutSlideBackThenFold()
    {
        var retracting = new List<bool>();
        _model.RetractingChanged += () => retracting.Add(_model.IsRetractingStatus);
        _model.ShowAlert(new BatteryAlert(new BatteryEvent(BatteryEventKind.PluggedIn, 61)), AlertDurations.Battery);

        Assert.Equal(SpringSpec.Status, _model.Spring);
        Wait(3000);
        Assert.True(_model.IsRetractingStatus);
        Assert.Equal(IslandState.Alert, _model.State);
        Wait(239);
        Assert.Equal(IslandState.Alert, _model.State);
        Wait(1);
        Assert.False(_model.IsRetractingStatus);
        Assert.Equal(IslandState.Pill, _model.State);
        Assert.Equal(SpringSpec.SoftClose, _model.Spring);
        Assert.Equal([true, false], retracting);
        Wait(700);
        Assert.Equal(SpringSpec.Close, _model.Spring);
    }

    [Fact]
    public void ThePointerArrivingStopsTheFold()
    {
        _model.ShowAlert(new WiredAlert(true, NetworkLink.Wired), AlertDurations.Wired);
        Wait(3100);
        Assert.True(_model.IsRetractingStatus);

        _model.SetHovering(true);
        Assert.False(_model.IsRetractingStatus);
        Wait(1000);
        Assert.Equal(IslandState.Alert, _model.State);

        _model.SetHovering(false);
        Wait(1200 + 240);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void ALowBatteryBannerClosesAtOnce()
    {
        _model.ShowAlert(new BatteryAlert(new BatteryEvent(BatteryEventKind.Critical, 10)), AlertDurations.Battery);
        Assert.Equal(SpringSpec.Open, _model.Spring);
        Wait(3000);
        Assert.False(_model.IsRetractingStatus);
        Assert.Equal(IslandState.Pill, _model.State);
        Assert.Equal(SpringSpec.Close, _model.Spring);
    }

    [Fact]
    public void TheMacDefaultsAreOn()
    {
        Assert.True(_settings.ShowVolumeHud);
        Assert.True(_settings.ShowBrightnessHud);
        Assert.True(_settings.ShowBatteryAlerts);
        Assert.True(_settings.ShowBatteryChip);
        Assert.True(_settings.ShowNetworkChip);
    }
}
