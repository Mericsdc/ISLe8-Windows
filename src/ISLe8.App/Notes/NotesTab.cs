using ISLe8.Core.Notes;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Core;

namespace ISLe8.Notes;

/// <summary>
/// The Notes tab (NotesViews.swift, NotesTab): a line to write in, and the notes as cards, two to
/// four across as the island's width allows. Clicking a card copies it; hovering shows edit and
/// delete, and the right-click menu has all three.
/// </summary>
internal sealed class NotesTab
{
    private const double MinimumCardWidth = 200;
    private const double Gap = 8;

    private readonly NotesStore _notes;
    private readonly IslandTheme _theme;
    private readonly DispatcherQueue _queue;
    private readonly Action _keyboard;
    private readonly TextBox _box;
    private readonly FontIcon _boxIcon;
    private readonly Button _cancel;
    private readonly Button _commit;
    private readonly FontIcon _commitIcon;
    private readonly Grid _cards = new() { ColumnSpacing = Gap, RowSpacing = Gap };
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _empty;
    private Guid? _editing;
    private Guid? _copied;
    private int _columns = 2;

    /// <param name="keyboard">Typing needs the island to take the keyboard (the Mac's requestKeyboard).</param>
    public NotesTab(NotesStore notes, IslandTheme theme, DispatcherQueue queue, Action keyboard)
    {
        _notes = notes;
        _theme = theme;
        _queue = queue;
        _keyboard = keyboard;

        _boxIcon = new FontIcon { Glyph = Glyphs.Notes, FontSize = 11, Foreground = theme.Accent.ToBrush(), VerticalAlignment = VerticalAlignment.Center };
        _box = new TextBox
        {
            PlaceholderText = L.Get("Notes_Placeholder"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            MaxHeight = 3 * 18 + 8,
            MinHeight = 24,
            Padding = new Thickness(2, 3, 2, 3),
            BorderThickness = new Thickness(0),
            Background = Ui.Clear(),
            VerticalAlignment = VerticalAlignment.Center,
        };
        // The plain look of the Mac's text field: no box of its own, the row is the field.
        foreach (var key in new[] { "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrushFocused", "TextControlBorderBrushPointerOver" })
        {
            _box.Resources[key] = Ui.Clear();
        }
        _box.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => BeginTyping()), handledEventsToo: true);
        _box.TextChanged += (_, _) => UpdateButtons();
        // Before the box: it takes Enter for a new line itself, and only Shift+Enter should.
        _box.PreviewKeyDown += OnKey;

        _cancel = Ui.IconButton(Glyphs.Close, 22, 11, L.Get("Notes_Cancel"), Cancel, out _);
        _commit = Ui.IconButton(Glyphs.Add, 22, 11, L.Get("Notes_Add"), Commit, out _commitIcon);
        var editor = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition(),
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        editor.Children.Add(_boxIcon);
        Grid.SetColumn(_box, 1);
        editor.Children.Add(_box);
        Grid.SetColumn(_cancel, 2);
        editor.Children.Add(_cancel);
        Grid.SetColumn(_commit, 3);
        editor.Children.Add(_commit);
        var editorBox = new Border
        {
            Padding = new Thickness(10, 3, 10, 3),
            CornerRadius = new CornerRadius(9),
            Background = Ui.White(0x14),
            Child = editor,
        };

