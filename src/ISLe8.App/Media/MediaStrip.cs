using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Media;

/// <summary>
/// The song in one slim row, for minimal Home (64 DIPs tall): artwork, title and artist,
/// play/pause and next. Nothing in it ticks.
/// </summary>
internal sealed class MediaStrip
{
    private const double ArtworkSize = 44;

    private readonly MediaService _media;
    private readonly Image _artwork;
    private readonly FontIcon _placeholder;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;
    private readonly Button _playPause;
    private readonly Button _next;
    private readonly FontIcon _playPauseIcon;

    public MediaStrip(MediaService media, IslandTheme theme)
    {
        _media = media;
        _placeholder = new FontIcon { Glyph = Glyphs.Music, FontSize = 18, Foreground = Ui.White(0x59) };
        _artwork = new Image { Stretch = Stretch.UniformToFill };
        var artwork = new Border
        {
            Width = ArtworkSize,
            Height = ArtworkSize,
            CornerRadius = new CornerRadius(Math.Max(theme.CornerRadius / 2, 4)),
            Background = theme.Surface.ToBrush(),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Grid { Children = { _placeholder, _artwork } },
        };

        _title = Ui.Text(13, semibold: true);
        _subtitle = Ui.Text(11, alpha: 0x99);
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0),
            Spacing = 1,
            Children = { _title, _subtitle },
        };

        _playPause = Ui.IconButton(Glyphs.Play, 36, 16, L.Get("Media_PlayPause"), media.TogglePlayPause, out _playPauseIcon);
        _playPause.Background = Ui.White(0x1F);
        _next = Ui.IconButton(Glyphs.Next, 32, 14, L.Get("Media_Next"), media.Next, out _);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _playPause, _next },
        };

        Root = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        Root.Children.Add(artwork);
        Grid.SetColumn(text, 1);
        Root.Children.Add(text);
        Grid.SetColumn(buttons, 2);
        Root.Children.Add(buttons);
    }

    public Grid Root { get; }

    public void SetArtwork(ImageSource? source)
    {
        _artwork.Source = source;
        _placeholder.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }

    public void Refresh()
    {
        var now = _media.Current;
        _title.Text = now is null ? L.Get("Media_NothingPlaying")
            : string.IsNullOrWhiteSpace(now.Title) ? L.Get("Media_UnknownTitle") : now.Title;
        _subtitle.Text = now is null ? ""
            : string.Join(" · ", new[] { now.Artist, now.SourceName }.Where(part => !string.IsNullOrWhiteSpace(part)));
        _playPause.IsEnabled = now?.CanPlayPause ?? false;
        _next.IsEnabled = now?.CanNext ?? false;
        _playPauseIcon.Glyph = now?.IsPlaying == true ? Glyphs.Pause : Glyphs.Play;
    }
}
