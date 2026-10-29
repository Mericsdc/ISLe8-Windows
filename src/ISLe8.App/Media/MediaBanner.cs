using ISLe8.Core.Theming;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Media;

/// <summary>
/// The short banner when a new song starts (MediaViews.swift, MediaAlertView): artwork, title and
/// artist, and the bars on the right.
/// </summary>
internal sealed class MediaBanner
{
    private readonly MediaService _media;
    private readonly Artwork _artwork = Tiles.Artwork(30, 7);
    private readonly TextBlock _title = Ui.Text(12, semibold: true);
    private readonly TextBlock _artist = Ui.Text(11, alpha: 0x99);

    public MediaBanner(MediaService media, IslandTheme theme)
    {
        _media = media;
        var text = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _title, _artist },
        };
        var barsHost = new Border
        {
            Width = AudioBars.Width,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Bars = new AudioBars(barsHost);
        Bars.SetColor(theme.Accent.ToColor());

        _artwork.Root.VerticalAlignment = VerticalAlignment.Center;
        // The Mac's spacer keeps at least 8 between the title and the bars; the grid's spacing gives 10.
        var row = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            Children = { _artwork.Root, text, barsHost },
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(barsHost, 2);
        Root = row;
    }

    public FrameworkElement Root { get; }

    public AudioBars Bars { get; }

    public void SetArtwork(ImageSource? source) => _artwork.Set(source);

    public void Refresh()
    {
        var now = _media.Current;
        _title.Text = now?.Title ?? "";
        _artist.Text = now?.Artist ?? "";
    }
}
