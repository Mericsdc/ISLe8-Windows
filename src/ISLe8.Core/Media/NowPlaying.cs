namespace ISLe8.Core.Media;

public enum RepeatMode
{
    Off,
    One,
    All,
}

/// <summary>What is playing right now, as the system media session reports it.</summary>
public sealed record NowPlaying
{
    public required string Title { get; init; }
    public string Artist { get; init; } = "";
    public string Album { get; init; } = "";

    /// <summary>The player's AppUserModelID, e.g. <c>Spotify.exe</c> or <c>Chrome</c>.</summary>
    public string SourceAppId { get; init; } = "";

    public TimeSpan Duration { get; init; }

    /// <summary>Playback position at <see cref="PositionUpdatedAt"/>.</summary>
    public TimeSpan Position { get; init; }

    public DateTimeOffset PositionUpdatedAt { get; init; }
    public bool IsPlaying { get; init; }
    public double Rate { get; init; } = 1;

    public bool CanPlayPause { get; init; }
    public bool CanNext { get; init; }
    public bool CanPrevious { get; init; }
    public bool CanShuffle { get; init; }
    public bool CanRepeat { get; init; }
    public bool CanSeek { get; init; }

    public bool IsShuffling { get; init; }
    public RepeatMode Repeat { get; init; }

    public string TrackKey => $"{Title}\u001F{Artist}\u001F{Album}";
    public string SourceName => MediaSources.FriendlyName(SourceAppId);

    /// <summary>The position at <paramref name="now"/>, worked out from the last report instead of a running timer.</summary>
    public TimeSpan PositionAt(DateTimeOffset now)
    {
        var position = Position;
        if (IsPlaying && now > PositionUpdatedAt)
        {
            position += (now - PositionUpdatedAt) * (Rate > 0 ? Rate : 1);
        }
        if (position < TimeSpan.Zero) position = TimeSpan.Zero;
        return Duration > TimeSpan.Zero && position > Duration ? Duration : position;
    }

    /// <summary>
    /// Some players leave the timeline's update time empty (year 1601) or set it ahead of the clock.
    /// Either way the report is taken to be from the moment it arrived.
    /// </summary>
    public static DateTimeOffset NormalizeTimestamp(DateTimeOffset reported, DateTimeOffset receivedAt) =>
        reported.Year < 2000 || reported > receivedAt + TimeSpan.FromMinutes(1) ? receivedAt : reported;
}

public static class MediaSources
{
    private static readonly (string Needle, string Name)[] Known =
    [
        ("spotify", "Spotify"),
        ("zunemusic", "Media Player"),
        ("applemusic", "Apple Music"),
        ("msedge", "Edge"),
        ("chrome", "Chrome"),
        ("firefox", "Firefox"),
        ("brave", "Brave"),
        ("opera", "Opera"),
        ("vivaldi", "Vivaldi"),
        ("vlc", "VLC"),
        ("foobar2000", "foobar2000"),
        ("musicbee", "MusicBee"),
        ("tidal", "TIDAL"),
        ("deezer", "Deezer"),
        ("amazonmusic", "Amazon Music"),
    ];

    /// <summary>A short player name from an AppUserModelID, a package family name or an .exe path.</summary>
    public static string FriendlyName(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return "";

        var lower = appId.ToLowerInvariant();
        foreach (var (needle, name) in Known)
        {
            if (lower.Contains(needle, StringComparison.Ordinal)) return name;
        }

        // "C:\...\Foo.exe", "Publisher.App_8wekyb3d8bbwe!App" or "Foo".
        var id = appId;
        var bang = id.IndexOf('!');
        if (bang >= 0) id = id[..bang];
        var slash = id.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) id = id[(slash + 1)..];
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
        var underscore = id.IndexOf('_');
        if (underscore > 0) id = id[..underscore];
        var dot = id.LastIndexOf('.');
        if (dot >= 0 && dot < id.Length - 1) id = id[(dot + 1)..];

        return id.Length == 0 ? "" : char.ToUpperInvariant(id[0]) + id[1..];
    }
}

public static class TimeText
{
    /// <summary>"3:07", or "1:02:09" past an hour.</summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        var seconds = (long)time.TotalSeconds;
        return seconds >= 3600
            ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
            : $"{seconds / 60}:{seconds % 60:00}";
    }

    /// <summary>"-2:45": what is left of the song.</summary>
    public static string Remaining(TimeSpan position, TimeSpan duration) =>
        "-" + Format(duration - TimeSpan.FromSeconds(Math.Floor(position.TotalSeconds)));
}
