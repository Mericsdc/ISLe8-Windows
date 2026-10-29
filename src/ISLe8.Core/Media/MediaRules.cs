namespace ISLe8.Core.Media;

/// <summary>A media session as far as choosing one goes: whose it is and whether it plays.</summary>
public readonly record struct SessionSummary(string AppId, bool IsPlaying);

/// <summary>
/// The rules behind PORTING.md §3.1's media fixes, kept apart from SMTC so they can be tested.
/// </summary>
public static class MediaRules
{
    /// <summary>
    /// The session the island follows. Windows' "current" one can be a paused player while another
    /// plays (a paused YouTube tab, Spotify going); then the playing one wins. Otherwise Windows'
    /// choice stands. Returns the index into <paramref name="sessions"/>, or -1 for none.
    /// </summary>
    public static int PickSession(IReadOnlyList<SessionSummary> sessions, string? currentAppId)
    {
        var current = -1;
        for (var i = 0; i < sessions.Count; i++)
        {
            if (sessions[i].AppId == currentAppId) current = i;
        }
        if (current >= 0 && sessions[current].IsPlaying) return current;
        for (var i = 0; i < sessions.Count; i++)
        {
            if (sessions[i].IsPlaying) return i;
        }
        return current >= 0 ? current : sessions.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// When paused playback resumes, the player may not send a new timeline. The old one's timestamp
    /// is from before the pause (or the pause itself, where the position was held); counting from it
    /// would jump the position forward by the whole pause. So the position counts from the resume instead.
    /// </summary>
    public static DateTimeOffset ResumeTimestamp(DateTimeOffset reported, DateTimeOffset pausedAt, DateTimeOffset now) =>
        reported <= pausedAt ? now : reported;

    /// <summary>Waits between attempts when Windows won't hand over the media sessions yet: 1, 2, 4, 8, 16, then every 30 s.</summary>
    public static TimeSpan RetryDelay(int attempt) =>
        TimeSpan.FromSeconds(attempt >= 5 ? 30 : 1 << Math.Max(attempt, 0));
}

/// <summary>
/// Decides when a new song deserves a banner (MediaModule.swift): something new is playing, but not
/// whatever was already playing when ISLe8 started, and not the same song resuming.
/// </summary>
public sealed class TrackChangeDetector
{
    private bool _seenFirst;
    private string? _lastTrack;

    /// <summary>
    /// Feeds the latest report; true when the banner should show. Like the Mac it compares with the
    /// previous report, paused ones included: a song picked while paused gets no banner when it plays.
    /// A session Windows has only just handed over has no title yet; that report is passed over and
    /// the one with the title counts.
    /// </summary>
    public bool Update(NowPlaying? now)
    {
        if (now is { Title: "" }) return false;
        var first = !_seenFirst;
        _seenFirst = true;
        var previous = _lastTrack;
        _lastTrack = now?.TrackKey;
        return now is { IsPlaying: true } && now.TrackKey != previous && !first;
    }
}