        _empty = Ui.Text(11, alpha: 0x73);
        _empty.Text = L.Get("Notes_Empty");
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.MaxLines = 2;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _scroll = new ScrollViewer
        {
            Content = _cards,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        Root = new Grid
        {
            RowSpacing = 8,
            RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() },
            Children = { editorBox },
        };
        Grid.SetRow(_empty, 1);
        Grid.SetRow(_scroll, 1);
        Root.Children.Add(_empty);
        Root.Children.Add(_scroll);
        Root.SizeChanged += (_, e) =>
        {
            // GridItem(.adaptive(minimum: 200)): as many columns as fit at 200 or more.
            var columns = Math.Max(1, (int)((e.NewSize.Width + Gap) / (MinimumCardWidth + Gap)));
            if (columns == _columns) return;
            _columns = columns;
            Fill();
        };
        notes.Changed += Fill;
        UpdateButtons();
        Fill();
    }

    public Grid Root { get; }

    public void Detach() => _notes.Changed -= Fill;

    // MARK: Writing

    /// <summary>The island only takes the keyboard when asked, so typing works without bringing ISLe8 to the front.</summary>
    private void BeginTyping()
    {
        _keyboard();
        _box.Focus(FocusState.Programmatic);
    }

    private void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && !InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            Commit();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            Cancel();
        }
    }

    private void Commit()
    {
        if (_editing is { } id) _notes.Update(id, _box.Text);
        else _notes.Add(_box.Text);
        _editing = null;
        _box.Text = "";
        UpdateButtons();
    }

    private void Cancel()
    {
        _editing = null;
        _box.Text = "";
        UpdateButtons();
        Fill();
    }

    private void StartEditing(Note note)
    {
        _editing = note.Id;
        _box.Text = note.Text;
        _box.SelectionStart = _box.Text.Length;
        UpdateButtons();
        Fill();
        BeginTyping();
    }

    private void UpdateButtons()
    {
        var editing = _editing is not null;
        _boxIcon.Glyph = editing ? Glyphs.Edit : Glyphs.Notes;
        _box.PlaceholderText = L.Get(editing ? "Notes_EditPlaceholder" : "Notes_Placeholder");
        _cancel.Visibility = editing || _box.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _commitIcon.Glyph = editing ? Glyphs.Check : Glyphs.Add;
        ToolTipService.SetToolTip(_commit, L.Get(editing ? "Notes_Save" : "Notes_Add"));
    }

    // MARK: Cards

    private void Fill()
    {
        var notes = _notes.Notes;
        _empty.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = notes.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _cards.Children.Clear();
        _cards.ColumnDefinitions.Clear();
        _cards.RowDefinitions.Clear();
        for (var c = 0; c < _columns; c++) _cards.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < notes.Count; i++)
        {
            if (i % _columns == 0) _cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var card = Card(notes[i]);
            Grid.SetRow(card, i / _columns);
            Grid.SetColumn(card, i % _columns);
            _cards.Children.Add(card);
        }
    }

    private FrameworkElement Card(Note note)
    {
        var text = Ui.Text(12);
        text.Text = note.Text;
        text.TextWrapping = TextWrapping.Wrap;
        text.MaxLines = 3;

        FrameworkElement footer;
        if (_copied == note.Id)
        {
            var copied = Ui.Text(9.5, semibold: true);
            copied.Text = L.Get("Notes_Copied");
            copied.Foreground = _theme.Accent.ToBrush();
            footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                Children = { new FontIcon { Glyph = Glyphs.Check, FontSize = 9, Foreground = _theme.Accent.ToBrush() }, copied },
            };
        }
        else
        {
            var when = Ui.Text(9.5, alpha: 0x66);
            when.Text = Ago(note.Updated);
            footer = when;
        }

        var content = new Grid { RowSpacing = 4, RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition(), new RowDefinition { Height = GridLength.Auto } } };
        content.Children.Add(text);
        Grid.SetRow(footer, 2);
        content.Children.Add(footer);

        var tile = Tiles.Tile(_theme, content, new Thickness(10));
        tile.CornerRadius = new CornerRadius(10);
        tile.MinHeight = 66;
        if (_editing == note.Id)
        {
            tile.BorderBrush = _theme.Accent.ToBrush();
            tile.BorderThickness = new Thickness(1.5);
        }

        var edit = Ui.IconButton(Glyphs.Edit, 22, 11, L.Get("Notes_Edit"), () => StartEditing(note), out _);
        var delete = Ui.IconButton(Glyphs.Delete, 22, 11, L.Get("Notes_Delete"), () => _notes.Delete(note.Id), out _);
        edit.Background = delete.Background = Ui.White(0x26);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            Children = { edit, delete },
        };

        var card = new Grid { Background = Ui.Clear(), Children = { tile, actions } };
        card.PointerEntered += (_, _) => actions.Visibility = Visibility.Visible;
        card.PointerExited += (_, _) => actions.Visibility = Visibility.Collapsed;
        card.Tapped += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && IsInside(source, actions)) return;
            Copy(note);
        };
        ToolTipService.SetToolTip(card, L.Get("Notes_ClickToCopy"));

        var menu = new MenuFlyout();
        foreach (var (title, action) in new (string, Action)[] { (L.Get("Notes_Copy"), () => Copy(note)), (L.Get("Notes_Edit"), () => StartEditing(note)), (L.Get("Notes_Delete"), () => _notes.Delete(note.Id)) })
        {
            var item = new MenuFlyoutItem { Text = title };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        card.ContextFlyout = menu;
        return card;
    }

    /// <summary>Copies it, and says "Kopyalandı" for 1.2 seconds.</summary>
    private void Copy(Note note)
    {
        var package = new DataPackage();
        package.SetText(note.Text);
        Clipboard.SetContent(package);
        _copied = note.Id;
        Fill();
        var timer = _queue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1.2);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (_copied == note.Id)
            {
                _copied = null;
                Fill();
            }
        };
        timer.Start();
    }

    private static string Ago(DateTimeOffset when)
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

    private static bool IsInside(DependencyObject element, DependencyObject container)
    {
        for (var current = element; current is not null; current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current))
        {
            if (current == container) return true;
        }
        return false;
    }
}
