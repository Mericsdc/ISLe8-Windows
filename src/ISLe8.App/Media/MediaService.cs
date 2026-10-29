using ISLe8.Core.Diagnostics;
using ISLe8.Core.Media;
using Microsoft.UI.Dispatching;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace ISLe8.Media;

/// <summary>
/// What is playing, from Windows' System Media Transport Controls: the same sessions the volume
/// flyout shows (Spotify, browsers, Media Player, Apple Music…). Driven entirely by SMTC events;
/// nothing polls. Events arrive on worker threads and are handled on the UI thread. A player that
/// quits can leave its session object dead (calls throw); that drops back to another session.
/// </summary>
/// <remarks>
/// Every session's playback is watched, so a player that starts while Windows still calls a
/// paused one current takes over (<see cref="MediaRules.PickSession"/>). Titles, artwork and the
/// timeline are read from the followed session only.
/// </remarks>
internal sealed class MediaService : IDisposable
{
    private readonly DispatcherQueue _queue;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _propertiesChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _playbackChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _timelineChanged;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private List<GlobalSystemMediaTransportControlsSession> _sessions = [];
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _appId = "";
    private bool _disposed;

    private string _title = "";
    private string _artist = "";
    private string _album = "";
    private int _artworkVersion;
    private GlobalSystemMediaTransportControlsSessionPlaybackStatus _status;
    private GlobalSystemMediaTransportControlsSessionPlaybackControls? _controls;
    private bool _shuffling;
    private RepeatMode _repeat;
    private double _rate = 1;
    private TimeSpan _start;
    private TimeSpan _duration;
    private TimeSpan _position;
    private DateTimeOffset _positionAt;
    /// <summary>The timeline as the player last reported it, to tell a new report from the same one read again.</summary>
    private (TimeSpan Position, DateTimeOffset At) _reported;
    private DateTimeOffset _pausedAt;

    public MediaService(DispatcherQueue queue)
    {
        _queue = queue;
        // The event's sender may be a different wrapper for the same session; work on the followed one.
        _propertiesChanged = (session, args) => OnUi(session, current => _ = ReadPropertiesAsync(current));
        _playbackChanged = (session, _) => _queue.TryEnqueue(() => OnPlaybackChanged(session));
        _timelineChanged = (session, _) => OnUi(session, current => { ReadTimeline(current); Publish(); });
    }

    public NowPlaying? Current { get; private set; }

    /// <summary>The artwork as the player hands it over; decoded by whoever shows it.</summary>
    public IRandomAccessStreamReference? Thumbnail { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Asks Windows for the media sessions. Right after sign-in that can fail; it is asked again
    /// after 1, 2, 4, 8 and 16 s, then every 30 s.
    /// </summary>
    public async Task StartAsync()
    {
        for (var attempt = 0; !_disposed; attempt++)
        {
            try
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                break;
            }
            catch (Exception e)
            {
                Log.Error(e, $"media (attempt {attempt + 1})");
            }
            await Task.Delay(MediaRules.RetryDelay(attempt));
        }
        if (_manager is not { } manager || _disposed) return;
        manager.SessionsChanged += (_, _) => _queue.TryEnqueue(Refresh);
        manager.CurrentSessionChanged += (_, _) => _queue.TryEnqueue(Refresh);
        Refresh();
        // Nothing to follow is news too: what starts after this is new (TrackChangeDetector).
        if (_session is null) Publish();
    }

    public void TogglePlayPause() => Send(session => session.TryTogglePlayPauseAsync());
    public void Next() => Send(session => session.TrySkipNextAsync());
    public void Previous() => Send(session => session.TrySkipPreviousAsync());
    public void ToggleShuffle() => Send(session => session.TryChangeShuffleActiveAsync(!_shuffling));

    /// <summary>Off → all → one → off, like the Mac version.</summary>
    public void CycleRepeat() => Send(session => session.TryChangeAutoRepeatModeAsync(_repeat switch
    {
        RepeatMode.Off => MediaPlaybackAutoRepeatMode.List,
        RepeatMode.All => MediaPlaybackAutoRepeatMode.Track,
        _ => MediaPlaybackAutoRepeatMode.None,
    }));

