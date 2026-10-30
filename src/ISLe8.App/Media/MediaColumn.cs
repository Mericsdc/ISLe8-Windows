using ISLe8.Core.Media;
using ISLe8.Core.Settings;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Media;

/// <summary>
/// Home's media column (MediaViews.swift, MediaColumn). Wide: a card with artwork and title, the
/// scrubber, and shuffle / previous / play / next / repeat over the full height. Compact: no card;
/// the artwork beside the title, the artist and the same controls. Minimal: one line with the
/// artwork, the title, previous, play and next. Its clock only ticks while it can be seen and
/// something plays. Wide, the output volume's slider closes the card.
/// </summary>
internal sealed class MediaColumn
{
    /// <summary>The biggest the artwork is drawn; it is decoded at this size.</summary>
    public const double LargestArtwork = 104;

    private readonly MediaService _media;
    private readonly IslandTheme _theme;
    private readonly HomeStyle _style;
    private readonly Artwork _artwork;
    private readonly TextBlock _title;
    private readonly TextBlock _artist;
    private readonly Scrubber? _scrubber;
    private readonly DispatcherQueueTimer? _clock;
    private readonly Button _playPause;
    private readonly FontIcon _playPauseIcon;
    private readonly Button _next;
    private readonly Button _previous;
    private readonly Button? _shuffle;
    private readonly FontIcon? _shuffleIcon;
    private readonly Button? _repeat;
    private readonly FontIcon? _repeatIcon;
    private readonly VolumeRow? _volume;
    private bool _active;

