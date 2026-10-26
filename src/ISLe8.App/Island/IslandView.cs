using ISLe8.Core.Geometry;
using ISLe8.Core.Theming;
using ISLe8.Media;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Island;

/// <summary>
/// The XAML side of the island. The tree is laid out once per monitor/DPI and never during an
/// animation: the compositor moves the shape and clips the content (see <see cref="IslandSurface"/>).
/// <code>
/// Root (the window)
/// ├─ BackgroundHost   shape + shadow
/// └─ Area             the island column: takes the hover, clipped to the shape
///    ├─ PillLayer     artwork + bars, shown in the compact pill
///    └─ ExpandedLayer the player, shown while open
/// </code>
/// </summary>
internal sealed class IslandView
{
    private readonly Image _pillArtwork;

    public IslandView(IslandTheme theme, MediaService media, DispatcherQueue queue)
    {
        BackgroundHost = new Grid { IsHitTestVisible = false };

        _pillArtwork = new Image { Stretch = Stretch.UniformToFill };
        var pillArtwork = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(6),
            Background = theme.Surface.ToBrush(),
            Child = _pillArtwork,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var barsHost = new Border
        {
            Width = AudioBars.Width,
            Height = 14,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Bars = new AudioBars(barsHost);
        Bars.SetColor(theme.Accent.ToColor());

        PillLayer = new Grid
        {
            Padding = new Thickness(5, 0, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Children = { pillArtwork, barsHost },
        };

        Player = new MediaPlayerView(media, theme, queue);
        ExpandedLayer = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Children = { Player.Root },
        };

        // A transparent (not null) background, so the whole column takes the pointer.
        Area = new Grid
        {
            Background = Ui.Clear(),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Children = { PillLayer, ExpandedLayer },
        };

        Root = new Grid { Children = { BackgroundHost, Area } };
    }

    public Grid Root { get; }
    public Grid BackgroundHost { get; }
    public Grid Area { get; }
    public Grid PillLayer { get; }
    public Grid ExpandedLayer { get; }
    public MediaPlayerView Player { get; }
    public AudioBars Bars { get; }

    public void ApplyLayout(IslandLayout layout)
    {
        Place(Area, layout.Area);
        Place(PillLayer, layout.CompactPill);
        Place(ExpandedLayer, layout.Expanded);
    }

    public void SetArtwork(ImageSource? source)
    {
        _pillArtwork.Source = source;
        Player.SetArtwork(source);
    }

    private static void Place(FrameworkElement element, DipRect rect)
    {
        element.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        element.Width = rect.Width;
        element.Height = rect.Height;
    }
}