    public void Seek(TimeSpan position) => Send(session => session.TryChangePlaybackPositionAsync((_start + position).Ticks));

    public void Dispose()
    {
        _disposed = true;
        Detach();
        Watch([]);
        _manager = null;
    }

    // MARK: Sessions

    /// <summary>Sessions came or went, or Windows changed its mind about the current one.</summary>
    private void Refresh()
    {
        if (_manager is not { } manager || _disposed) return;
        try
        {
            Watch([.. manager.GetSessions()]);
        }
        catch (Exception e)
        {
            Log.Error(e, "media sessions");
            Watch([]);
        }
        Follow();
    }

    /// <summary>Listens to every session's playback, so one that starts playing can take over.</summary>
    private void Watch(List<GlobalSystemMediaTransportControlsSession> sessions)
    {
        foreach (var session in _sessions)
        {
            try
            {
                session.PlaybackInfoChanged -= _playbackChanged;
            }
            catch (Exception)
            {
                // A dead session can refuse even this; it is dropped either way.
            }
        }
        _sessions = [];
        foreach (var session in sessions)
        {
            try
            {
                session.PlaybackInfoChanged += _playbackChanged;
                _sessions.Add(session);
            }
            catch (Exception e)
            {
                Log.Error(e, "media watch");
            }
        }
    }

    /// <summary>Follows the session <see cref="MediaRules.PickSession"/> chooses; true if that is a different one.</summary>
    private bool Follow()
    {
        var summaries = new List<SessionSummary>(_sessions.Count);
        var alive = new List<GlobalSystemMediaTransportControlsSession>(_sessions.Count);
        foreach (var session in _sessions)
        {
            try
            {
                summaries.Add(new SessionSummary(
                    session.SourceAppUserModelId ?? "",
                    session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing));
                alive.Add(session);
            }
            catch (Exception)
            {
                // Its player just quit; SessionsChanged follows.
            }
        }
        string? current = null;
        try
        {
            current = _manager?.GetCurrentSession()?.SourceAppUserModelId;
        }
        catch (Exception e)
        {
            Log.Error(e, "media session");
        }

        var index = MediaRules.PickSession(summaries, current);
        var chosen = index < 0 ? null : alive[index];
        if (chosen is null ? _session is null : _session is not null && summaries[index].AppId == _appId) return false;
        Attach(chosen);
        return true;
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        Detach();
        _title = _artist = _album = "";
        Thumbnail = null;
        _reported = default;
        _pausedAt = DateTimeOffset.Now;
        if (session is not null)
        {
            try
            {
                _appId = session.SourceAppUserModelId ?? "";
                session.MediaPropertiesChanged += _propertiesChanged;
                session.TimelinePropertiesChanged += _timelineChanged;
                _session = session;
                ReadPlayback(session);
                ReadTimeline(session);
            }
            catch (Exception e)
            {
                Log.Error(e, "media attach");
                Detach();
            }
        }
        Publish();
        if (_session is { } attached) _ = ReadPropertiesAsync(attached);
    }

    private void Detach()
    {
        if (_session is { } session)
        {
            try
            {
                session.MediaPropertiesChanged -= _propertiesChanged;
                session.TimelinePropertiesChanged -= _timelineChanged;
            }
            catch (Exception)
            {
                // A dead session can refuse even this; it is dropped either way.
            }
        }
        _session = null;
        _appId = "";
    }

    /// <summary>
    /// Hops to the UI thread and drops events from a session that is not followed. If the session
    /// has died under us, follows another.
    /// </summary>
    private void OnUi(GlobalSystemMediaTransportControlsSession sender, Action<GlobalSystemMediaTransportControlsSession> action) =>
        _queue.TryEnqueue(() =>
        {
            try
            {
                if (_session is { } current && sender.SourceAppUserModelId == _appId) action(current);
            }
            catch (Exception e)
            {
                Log.Error(e, "media event");
                Refresh();
            }
        });

