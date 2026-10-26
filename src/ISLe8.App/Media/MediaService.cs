using ISLe8.Core.Diagnostics;
using ISLe8.Core.Media;
using Microsoft.UI.Dispatching;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace ISLe8.Media;

/// <summary>
/// What is playing, from Windows' System Media Transport Controls: the same session the volume
/// flyout shows (Spotify, browsers, Media Player, Apple Music…). Driven entirely by SMTC events;
/// nothing polls. Events arrive on worker threads and are handled on the UI thread. A player that
/// quits can leave its session object dead (calls throw); that drops back to whatever is current.
/// </summary>
internal sealed class MediaService : IDisposable
{
    private readonly DispatcherQueue _queue;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> _propertiesChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> _playbackChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> _timelineChanged;
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private string _appId = "";

    private string _title = "";
    private string _artist = "";
    private string _album = "";
    private GlobalSystemMediaTransportControlsSessionPlaybackStatus _status;
    private GlobalSystemMediaTransportControlsSessionPlaybackControls? _controls;
    private bool _shuffling;
    private RepeatMode _repeat;
    private double _rate = 1;
    private TimeSpan _start;
    private TimeSpan _duration;
    private TimeSpan _position;
    private DateTimeOffset _positionAt;

    public MediaService(DispatcherQueue queue)
    {
        _queue = queue;
        // The event's sender may be a different wrapper for the same session; work on the attached one.
        _propertiesChanged = (session, args) => OnUi(session, current => _ = ReadPropertiesAsync(current));
        _playbackChanged = (session, _) => OnUi(session, current => { ReadPlayback(current); Publish(); });
        _timelineChanged = (session, _) => OnUi(session, current => { ReadTimeline(current); Publish(); });
    }

    public NowPlaying? Current { get; private set; }

    /// <summary>The artwork as the player hands it over; decoded by whoever shows it.</summary>
    public IRandomAccessStreamReference? Thumbnail { get; private set; }

    public event Action? Changed;

    public async Task StartAsync()
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += (_, _) => _queue.TryEnqueue(() => Attach(CurrentSession()));
        }
        catch (Exception e)
        {
            Log.Error(e, "media");
            return;
        }
        Attach(CurrentSession());
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
        Detach();
        _manager = null;
    }

    private GlobalSystemMediaTransportControlsSession? CurrentSession()
    {
        try
        {
            return _manager?.GetCurrentSession();
        }
        catch (Exception e)
        {
            Log.Error(e, "media session");
            return null;
        }
    }

    private void Attach(GlobalSystemMediaTransportControlsSession? session)
    {
        Detach();
        _title = _artist = _album = "";
        Thumbnail = null;
        if (session is not null)
        {
            try
            {
                _appId = session.SourceAppUserModelId ?? "";
                session.MediaPropertiesChanged += _propertiesChanged;
                session.PlaybackInfoChanged += _playbackChanged;
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
                session.PlaybackInfoChanged -= _playbackChanged;
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
    /// Hops to the UI thread and drops events from a session that is no longer current. If the
    /// session has died under us, falls back to whatever session Windows calls current now.
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
                Attach(CurrentSession());
            }
        });

    private async Task ReadPropertiesAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var properties = await session.TryGetMediaPropertiesAsync();
            if (!ReferenceEquals(session, _session)) return;
            _title = properties.Title ?? "";
            _artist = string.IsNullOrEmpty(properties.Artist) ? properties.AlbumArtist ?? "" : properties.Artist;
            _album = properties.AlbumTitle ?? "";
            Thumbnail = properties.Thumbnail;
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

    private void ReadTimeline(GlobalSystemMediaTransportControlsSession session)
    {
        var timeline = session.GetTimelineProperties();
        _start = timeline.StartTime;
        _duration = timeline.EndTime - timeline.StartTime;
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
            IsPlaying = _status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            Rate = _rate,
            CanPlayPause = _controls is { } c && (c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled),
            CanNext = _controls?.IsNextEnabled ?? false,
            CanPrevious = _controls?.IsPreviousEnabled ?? false,
            CanShuffle = _controls?.IsShuffleEnabled ?? false,
            CanRepeat = _controls?.IsRepeatEnabled ?? false,
            CanSeek = (_controls?.IsPlaybackPositionEnabled ?? false) && _duration > TimeSpan.Zero,
            IsShuffling = _shuffling,
            Repeat = _repeat,
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