    public MediaColumn(MediaService media, Status.SystemStatus status, IslandTheme theme, HomeStyle style, DispatcherQueue queue)
    {
        _media = media;
        _theme = theme;
        _style = style;
        _previous = Transport(Glyphs.Previous, prominent: false, L.Get("Media_Previous"), media.Previous, out _);
        _next = Transport(Glyphs.Next, prominent: false, L.Get("Media_Next"), media.Next, out _);

        if (style == HomeStyle.Minimal)
        {
            _artwork = Tiles.Artwork(38, 8);
            _title = Ui.Text(12, semibold: true);
            _artist = Ui.Text(10, alpha: 0x8C);
            _playPause = Transport(Glyphs.Play, prominent: true, L.Get("Media_Play"), media.TogglePlayPause, out _playPauseIcon);
            var text = new StackPanel
            {
                Spacing = 1,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { _title, _artist },
            };
            // The Mac's spacer keeps at least 4 between the title and the buttons; the grid's spacing gives 6.
            var row = new Grid
            {
                ColumnSpacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
                ColumnDefinitions = { Auto(), Star(), Auto(), Auto(), Auto() },
            };
            FrameworkElement[] parts = [_artwork.Root, text, _previous, _playPause, _next];
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i].VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(At(parts[i], column: i));
            }
            Root = row;
            return;
        }

        _shuffle = Mode(Glyphs.Shuffle, L.Get("Media_Shuffle"), media.ToggleShuffle, out _shuffleIcon);
        _repeat = Mode(Glyphs.RepeatAll, L.Get("Media_Repeat"), media.CycleRepeat, out _repeatIcon);

        if (style == HomeStyle.Compact)
        {
            // black 35 %, radius 6, 3 down on the Mac
            _artwork = Tiles.Artwork(64, 10, shadowBlur: 12, shadowOffset: 3);
            _title = Ui.Text(13, semibold: true);
            _artist = Ui.Text(10.5, alpha: 0x8C);
            _playPause = Play(34, accentRing: true, media.TogglePlayPause, out _playPauseIcon);
            // As tall as the artwork; the Mac lets its few extra points overflow, a Grid would clip them.
            var side = new Grid
            {
                MinHeight = 64,
                VerticalAlignment = VerticalAlignment.Center,
                RowSpacing = 2,
                RowDefinitions = { AutoRow(), AutoRow(), new RowDefinition(), AutoRow() },
            };
            side.Children.Add(_title);
            side.Children.Add(Row(_artist, 1));
            side.Children.Add(Row(Controls(), 3));
            var player = new Grid
            {
                ColumnSpacing = 10,
                VerticalAlignment = VerticalAlignment.Center,
                ColumnDefinitions = { Auto(), Star() },
            };
            player.Children.Add(_artwork.Root);
            player.Children.Add(At(side, column: 1));
            Root = player;
            return;
        }

        // black 35 %, radius 8, 4 down on the Mac
        _artwork = Tiles.Artwork(LargestArtwork, 14, shadowBlur: 16, shadowOffset: 4);
        _title = Ui.Text(13, semibold: true);
        _artist = Ui.Text(11, alpha: 0x8C);
        foreach (var text in new[] { _title, _artist })
        {
            text.TextWrapping = TextWrapping.Wrap;
            text.MaxLines = 2;
        }
        // Centred in the artwork's height, as the Mac's spacers above and below do.
        var titles = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _title, _artist },
        };
        var titleBox = new Grid { Height = LargestArtwork, Children = { titles } };
        var top = new Grid { ColumnSpacing = 12, ColumnDefinitions = { Auto(), Star() } };
        top.Children.Add(_artwork.Root);
        top.Children.Add(At(titleBox, column: 1));

        _scrubber = new Scrubber(media.Seek);
        _playPause = Play(52, accentRing: false, media.TogglePlayPause, out _playPauseIcon);

        // The rows spread over the column's full height, at least 8 apart.
        var rows = new Grid { RowDefinitions = { AutoRow(), Spacer(), AutoRow(), Spacer(), AutoRow(), Spacer(), AutoRow() } };
        rows.Children.Add(top);
        rows.Children.Add(Row(_scrubber.Root, 2));
        rows.Children.Add(Row(Controls(), 4));
        _volume = new VolumeRow(status, theme);
        rows.Children.Add(Row(_volume.Root, 6));
        Root = Tiles.Tile(theme, rows, new Thickness(14));

        _clock = queue.CreateTimer();
        _clock.Interval = TimeSpan.FromMilliseconds(500);
        _clock.IsRepeating = true;
        _clock.OnTick(() => _scrubber.Show(_media.Current, DateTimeOffset.Now));
        _scrubber.DraggingChanged += UpdateClock;
    }

    /// <summary>Shuffle / previous / play / next / repeat, spread across the width.</summary>
    private Grid Controls()
    {
        var controls = new Grid { ColumnDefinitions = { Auto(), Star(), Auto(), Star(), Auto(), Star(), Auto(), Star(), Auto() } };
        FrameworkElement[] buttons = [_shuffle!, _previous, _playPause, _next, _repeat!];
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].VerticalAlignment = VerticalAlignment.Center;
            controls.Children.Add(At(buttons[i], column: 2 * i));
        }
        return controls;
    }

    public FrameworkElement Root { get; }

    /// <summary>True while it can be seen. Turning on catches up with what changed while hidden.</summary>
    public void SetActive(bool active)
    {
        if (active == _active) return;
        _active = active;
        _volume?.SetActive(active);
        if (active) Refresh();
        else UpdateClock();
    }

    public void SetArtwork(ImageSource? source) => _artwork.Set(source);

    public void Refresh()
    {
        var now = _media.Current;
        _title.Text = now is null ? L.Get("Media_NothingPlaying")
            : string.IsNullOrWhiteSpace(now.Title) ? L.Get("Media_UnknownTitle") : now.Title;
        if (_style == HomeStyle.Minimal)
        {
            _artist.Text = now?.Artist ?? "";
            _artist.Visibility = string.IsNullOrEmpty(_artist.Text) ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            _artist.Text = now is null ? L.Get("Media_NothingPlayingHint") : now.Artist;
        }

        var playing = now?.IsPlaying == true;
        _playPauseIcon.Glyph = playing ? Glyphs.Pause : Glyphs.Play;
        ToolTipService.SetToolTip(_playPause, L.Get(playing ? "Media_Pause" : "Media_Play"));
        _playPause.IsEnabled = now?.CanPlayPause ?? false;
        _next.IsEnabled = now?.CanNext ?? false;
        _previous.IsEnabled = now?.CanPrevious ?? false;
        if (_shuffle is not null)
        {
            _shuffle.IsEnabled = now?.CanShuffle ?? false;
            _shuffleIcon!.Foreground = ModeColor(_shuffle.IsEnabled, now?.IsShuffling == true);
        }
        if (_repeat is not null)
        {
            _repeat.IsEnabled = now?.CanRepeat ?? false;
            _repeatIcon!.Glyph = now?.Repeat == RepeatMode.One ? Glyphs.RepeatOne : Glyphs.RepeatAll;
            _repeatIcon.Foreground = ModeColor(_repeat.IsEnabled, now is { Repeat: not RepeatMode.Off });
        }
        _scrubber?.Show(now, DateTimeOffset.Now);
        UpdateClock();
    }

    /// <summary>Twice a second, only while shown, playing and not being dragged.</summary>
    private void UpdateClock()
    {
        if (_clock is null) return;
        if (_active && _media.Current is { IsPlaying: true, Duration.TotalSeconds: > 0 } && !_scrubber!.IsDragging) _clock.Start();
        else _clock.Stop();
    }

    // MARK: Buttons

    /// <summary>The icon's own colour beats the button's disabled one, so a mode the player lacks is dimmed here.</summary>
    private SolidColorBrush ModeColor(bool enabled, bool on) =>
        !enabled ? Ui.White(0x40) : on ? _theme.Accent.ToBrush() : Ui.White(0x99);

    /// <summary>Previous, next and the minimal strip's play: a circle, filled when prominent (TransportButton).</summary>
    private static Button Transport(string glyph, bool prominent, string help, Action click, out FontIcon icon)
    {
        var button = Ui.IconButton(glyph, prominent ? 34 : 28, prominent ? 16 : 13, help, click, out icon);
        button.Background = prominent ? Ui.White(0x1F) : Ui.Clear();
        button.Resources["ButtonBackgroundPointerOver"] = Ui.White(prominent ? (byte)0x38 : (byte)0x1A);
        button.Resources["ButtonBackgroundPressed"] = Ui.White(prominent ? (byte)0x2B : (byte)0x12);
        return button;
    }

    /// <summary>Shuffle and repeat: no circle, tinted while on (ModeButton).</summary>
    private static Button Mode(string glyph, string help, Action click, out FontIcon icon)
    {
        var button = Ui.IconButton(glyph, 28, 12, help, click, out icon);
        button.Resources["ButtonBackgroundPointerOver"] = Ui.Clear();
        button.Resources["ButtonBackgroundPressed"] = Ui.Clear();
        return button;
    }

    /// <summary>
    /// The large play button in a ring, the centre of the controls (PlayButton). The compact strip
    /// rings it in the theme's accent.
    /// </summary>
    private Button Play(double size, bool accentRing, Action click, out FontIcon icon)
    {
        var button = Ui.IconButton(Glyphs.Play, size, Math.Round(size * 0.35), L.Get("Media_Play"), click, out icon);
        var ring = accentRing ? _theme.Accent.ToBrush() : Ui.White(0x24);
        button.Background = Ui.White(0x14);
        button.BorderBrush = ring;
        button.BorderThickness = new Thickness(accentRing ? 1.5 : 1);
        button.Resources["ButtonBackgroundPointerOver"] = Ui.White(0x29);
        button.Resources["ButtonBackgroundPressed"] = Ui.White(0x1F);
        button.Resources["ButtonBorderBrushPointerOver"] = ring;
        button.Resources["ButtonBorderBrushPressed"] = ring;
        button.Resources["ButtonBorderBrushDisabled"] = accentRing ? ring : Ui.White(0x14);
        return button;
    }

    private static ColumnDefinition Auto() => new() { Width = GridLength.Auto };
    private static ColumnDefinition Star() => new() { Width = new GridLength(1, GridUnitType.Star) };
    private static RowDefinition AutoRow() => new() { Height = GridLength.Auto };
    private static RowDefinition Spacer() => new() { Height = new GridLength(1, GridUnitType.Star), MinHeight = 8 };

    private static FrameworkElement At(FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        return element;
    }

    private static FrameworkElement Row(FrameworkElement element, int row)
    {
        Grid.SetRow(element, row);
        return element;
    }
}