    /// <summary>Any session played or paused: it may take over; if it is the followed one, its state is read.</summary>
    private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender)
    {
        try
        {
            if (Follow()) return;
            if (_session is not { } current || sender.SourceAppUserModelId != _appId) return;
            var wasPlaying = IsPlaying;
            var playedFor = Current;
            ReadPlayback(current);
            if (wasPlaying && !IsPlaying)
            {
                // Hold the position where it stopped; a player that sends no timeline on pause
                // would otherwise show the last report's position.
                var now = DateTimeOffset.Now;
                if (playedFor is not null) _position = playedFor.PositionAt(now);
                _positionAt = _pausedAt = now;
                ReadTimeline(current);
            }
            else if (!wasPlaying && IsPlaying)
            {
                ReadTimeline(current);
                _positionAt = MediaRules.ResumeTimestamp(_positionAt, _pausedAt, DateTimeOffset.Now);
            }
            Publish();
        }
        catch (Exception e)
        {
            Log.Error(e, "media event");
            Refresh();
        }
    }

    // MARK: Reading

    private bool IsPlaying => _status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    private async Task ReadPropertiesAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(session, _session)) return;
            _title = properties.Title ?? "";
            _artist = string.IsNullOrEmpty(properties.Artist) ? properties.AlbumArtist ?? "" : properties.Artist;
            _album = properties.AlbumTitle ?? "";
            // Every report brings a new reference, even for the same picture: Chrome sends the old
            // song's cover under the new title first, the right one moments later.
            Thumbnail = properties.Thumbnail;
            _artworkVersion++;
            Publish();
        }
        catch (Exception e)
        {
            Log.Error(e, "media properties");
        }
    }

    private void ReadPlayback(GlobalSystemMediaTransportControlsSession session)
    {
        var info = session.GetPlaybackInfo();
        _status = info.PlaybackStatus;
        _controls = info.Controls;
        _shuffling = info.IsShuffleActive ?? false;
        _rate = info.PlaybackRate ?? 1;
        _repeat = info.AutoRepeatMode switch
        {
            MediaPlaybackAutoRepeatMode.Track => RepeatMode.One,
            MediaPlaybackAutoRepeatMode.List => RepeatMode.All,
            _ => RepeatMode.Off,
        };
    }

    /// <summary>
    /// Takes the player's timeline, unless it is the same report read again: then the position
    /// held at a pause, or counted from a resume, stands.
    /// </summary>
    private void ReadTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        var timeline = session.GetTimelineProperties();
        _start = timeline.StartTime;
        _duration = timeline.EndTime - timeline.StartTime;
        var reported = (timeline.Position, timeline.LastUpdatedTime);
        if (reported == _reported) return;
        _reported = reported;
        _position = timeline.Position - timeline.StartTime;
        _positionAt = NowPlaying.NormalizeTimestamp(timeline.LastUpdatedTime, DateTimeOffset.Now);
    }

    private void Publish()
    {
        Current = _session is null ? null : new NowPlaying
        {
            Title = _title,
            Artist = _artist,
            Album = _album,
            SourceAppId = _appId,
            Duration = _duration > TimeSpan.Zero ? _duration : TimeSpan.Zero,
            Position = _position,
            PositionUpdatedAt = _positionAt,
            IsPlaying = IsPlaying,
            Rate = _rate,
            CanPlayPause = _controls is { } c && (c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled),
            CanNext = _controls?.IsNextEnabled ?? false,
            CanPrevious = _controls?.IsPreviousEnabled ?? false,
            CanShuffle = _controls?.IsShuffleEnabled ?? false,
            CanRepeat = _controls?.IsRepeatEnabled ?? false,
            CanSeek = (_controls?.IsPlaybackPositionEnabled ?? false) && _duration > TimeSpan.Zero,
            IsShuffling = _shuffling,
            Repeat = _repeat,
            ArtworkVersion = _artworkVersion,
        };
        Changed?.Invoke();
    }

    private async void Send(Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
    {
        if (_session is not { } session) return;
        try
        {
            if (!await command(session)) Log.Info($"media command refused by {_appId}");
        }
        catch (Exception e)
        {
            Log.Error(e, "media command");
        }
    }
}
