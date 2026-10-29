using System.Diagnostics;
using ISLe8.Core.Clipboard;
using ISLe8.Core.Notes;
using ISLe8.Core.Settings;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using Windows.UI.Core;

namespace ISLe8.ClipboardTab;

/// <summary>
/// The Clipboard tab (ClipboardViews.swift): a search field, and the history newest first with the
/// pinned on top. A click copies an entry back, Alt and a click pastes it into the window you were
/// in; hover shows pin and delete, the right-click menu the rest.
/// </summary>
internal sealed class ClipboardTabView
{
    private static readonly Color Green = Color.FromArgb(255, 0x30, 0xD1, 0x58);

    private readonly ClipboardService _clipboard;
    private readonly AppSettings _settings;
    private readonly IslandTheme _theme;
    private readonly Action _keyboard;
    private readonly Action _close;
    private readonly TextBox _search;
    private readonly Button _clear;
    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _notice = new() { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    /// <param name="keyboard">The search field needs typing: the island takes the keyboard.</param>
    /// <param name="close">Pasting closes the island so the window behind it gets the keystroke.</param>
    public ClipboardTabView(ClipboardService clipboard, AppSettings settings, IslandTheme theme, Action keyboard, Action close)
    {
        _clipboard = clipboard;
        _settings = settings;
        _theme = theme;
        _keyboard = keyboard;
        _close = close;

        _search = new TextBox
        {
            PlaceholderText = L.Get("Clipboard_Search"),
            FontSize = 12,
            MinHeight = 24,
            Padding = new Thickness(2, 3, 2, 3),
            BorderThickness = new Thickness(0),
            Background = Ui.Clear(),
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var key in new[] { "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrushFocused", "TextControlBorderBrushPointerOver" })
        {
            _search.Resources[key] = Ui.Clear();
        }
        _search.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => FocusSearch()), handledEventsToo: true);
        _search.TextChanged += (_, _) => clipboard.Query = _search.Text;
        _search.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Escape) return;
            e.Handled = true;
            close();
        };
        var field = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            Children = { new FontIcon { Glyph = Glyphs.Search, FontSize = 11, Foreground = Ui.White(0x80), VerticalAlignment = VerticalAlignment.Center } },
        };
        Grid.SetColumn(_search, 1);
        field.Children.Add(_search);
        var capsule = new Border { Height = 26, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(13), Background = Ui.White(0x14), Child = field };

        _clear = Ui.IconButton(Glyphs.Delete, 24, 12, L.Get("Clipboard_ClearPinnedStay"), clipboard.Clear, out _);
        var top = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            Children = { capsule },
        };
        Grid.SetColumn(_clear, 1);
        top.Children.Add(_clear);

        _scroll = new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Root = new Grid { RowSpacing = 8, RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() }, Children = { top } };
        Grid.SetRow(_scroll, 1);
        Grid.SetRow(_notice, 1);
        Root.Children.Add(_scroll);
        Root.Children.Add(_notice);

        clipboard.Changed += Fill;
        Fill();
    }

    public Grid Root { get; }

    public void Detach() => _clipboard.Changed -= Fill;

    /// <summary>The shortcut opened the tab: the search field gets the keyboard.</summary>
    public void FocusSearch()
    {
        _keyboard();
        _search.Focus(FocusState.Programmatic);
    }

    private void Fill()
    {
        var items = _clipboard.Items;
        _clear.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _rows.Children.Clear();
        (string Title, string Detail)? notice = !_clipboard.History.IsAvailable ? ("Clipboard_Unavailable", "Clipboard_UnavailableDetail")
            : !_settings.RecordClipboard && items.Count == 0 ? ("Clipboard_Off", "Clipboard_OffDetail")
            : items.Count == 0 && _clipboard.Query.Length == 0 ? ("Clipboard_Empty", "Clipboard_EmptyDetail")
            : items.Count == 0 ? ("Clipboard_NoResults", "Clipboard_NoResultsDetail")
            : null;
        _notice.Children.Clear();
        _notice.Visibility = notice is null ? Visibility.Collapsed : Visibility.Visible;
        _scroll.Visibility = notice is null ? Visibility.Visible : Visibility.Collapsed;
        if (notice is { } n)
        {
            var title = Ui.Text(12, semibold: true);
            title.Text = L.Get(n.Title);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            var detail = Ui.Text(11, alpha: 0x80);
            detail.Text = L.Get(n.Detail);
            detail.TextWrapping = TextWrapping.Wrap;
            detail.TextAlignment = TextAlignment.Center;
            _notice.Children.Add(title);
            _notice.Children.Add(detail);
            return;
        }
        foreach (var entry in items) _rows.Children.Add(Row(entry));
    }

    private FrameworkElement Row(ClipboardEntry entry)
    {
        var title = Ui.Text(12);
        title.Text = entry.Title ?? L.Get("Clipboard_Image");
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxLines = 2;
        var when = Ui.Text(10, alpha: 0x73);
        when.Text = Ago(entry.CopiedAt);
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (entry.IsPinned) meta.Children.Add(new FontIcon { Glyph = Glyphs.Pinned, FontSize = 8, Foreground = Ui.White(0x73) });
        meta.Children.Add(when);
        if (entry.SourceApp is { } app)
        {
            var source = Ui.Text(10, alpha: 0x59);
            source.Text = "· " + Path.GetFileNameWithoutExtension(app);
            meta.Children.Add(source);
        }
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { title, meta } };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        actions.Children.Add(Ui.IconButton(entry.IsPinned ? Glyphs.Unpin : Glyphs.Pin, 24, 11, L.Get(entry.IsPinned ? "Clipboard_Unpin" : "Clipboard_Pin"), () => _clipboard.TogglePin(entry), out _));
        actions.Children.Add(Ui.IconButton(Glyphs.Close, 24, 11, L.Get("Clipboard_Delete"), () => _clipboard.Delete(entry), out _));
        FrameworkElement trailing = _clipboard.LastCopiedId == entry.Id
            ? new FontIcon { Glyph = Glyphs.CheckCircle, FontSize = 14, Foreground = new SolidColorBrush(Green), VerticalAlignment = VerticalAlignment.Center }
            : actions;

        var grid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(34) }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
        };
        grid.Children.Add(Leading(entry));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(trailing, 2);
        grid.Children.Add(trailing);

        var row = new Border { Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(10), Background = Ui.Clear(), Child = grid };
        row.PointerEntered += (_, _) =>
        {
            row.Background = Ui.White(0x14);
            actions.Visibility = Visibility.Visible;
        };
        row.PointerExited += (_, _) =>
        {
            row.Background = Ui.Clear();
            actions.Visibility = Visibility.Collapsed;
        };
        row.Tapped += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && Inside(source, actions)) return;
            var alt = InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down);
            _ = alt ? _clipboard.Paste(entry, _close) : _clipboard.Copy(entry);
        };
        ToolTipService.SetToolTip(row, L.Get("Clipboard_Help"));
        row.ContextFlyout = Menu(entry);
        return row;
    }

    private MenuFlyout Menu(ClipboardEntry entry)
    {
        var menu = new MenuFlyout();
        void Add(string key, Action action)
        {
            var item = new MenuFlyoutItem { Text = L.Get(key) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Add("Clipboard_Copy", () => _ = _clipboard.Copy(entry));
        Add("Clipboard_Paste", () => _ = _clipboard.Paste(entry, _close));
        Add(entry.IsPinned ? "Clipboard_Unpin" : "Clipboard_Pin", () => _clipboard.TogglePin(entry));
        if (entry.Kind == ClipboardKind.Url && Uri.TryCreate(entry.Text.Trim(), UriKind.Absolute, out var url))
        {
            Add("Clipboard_OpenInBrowser", () => _ = Windows.System.Launcher.LaunchUriAsync(url));
        }
        if (entry.EmailAddress is { } email)
        {
            Add("Clipboard_WriteEmail", () => _ = Windows.System.Launcher.LaunchUriAsync(new Uri("mailto:" + email)));
        }
        if (entry.Kind == ClipboardKind.Files)
        {
            Add("Clipboard_ShowInFolder", () => ShowInFolder(entry.Text.Split('\n')[0]));
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Clipboard_Delete", () => _clipboard.Delete(entry));
        return menu;
    }

    private FrameworkElement Leading(ClipboardEntry entry)
    {
        if (entry.Kind == ClipboardKind.Image && File.Exists(_clipboard.ImagePath(entry)))
        {
            return new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(6),
                Child = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    Source = new BitmapImage(new Uri(_clipboard.ImagePath(entry))) { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = 34 },
                },
            };
        }
        if (entry.Color is { } color)
        {
            return new Border
            {
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(255, color.R, color.G, color.B)),
                BorderBrush = Ui.White(0x33),
                BorderThickness = new Thickness(1),
            };
        }
        var glyph = entry.Kind switch
        {
            ClipboardKind.RichText => Glyphs.RichText,
            ClipboardKind.Url => Glyphs.Link,
            ClipboardKind.Image => Glyphs.Photo,
            ClipboardKind.Files => Glyphs.Files,
            _ => entry.EmailAddress is null ? Glyphs.TextLines : Glyphs.Mail,
        };
        return new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(8),
            Background = Ui.White(0x0F),
            Child = new FontIcon { Glyph = glyph, FontSize = 13, Foreground = Ui.White(0xB3) },
        };
    }

    /// <summary>The Mac's "Finder'da göster": Explorer with the file selected.</summary>
    private static void ShowInFolder(string path)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Core.Diagnostics.Log.Error(e, "show in folder");
        }
    }

    /// <summary>"5 dakika önce": also the limits card's last reading.</summary>
    internal static string Ago(DateTimeOffset when)
    {
        var (unit, count) = RelativeTime.Of(when, DateTimeOffset.Now);
        return unit switch
        {
            RelativeTime.Unit.Now => L.Get("Ago_Now"),
            RelativeTime.Unit.Yesterday => L.Get("Ago_Yesterday"),
            RelativeTime.Unit.Minutes => L.Format(count == 1 ? "Ago_Minute" : "Ago_Minutes", count),
            RelativeTime.Unit.Hours => L.Format(count == 1 ? "Ago_Hour" : "Ago_Hours", count),
            RelativeTime.Unit.Days => L.Format("Ago_Days", count),
            RelativeTime.Unit.Weeks => L.Format(count == 1 ? "Ago_Week" : "Ago_Weeks", count),
            RelativeTime.Unit.Months => L.Format(count == 1 ? "Ago_Month" : "Ago_Months", count),
            _ => L.Format(count == 1 ? "Ago_Year" : "Ago_Years", count),
        };
    }

    private static bool Inside(DependencyObject element, DependencyObject container)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current == container) return true;
        }
        return false;
    }
}
