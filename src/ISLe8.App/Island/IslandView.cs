using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Settings;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.Media;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Island;

/// <summary>
/// The XAML side of the island. The tree is laid out when the monitor, DPI, layout or tab
/// height changes, never while the shape animates: the compositor moves the shape and clips the
/// content (see <see cref="IslandSurface"/>).
/// <code>
/// Root (the window)
/// ├─ BackgroundHost   shape + shadow
/// └─ Area             the island column: takes the hover, clipped to the shape
///    ├─ PillLayer     artwork + bars, shown in the wide pill
///    └─ ExpandedLayer the open island (IslandView.swift, ExpandedView)
///       ├─ Header     chips · pin, settings
///       ├─ content    one view per tab; the shown one only
///       └─ Dock       the tabs
/// </code>
/// </summary>
internal sealed class IslandView
{
    private readonly Image _pillArtwork;
    private readonly RowDefinition _contentRow = new();
    private readonly Dictionary<IslandTab, FrameworkElement> _tabs = [];

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

        // Home holds W0's player until step 4 gives it columns; minimal Home is too short for it.
        Player = new MediaPlayerView(media, theme, queue);
        Player.Root.Width = 560;
        Player.Root.Height = 160;
        Player.Root.HorizontalAlignment = HorizontalAlignment.Center;
        Player.Root.VerticalAlignment = VerticalAlignment.Center;
        Strip = new MediaStrip(media, theme);
        _tabs[IslandTab.Home] = new Grid { Children = { Player.Root, Strip.Root } };
        foreach (var tab in new[] { IslandTab.Shelf, IslandTab.Clipboard, IslandTab.Agents, IslandTab.Devices })
        {
            _tabs[tab] = Placeholder(tab);
        }

        var content = new Grid { Padding = new Thickness(IslandSizes.ContentInset, IslandSizes.ContentTop, IslandSizes.ContentInset, 0) };
        foreach (var view in _tabs.Values)
        {
            view.Visibility = Visibility.Collapsed;
            content.Children.Add(view);
        }

        Header = new HeaderBar();
        Dock = new TabDock(theme);
        Dock.Root.Margin = new Thickness(0, IslandSizes.DockTop, 0, IslandSizes.DockBottom);
        Dock.Root.VerticalAlignment = VerticalAlignment.Top;

        var open = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(IslandSizes.HeaderHeight) },
                _contentRow,
                new RowDefinition { Height = new GridLength(IslandSizes.DockArea) },
            },
        };
        open.Children.Add(Header.Root);
        Grid.SetRow(content, 1);
        open.Children.Add(content);
        Grid.SetRow(Dock.Root, 2);
        open.Children.Add(Dock.Root);

        ExpandedLayer = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Children = { open },
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
    public HeaderBar Header { get; }
    public TabDock Dock { get; }
    public MediaPlayerView Player { get; }
    public MediaStrip Strip { get; }
    public AudioBars Bars { get; }

    public void ApplyLayout(IslandLayout layout, DipRect open, HomeStyle style, IslandTab tab)
    {
        Place(Area, layout.Area);
        Place(PillLayer, layout.CompactPill);
        SetOpen(open, style, tab);
        var minimal = style == HomeStyle.Minimal;
        Player.Root.Visibility = minimal ? Visibility.Collapsed : Visibility.Visible;
        Strip.Root.Visibility = minimal ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The open island's outline for <paramref name="tab"/>: the dock sits under that tab's content.</summary>
    public void SetOpen(DipRect open, HomeStyle style, IslandTab tab)
    {
        Place(ExpandedLayer, open);
        _contentRow.Height = new GridLength(IslandSizes.ContentTop + IslandSizes.ContentHeight(style, tab));
    }

    public FrameworkElement TabView(IslandTab tab) => _tabs[tab];

    /// <summary>Shows <paramref name="tab"/> at once, without a crossfade.</summary>
    public void ShowTab(IslandTab tab)
    {
        foreach (var (key, view) in _tabs) view.Visibility = key == tab ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetArtwork(ImageSource? source)
    {
        _pillArtwork.Source = source;
        Player.SetArtwork(source);
        Strip.SetArtwork(source);
    }

    /// <summary>What a tab that arrives later in W1–W5 shows until then.</summary>
    private static FrameworkElement Placeholder(IslandTab tab) => new StackPanel
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Spacing = 6,
        Children =
        {
            new FontIcon { Glyph = TabDock.Glyph(tab), FontSize = 22, Foreground = Ui.White(0x59) },
            new TextBlock
            {
                Text = TabDock.Title(tab),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ui.White(0xCC),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
            new TextBlock
            {
                Text = L.Get("Tab_ComingSoon"),
                FontSize = 12,
                Foreground = Ui.White(0x80),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        },
    };

    private static void Place(FrameworkElement element, DipRect rect)
    {
        element.Margin = new Thickness(rect.X, rect.Y, 0, 0);
        element.Width = rect.Width;
        element.Height = rect.Height;
    }
}