/// <summary>
/// The song's progress (PlaybackScrubber): a thin bar that thickens while dragged, the time played
/// on the left and the time left on the right. Dragging seeks when let go.
/// </summary>
internal sealed class Scrubber
{
    private const double Thin = 4;
    private const double Thick = 6;

    private readonly Action<TimeSpan> _seek;
    private readonly Grid _bar;
    private readonly Border _track;
    private readonly Border _fill;
    private readonly TextBlock _elapsed;
    private readonly TextBlock _remaining;
    private NowPlaying? _now;
    private double _fraction;
    private double? _drag;

    public Scrubber(Action<TimeSpan> seek)
    {
        _seek = seek;
        _track = new Border { Height = Thin, CornerRadius = new CornerRadius(Thin / 2), Background = Ui.White(0x26), VerticalAlignment = VerticalAlignment.Center };
        _fill = new Border
        {
            Height = Thin,
            CornerRadius = new CornerRadius(Thin / 2),
            Background = Ui.White(0xD9),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Width = 0,
        };
        // A transparent background, so the whole 10 DIPs take the pointer, not just the bar.
        _bar = new Grid { Height = 10, Background = Ui.Clear(), Children = { _track, _fill } };
        _bar.SizeChanged += (_, _) => Draw();
        _bar.PointerPressed += (_, e) =>
        {
            if (_now is not { CanSeek: true } || !_bar.CapturePointer(e.Pointer)) return;
            e.Handled = true;
            Drag(e.GetCurrentPoint(_bar).Position.X);
            DraggingChanged?.Invoke();
        };
        _bar.PointerMoved += (_, e) =>
        {
            if (_drag is null) return;
            Drag(e.GetCurrentPoint(_bar).Position.X);
        };
        _bar.PointerReleased += (_, e) =>
        {
            if (_drag is not { } fraction) return;
            _bar.ReleasePointerCapture(e.Pointer);
            if (_now is { } now) _seek(now.Duration * fraction);
            EndDrag();
        };
        _bar.PointerCaptureLost += (_, _) => EndDrag();

        _elapsed = Time();
        _remaining = Time();
        _remaining.HorizontalAlignment = HorizontalAlignment.Right;
        var times = new Grid { Children = { _elapsed, _remaining } };

        Root = new StackPanel { Spacing = 3, Children = { _bar, times } };
    }

