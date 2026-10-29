using ISLe8.Core.Island;
using ISLe8.Core.Settings;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.Media;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Island;

/// <summary>
/// The Home tab (HomeView.swift): the columns chosen in Settings › Layout, side by side at full
/// height, each as wide as <see cref="HomeColumns"/> says. Cards on the dashboard; in the compact
/// and minimal strips flat columns split by hairlines, the player in the middle. Built again only
/// when the columns, the layout or the width change.
/// </summary>
internal sealed class HomeView(MediaService media, IslandTheme theme, DispatcherQueue queue)
{
    private ImageSource? _artwork;
    private string? _built;
    private bool _active;

    public Grid Root { get; } = new();

    /// <summary>The media column, if Home has one.</summary>
    public MediaColumn? Media { get; private set; }

    public void Build(HomeStyle style, IReadOnlyList<string> columns, double width)
    {
        var key = $"{style}|{string.Join(',', columns)}|{width}";
        if (key == _built) return;
        _built = key;

        Media?.SetActive(false);
        Media = null;
        Root.Children.Clear();
        Root.ColumnDefinitions.Clear();
        if (columns.Count == 0)
        {
            var empty = Ui.Text(12, alpha: 0x80);
            empty.Text = L.Get("Home_Empty");
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.VerticalAlignment = VerticalAlignment.Center;
            Root.Children.Add(empty);
            return;
        }

        // Gaps are columns of their own: empty between cards, a hairline in the strips.
        var widths = HomeColumns.Widths(style, columns, width);
        for (var i = 0; i < columns.Count; i++)
        {
            if (i > 0)
            {
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HomeColumns.Spacing(style)) });
                if (style != HomeStyle.Dashboard)
                {
                    var hairline = new Border { Width = 1, Background = Ui.White(0x14), Margin = new Thickness(0, 4, 0, 4) };
                    Grid.SetColumn(hairline, Root.ColumnDefinitions.Count - 1);
                    Root.Children.Add(hairline);
                }
            }
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[i]) });
            FrameworkElement view;
            if (columns[i] == "media")
            {
                Media = new MediaColumn(media, theme, style, queue);
                Media.SetArtwork(_artwork);
                view = Media.Root;
            }
            else
            {
                view = Placeholder(columns[i], style);
            }
            Grid.SetColumn(view, Root.ColumnDefinitions.Count - 1);
            Root.Children.Add(view);
        }
        Media?.SetActive(_active);
    }

    /// <summary>True while Home can be seen.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        Media?.SetActive(active);
    }

    public void SetArtwork(ImageSource? source)
    {
        _artwork = source;
        Media?.SetArtwork(source);
    }

    /// <summary>A column's name in Settings and on its placeholder.</summary>
    public static string Title(string id) => L.Get(id switch
    {
        "calendar" => "Home_Calendar",
        "weather" => "Home_Weather",
        "timer" => "Home_Timer",
        _ => "Home_Media",
    });

    public static string Glyph(string id) => id switch
    {
        "calendar" => Glyphs.Calendar,
        "weather" => Glyphs.Weather,
        "timer" => Glyphs.Timer,
        _ => Glyphs.Music,
    };

    /// <summary>What a column that arrives later in W1 shows until then.</summary>
    private FrameworkElement Placeholder(string id, HomeStyle style)
    {
        var title = Ui.Text(style == HomeStyle.Minimal ? 12 : 13, alpha: 0xCC, semibold: true);
        title.Text = Title(id);
        var note = Ui.Text(style == HomeStyle.Minimal ? 10 : 11, alpha: 0x80);
        note.Text = L.Get("Tab_ComingSoon");
        note.TextWrapping = TextWrapping.Wrap;
        note.MaxLines = 2;
        var icon = new FontIcon { Glyph = Glyph(id), FontSize = style switch { HomeStyle.Minimal => 16, HomeStyle.Compact => 20, _ => 22 }, Foreground = Ui.White(0x59) };

        if (style != HomeStyle.Dashboard)
        {
            // The strips are flat and short: the icon beside the text, which trims to what is left.
            note.MaxLines = 1;
            note.TextWrapping = TextWrapping.NoWrap;
            var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, note } };
            Grid.SetColumn(text, 1);
            icon.VerticalAlignment = VerticalAlignment.Center;
            return new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
                Children = { icon, text },
            };
        }

        title.HorizontalAlignment = note.HorizontalAlignment = HorizontalAlignment.Center;
        note.TextAlignment = TextAlignment.Center;
        var content = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { icon, title, note },
        };
        return Tiles.Tile(theme, content, new Thickness(14));
    }
}
