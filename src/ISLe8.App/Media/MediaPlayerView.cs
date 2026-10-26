using ISLe8.Core.Media;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Media;

/// <summary>
/// The open island's player: artwork, title, artist and player, a scrubber, and shuffle /
/// previous / play-pause / next / repeat. Its clock only ticks while the island is open and playing.
/// </summary>
internal sealed class MediaPlayerView
{
    public const double ArtworkSize = 120;

    private readonly MediaService _media;
    private readonly IslandTheme _theme;
    private readonly DispatcherQueueTimer _clock;
    private readonly Image _artwork;
    private readonly FontIcon _placeholder;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly Slider _scrubber;
    private readonly TextBlock _elapsed;
    private readonly TextBlock _remaining;
    private readonly Button _shuffle;
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly Button _next;
    private readonly Button _repeat;
    private readonly FontIcon _shuffleIcon;
    private readonly FontIcon _playPauseIcon;
    private readonly FontIcon _repeatIcon;
    private bool _active;
    private bool _scrubbing;
    private bool _settingValue;

    public MediaPlayerView(MediaService media, IslandTheme theme, DispatcherQueue queue)
    {
        _media = media;
        _theme = theme;

        _placeholder = new FontIcon { Glyph = Glyphs.Music, FontSize = 36, Foreground = Ui.White(0x59) };
        _artwork = new Image { Stretch = Stretch.UniformToFill };
        var artwork = new Border
        {
            Width = ArtworkSize,
            Height = ArtworkSize,
            CornerRadius = new CornerRadius(Math.Max(theme.CornerRadius - 2, 4)),
            Background = theme.Surface.ToBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Grid { Children = { _placeholder, _artwork } },
        };

        _title = Ui.Text(16, semibold: true);
        _subtitle = Ui.Text(12, alpha: 0x99);
        _subtitle.Margin = new Thickness(0, 2, 0, 0);

        _scrubber = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            StepFrequency = 1,
            IsThumbToolTipEnabled = false,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var accent = theme.Accent.ToBrush();
        foreach (var key in new[] { "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed", "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed" })
        {
            _scrubber.Resources[key] = accent;
        }
        foreach (var key in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" })
        {
            _scrubber.Resources[key] = Ui.White(0x33);
        }
        _scrubber.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _scrubbing = true), true);
        _scrubber.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => EndScrub()), true);
        _scrubber.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => EndScrub()), true);
        _scrubber.ValueChanged += OnScrubberMoved;

        _elapsed = Ui.Text(11, alpha: 0x8C);
        _remaining = Ui.Text(11, alpha: 0x8C);
        _remaining.HorizontalAlignment = HorizontalAlignment.Right;

        _shuffle = Ui.IconButton(Glyphs.Shuffle, 32, 14, L.Get("Media_Shuffle"), media.ToggleShuffle, out _shuffleIcon);
        _previous = Ui.IconButton(Glyphs.Previous, 34, 16, L.Get("Media_Previous"), media.Previous, out _);
        _playPause = Ui.IconButton(Glyphs.Play, 40, 18, L.Get("Media_PlayPause"), media.TogglePlayPause, out _playPauseIcon);
        _playPause.Background = Ui.White(0x1F);
        _next = Ui.IconButton(Glyphs.Next, 34, 16, L.Get("Media_Next"), media.Next, out _);
        _repeat = Ui.IconButton(Glyphs.RepeatAll, 32, 14, L.Get("Media_Repeat"), media.CycleRepeat, out _repeatIcon);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _shuffle, _previous, _playPause, _next, _repeat },
        };
        var controls = new Grid { ColumnDefinitions = { Star(), Auto(), Star() } };
        controls.Children.Add(Place(_elapsed, column: 0));
        _elapsed.VerticalAlignment = VerticalAlignment.Center;
        _remaining.VerticalAlignment = VerticalAlignment.Center;
        controls.Children.Add(Place(buttons, column: 1));
        controls.Children.Add(Place(_remaining, column: 2));

        var details = new Grid
        {
            RowDefinitions = { AutoRow(), AutoRow(), StarRow(), AutoRow(), AutoRow() },
            Margin = new Thickness(16, 0, 0, 0),
        };
        details.Children.Add(PlaceRow(_title, 0));
        details.Children.Add(PlaceRow(_subtitle, 1));
        details.Children.Add(PlaceRow(_scrubber, 3));
        details.Children.Add(PlaceRow(controls, 4));

        Root = new Grid
        {
            Padding = new Thickness(20),
            ColumnDefinitions = { Auto(), Star() },
        };
        Root.Children.Add(Place(artwork, column: 0));
        Root.Children.Add(Place(details, column: 1));

        _clock = queue.CreateTimer();
        _clock.Interval = TimeSpan.FromMilliseconds(500);
        _clock.IsRepeating = true;
        _clock.Tick += (_, _) => UpdateProgress();
    }

    public Grid Root { get; }

    /// <summary>True while the island is open. Closed, the clock stops and nothing here runs.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        Refresh();
    }

    public void SetArtwork(ImageSource? source)
    {
        _artwork.Source = source;
        _placeholder.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Refresh()
    {
        var now = _media.Current;
        if (now is null)
        {
            _title.Text = L.Get("Media_NothingPlaying");
            _subtitle.Text = L.Get("Media_NothingPlayingHint");
        }
        else
        {
            _title.Text = string.IsNullOrWhiteSpace(now.Title) ? L.Get("Media_UnknownTitle") : now.Title;
            _subtitle.Text = string.Join(" · ", new[] { now.Artist, now.SourceName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        _playPause.IsEnabled = now?.CanPlayPause ?? false;
        _previous.IsEnabled = now?.CanPrevious ?? false;
        _next.IsEnabled = now?.CanNext ?? false;
        _shuffle.IsEnabled = now?.CanShuffle ?? false;
        _repeat.IsEnabled = now?.CanRepeat ?? false;
        _playPauseIcon.Glyph = now?.IsPlaying == true ? Glyphs.Pause : Glyphs.Play;
        _shuffleIcon.Foreground = now?.IsShuffling == true ? _theme.Accent.ToBrush() : Ui.White(0x8C);
        _repeatIcon.Glyph = now?.Repeat == RepeatMode.One ? Glyphs.RepeatOne : Glyphs.RepeatAll;
        _repeatIcon.Foreground = now is { Repeat: not RepeatMode.Off } ? _theme.Accent.ToBrush() : Ui.White(0x8C);

        var hasTimeline = now is { Duration.TotalSeconds: > 0 };
        _scrubber.Visibility = hasTimeline ? Visibility.Visible : Visibility.Collapsed;
        _scrubber.IsEnabled = now?.CanSeek ?? false;
        _elapsed.Visibility = _remaining.Visibility = hasTimeline ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress();

        if (_active && now is { IsPlaying: true } && hasTimeline) _clock.Start();
        else _clock.Stop();
    }

    private void UpdateProgress()
    {
        if (_media.Current is not { Duration.TotalSeconds: > 0 } now || _scrubbing) return;
        var position = now.PositionAt(DateTimeOffset.Now);
        _settingValue = true;
        _scrubber.Maximum = Math.Floor(now.Duration.TotalSeconds);
        _scrubber.Value = Math.Floor(position.TotalSeconds);
        _settingValue = false;
        _elapsed.Text = TimeText.Format(position);
        _remaining.Text = TimeText.Remaining(position, now.Duration);
    }

    private void OnScrubberMoved(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_settingValue || !_scrubbing || _media.Current is not { } now) return;
        var position = TimeSpan.FromSeconds(e.NewValue);
        _elapsed.Text = TimeText.Format(position);
        _remaining.Text = TimeText.Remaining(position, now.Duration);
    }

    private void EndScrub()
    {
        if (!_scrubbing) return;
        _scrubbing = false;
        _media.Seek(TimeSpan.FromSeconds(_scrubber.Value));
    }

    private static ColumnDefinition Auto() => new() { Width = GridLength.Auto };
    private static ColumnDefinition Star() => new() { Width = new GridLength(1, GridUnitType.Star) };
    private static RowDefinition AutoRow() => new() { Height = GridLength.Auto };
    private static RowDefinition StarRow() => new() { Height = new GridLength(1, GridUnitType.Star) };

    private static FrameworkElement Place(FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        return element;
    }

    private static FrameworkElement PlaceRow(FrameworkElement element, int row)
    {
        Grid.SetRow(element, row);
        return element;
    }
}