    public FrameworkElement Root { get; }

    public bool IsDragging => _drag is not null;

    public event Action? DraggingChanged;

    /// <summary>Where <paramref name="now"/> is at <paramref name="at"/>; with nothing playing, an empty bar at 0:00.</summary>
    public void Show(NowPlaying? now, DateTimeOffset at)
    {
        _now = now;
        if (_drag is not null) return;
        var position = now?.PositionAt(at) ?? TimeSpan.Zero;
        var duration = now?.Duration ?? TimeSpan.Zero;
        _fraction = duration > TimeSpan.Zero ? position / duration : 0;
        Write(position);
        Draw();
    }

    private void Drag(double x)
    {
        _drag = Math.Clamp(x / Math.Max(_bar.ActualWidth, 1), 0, 1);
        _fraction = _drag.Value;
        if (_now is { } now) Write(now.Duration * _fraction);
        Draw();
    }

    private void EndDrag()
    {
        if (_drag is null) return;
        _drag = null;
        Draw();
        DraggingChanged?.Invoke();
    }

    private void Write(TimeSpan position)
    {
        var duration = _now?.Duration ?? TimeSpan.Zero;
        _elapsed.Text = TimeText.Format(position);
        _remaining.Text = _now is null ? TimeText.Format(TimeSpan.Zero) : duration > TimeSpan.Zero ? TimeText.Remaining(position, duration) : "";
        // Nothing playing: the Mac's placeholder times are a little fainter.
        _elapsed.Opacity = _remaining.Opacity = _now is null ? 0.8 : 1;
    }

    private void Draw()
    {
        var height = _drag is null ? Thin : Thick;
        _track.Height = _fill.Height = height;
        _track.CornerRadius = _fill.CornerRadius = new CornerRadius(height / 2);
        _fill.Background = Ui.White(_drag is null ? (byte)0xD9 : (byte)0xFF);
        _fill.Width = _bar.ActualWidth * _fraction;
        _fill.Visibility = _now is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static TextBlock Time()
    {
        var text = Ui.Text(10, alpha: 0x80);
        text.FontWeight = FontWeights.Medium;
        // Tabular figures, so the times don't jitter as they count.
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        return text;
    }
}
