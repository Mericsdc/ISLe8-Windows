using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Media;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Tests;

/// <summary>Step 4: banners, Home's columns and the media rules (PORTING.md §2, §3.1).</summary>
public class BannerAndHomeTests
{
    private readonly ManualClock _clock = new();
    private readonly AppSettings _settings = new();
    private readonly IslandModel _model;
    private static readonly NowPlayingAlert Song = new();

    public BannerAndHomeTests()
    {
        _model = new IslandModel(_settings, _clock);
    }

    private void Wait(double milliseconds) => _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));

    // MARK: Banners

    [Fact]
    public void ABannerShowsForItsTimeThenGoes()
    {
        _model.ShowAlert(Song, AlertDurations.NowPlaying);
        Assert.Equal(IslandState.Alert, _model.State);
        Assert.Same(Song, _model.Alert);

        Wait(2499);
        Assert.Equal(IslandState.Alert, _model.State);
        Wait(1);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void ABannerWaitsWhileThePointerRestsOnIt()
    {
        _model.ShowAlert(Song, AlertDurations.NowPlaying);
        _model.SetHovering(true);
        Wait(10_000);
        Assert.Equal(IslandState.Alert, _model.State);

        _model.SetHovering(false);
        Wait(1199);
        Assert.Equal(IslandState.Alert, _model.State);
        Wait(1);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void RestingOnABannerDoesNotOpenTheIslandButAClickDoes()
    {
        _model.ShowAlert(Song, AlertDurations.NowPlaying);
        _model.SetHovering(true);
        Wait(1000);
        Assert.Equal(IslandState.Alert, _model.State);

        _model.Click();
        Assert.Equal(IslandState.Expanded, _model.State);
        Wait(10_000);
        Assert.Equal(IslandState.Expanded, _model.State);
    }

    [Fact]
    public void NoBannerOverAnOpenIslandOrUnderThePointer()
    {
        _model.Click();
        _model.ShowAlert(Song, AlertDurations.NowPlaying);
        Assert.Equal(IslandState.Expanded, _model.State);

        _model.Close();
        _model.SetHovering(true);
        _model.ShowAlert(Song, AlertDurations.NowPlaying);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void ANewBannerTakesTheOldOnesPlaceAndTime()
    {
        var swaps = 0;
        _model.AlertChanged += () => swaps++;
        _model.ShowAlert(Song, AlertDurations.NowPlaying);
        Wait(2000);
        var next = new NowPlayingAlert();
        _model.ShowAlert(next, AlertDurations.NowPlaying);
        Wait(2000);

        Assert.Equal(1, swaps);
        Assert.Equal(IslandState.Alert, _model.State);
        Assert.Same(next, _model.Alert);
        Wait(500);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void ABannerIsBiggerThanThePillAndGrows()
    {
        var growing = new List<bool>();
        _model.StateChanged += _ => growing.Add(_model.IsGrowing);
        _model.ShowAlert(Song, AlertDurations.NowPlaying);
        Wait(2500);

        Assert.Equal([true, false], growing);
        Assert.Equal(new DipSize(340, 78), IslandSizes.Alert(Song));
    }

    // MARK: Home

    [Fact]
    public void TheDefaultColumnsAreTheMacs()
    {
        Assert.Equal(["calendar", "media", "weather"], new AppSettings().HomeTiles);
    }

    [Fact]
    public void DashboardColumnsShareTheWidthByTheirWeights()
    {
        var widths = HomeColumns.Widths(HomeStyle.Dashboard, ["calendar", "media", "weather", "timer"], 792);

        Assert.Equal(792 - 3 * 10, widths.Sum(), precision: 6);
        Assert.Equal(1.1, widths[1] / widths[0], precision: 6);
        Assert.Equal(widths.Min(), widths[3]);
    }

    [Theory]
    [InlineData(HomeStyle.Compact, 822, 870)]   // 240 + 2 × (270 + 21)
    [InlineData(HomeStyle.Minimal, 672, 720)]   // 250 + 2 × (190 + 21)
    public void TheStripsAreAsWideAsTheirColumnsNeed(HomeStyle style, double strip, double island)
    {
        _settings.HomeStyle = style;

        Assert.Equal(strip, HomeStrip.Width(_settings.HomeTiles, style));
        Assert.Equal(island, IslandSizes.Width(_settings));
    }

    [Fact]
    public void ThePlayerSitsInTheMiddleOfTheStrip()
    {
        var widths = HomeColumns.Widths(HomeStyle.Minimal, ["calendar", "media", "weather"], 672);

        Assert.Equal(250, widths[1]);
        // Equal halves either side, gaps included: calendar gets the room weather doesn't need.
        Assert.Equal(widths[0] + HomeStrip.Gap, widths[2] + HomeStrip.Gap, precision: 6);
        Assert.Equal(672, widths.Sum() + 2 * HomeStrip.Gap, precision: 6);
    }

    [Fact]
    public void LopsidedSidesGoSideBySide()
    {
        // Timer on the right makes it 141 wider than the left: no centring.
        string[] columns = ["calendar", "media", "weather", "timer"];
        Assert.Equal(270 + 240 + 270 + 120 + 3 * 21, HomeStrip.Width(columns, HomeStyle.Compact));
        var widths = HomeStrip.Widths(columns, HomeStyle.Compact, 963);
        Assert.Equal([270, 240, 270, 120], widths.Select(w => Math.Round(w, 6)));

        // The player at an end cannot be in the middle either.
        Assert.Equal(240 + 21 + 270, HomeStrip.Width(["media", "weather"], HomeStyle.Compact));
    }

    [Fact]
    public void AStripNeverMakesTheIslandNarrowerThanTheMacs()
    {
        _settings.HomeStyle = HomeStyle.Compact;
        _settings.HomeTiles = ["media"];
        Assert.Equal(640, IslandSizes.Width(_settings));

        _settings.HomeStyle = HomeStyle.Minimal;
        _settings.HomeTiles = [];
        Assert.Equal(600, IslandSizes.Width(_settings));
    }

    [Fact]
    public void AnEmptyHomeHasNoColumns()
    {
        Assert.Empty(HomeColumns.Widths(HomeStyle.Dashboard, [], 792));
    }

    [Fact]
    public void ColumnsFromAFileAreKnownAndOnce()
    {
        var settings = SettingsStore.Import("""{ "homeTiles": ["media", "radio", "media", "timer"] }""");

        Assert.Equal(["media", "timer"], settings.HomeTiles);
        Assert.Equal(["calendar", "weather"], HomeColumns.Missing(settings));
        Assert.Empty(SettingsStore.Import("""{ "homeTiles": [] }""").HomeTiles);
    }

    // MARK: Media

    private static NowPlaying Track(string title, bool playing, int artwork = 0) =>
        new() { Title = title, Artist = "Artist", IsPlaying = playing, ArtworkVersion = artwork };

    [Fact]
    public void ANewArtworkForTheSameSongStillReloads()
    {
        Assert.NotEqual(Track("A", true, artwork: 1).ArtworkKey, Track("A", true, artwork: 2).ArtworkKey);
        Assert.Equal(Track("A", true, artwork: 1).TrackKey, Track("A", true, artwork: 2).TrackKey);
    }

    [Fact]
    public void APlayingSessionWinsOverAPausedCurrentOne()
    {
        SessionSummary[] sessions = [new("Chrome", false), new("Spotify.exe", true)];

        Assert.Equal(1, MediaRules.PickSession(sessions, "Chrome"));
        Assert.Equal(1, MediaRules.PickSession(sessions, "Spotify.exe"));
    }

    [Fact]
    public void WithNothingPlayingWindowsChoiceStands()
    {
        SessionSummary[] sessions = [new("Chrome", false), new("Spotify.exe", false)];

        Assert.Equal(0, MediaRules.PickSession(sessions, "Chrome"));
        Assert.Equal(0, MediaRules.PickSession(sessions, null));
        Assert.Equal(-1, MediaRules.PickSession([], null));
    }

    [Fact]
    public void ResumingCountsFromTheResumeNotFromBeforeThePause()
    {
        var reported = new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
        var pausedAt = reported.AddSeconds(30);
        var now = pausedAt.AddMinutes(5);

        Assert.Equal(now, MediaRules.ResumeTimestamp(reported, pausedAt, now));
        // The position held at the pause counts from the resume too.
        Assert.Equal(now, MediaRules.ResumeTimestamp(pausedAt, pausedAt, now));
        // A fresh timeline after the resume stands.
        Assert.Equal(now.AddSeconds(1), MediaRules.ResumeTimestamp(now.AddSeconds(1), pausedAt, now));
    }

    [Fact]
    public void RetriesBackOffToThirtySeconds()
    {
        Assert.Equal([1, 2, 4, 8, 16, 30, 30], Enumerable.Range(0, 7).Select(i => MediaRules.RetryDelay(i).TotalSeconds));
    }

    [Fact]
    public void OnlyANewPlayingSongGetsABanner()
    {
        var detector = new TrackChangeDetector();

        Assert.False(detector.Update(Track("A", true)));   // already playing when ISLe8 started
        Assert.False(detector.Update(Track("A", false)));  // paused
        Assert.False(detector.Update(Track("A", true)));   // resumed
        Assert.True(detector.Update(Track("B", true)));    // next song
        Assert.False(detector.Update(Track("C", false)));  // picked while paused…
        Assert.False(detector.Update(Track("C", true)));   // …then played: no banner, as on the Mac
        Assert.False(detector.Update(null));
        Assert.True(detector.Update(Track("D", true)));
    }

    [Fact]
    public void ASessionWithoutItsTitleYetIsPassedOver()
    {
        var atStart = new TrackChangeDetector();
        Assert.False(atStart.Update(Track("", true)));     // Windows hands over the session…
        Assert.False(atStart.Update(Track("A", true)));    // …and its title: still what played at launch

        var later = new TrackChangeDetector();
        Assert.False(later.Update(null));                  // nothing at launch
        Assert.False(later.Update(Track("", true)));
        Assert.True(later.Update(Track("B", true)));       // one banner, with the title
    }
}
