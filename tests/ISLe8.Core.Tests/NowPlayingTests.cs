using ISLe8.Core.Media;

namespace ISLe8.Core.Tests;

public class NowPlayingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private static NowPlaying Song(bool playing, double rate = 1) => new()
    {
        Title = "Song",
        Duration = TimeSpan.FromMinutes(3),
        Position = TimeSpan.FromSeconds(60),
        PositionUpdatedAt = T0,
        IsPlaying = playing,
        Rate = rate,
    };

    [Fact]
    public void PositionAdvancesWhilePlaying()
    {
        Assert.Equal(TimeSpan.FromSeconds(70), Song(playing: true).PositionAt(T0.AddSeconds(10)));
    }

    [Fact]
    public void PositionHoldsWhilePaused()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), Song(playing: false).PositionAt(T0.AddSeconds(10)));
    }

    [Fact]
    public void PositionFollowsTheRate()
    {
        Assert.Equal(TimeSpan.FromSeconds(80), Song(playing: true, rate: 2).PositionAt(T0.AddSeconds(10)));
    }

    [Fact]
    public void PositionStopsAtTheEnd()
    {
        Assert.Equal(TimeSpan.FromMinutes(3), Song(playing: true).PositionAt(T0.AddHours(1)));
    }

    [Fact]
    public void AnEmptyOrFutureTimestampMeansNow()
    {
        var now = T0;

        Assert.Equal(now, NowPlaying.NormalizeTimestamp(new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero), now));
        Assert.Equal(now, NowPlaying.NormalizeTimestamp(now.AddHours(2), now));
        Assert.Equal(now.AddSeconds(-3), NowPlaying.NormalizeTimestamp(now.AddSeconds(-3), now));
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Media Player")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", "Apple Music")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("308046B0AF4A39CB", "308046B0AF4A39CB")]
    [InlineData(@"C:\Program Files\Foo\coolplayer.exe", "Coolplayer")]
    [InlineData("Contoso.Player_abc123!App", "Player")]
    [InlineData("", "")]
    public void NamesThePlayer(string appId, string expected)
    {
        Assert.Equal(expected, MediaSources.FriendlyName(appId));
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(7, "0:07")]
    [InlineData(187, "3:07")]
    [InlineData(3729, "1:02:09")]
    [InlineData(-4, "0:00")]
    public void FormatsTimes(int seconds, string expected)
    {
        Assert.Equal(expected, TimeText.Format(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void FormatsWhatIsLeft()
    {
        Assert.Equal("-2:45", TimeText.Remaining(TimeSpan.FromSeconds(15.6), TimeSpan.FromSeconds(180)));
    }
}
