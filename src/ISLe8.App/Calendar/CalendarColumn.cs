using System.Globalization;
using ISLe8.Core.Calendar;
using ISLe8.Core.Settings;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.Timer;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace ISLe8.Calendar;

/// <summary>
/// Home's calendar column (CalendarViews.swift, CalendarColumn). Wide: the month with a dot on days
/// that have events, and the events of the day picked in it (today to begin with); "+" adds an
/// event to that day in a line: "14:30 Toplantı". Compact: this week in the same look; a tap on a
/// day puts its events in a line underneath. Minimal: the next thing coming up. Without a calendar
/// link it is a plain calendar from the computer's own date: the Mac asks for calendar access
/// first, but a month to look at needs nothing (PORTING.md §3.6).
/// </summary>
internal sealed class CalendarColumn
{
    private readonly CalendarService _calendar;
    private readonly IslandTheme _theme;
    private readonly HomeStyle _style;
    private readonly Action _keyboard;
    private readonly Border _host = new();
    private readonly SecondTicker _minute;
    private DateOnly _month;
    private DateOnly _week;
    private DateOnly _selected;
    private bool _adding;
    private string _draft = "";
    private bool _active;

    /// <param name="keyboard">The add line needs typing: the island takes the keyboard until it closes.</param>
    public CalendarColumn(CalendarService calendar, IslandTheme theme, HomeStyle style, DispatcherQueue queue, Action keyboard)
    {
        _calendar = calendar;
        _theme = theme;
        _style = style;
        _keyboard = keyboard;
        GoToToday();
        if (style == HomeStyle.Dashboard)
        {
            _host.Padding = new Thickness(12);
            Root = Tiles.Tile(theme, _host, new Thickness(0));
        }
        else
        {
            Root = _host;
        }
        // The minimal strip's "in 5 min" changes by the minute, and only while it is seen.
        _minute = new SecondTicker(queue, Build, TimeSpan.FromMinutes(1));
        calendar.Changed += Build;
        Build();
    }

    public FrameworkElement Root { get; }

    public void SetActive(bool active)
    {
        _active = active;
        if (active)
        {
            // Back on Home after midnight: today moves along.
            if (_selected < Today && !_adding) GoToToday();
            _ = _calendar.RefreshIfStale();
            Build();
        }
        _minute.Run(active && _style == HomeStyle.Minimal && _calendar.HasFeeds);
    }

    public void Detach()
    {
        _calendar.Changed -= Build;
        _minute.Run(false);
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    private static CultureInfo Culture => L.Culture;
    private static DayOfWeek FirstDay => Culture.DateTimeFormat.FirstDayOfWeek;

    private void GoToToday()
    {
        _selected = Today;
        _month = new DateOnly(Today.Year, Today.Month, 1);
        _week = CalendarGrid.WeekStart(Today, FirstDay);
    }

    private void Build()
    {
        _host.Child = _style switch
        {
            HomeStyle.Compact => WeekStrip(),
            HomeStyle.Minimal => Minimal(),
            _ => Full(),
        };
    }

    // MARK: Layouts

    /// <summary>The minimal strip: the next thing coming up, in one line.</summary>
    private FrameworkElement Minimal()
    {
        var row = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center, ColumnDefinitions = { Auto(), Star() } };
        var title = Ui.Text(12, semibold: true);
        var when = Ui.Text(10, alpha: 0x8C);
        if (_calendar.UpcomingAt(DateTimeOffset.Now).FirstOrDefault() is { } item)
        {
            row.Children.Add(new Rectangle { Width = 3, Height = 28, RadiusX = 1.5, RadiusY = 1.5, Fill = new SolidColorBrush(Hex(item.ColorHex)) });
            title.Text = item.Title;
            when.Text = When(item, DateTimeOffset.Now);
        }
        else
        {
            row.Children.Add(new FontIcon { Glyph = Glyphs.Calendar, FontSize = 15, Foreground = _theme.Accent.ToBrush(), VerticalAlignment = VerticalAlignment.Center });
            var today = DateTime.Now;
            title.Text = $"{today.Day} {Culture.DateTimeFormat.MonthNames[today.Month - 1]} {Culture.DateTimeFormat.DayNames[(int)today.DayOfWeek]}";
            when.Text = L.Get("Calendar_NoEvents");
        }
        row.Children.Add(At(new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, when } }, 1));
        return row;
    }

    /// <summary>Compact: the week in the month grid's look, under the month it falls in, and the picked day's events in a line below.</summary>
    private FrameworkElement WeekStrip()
    {
        var stack = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var middle = _week.AddDays(3);
        stack.Children.Add(Header(middle, 15, ShiftWeek, "Calendar_PrevWeek", "Calendar_NextWeek"));

        var days = Enumerable.Range(0, 7).Select(i => _week.AddDays(i)).ToList();
        var dots = _calendar.EventDays(days[0], days[^1]);
        var grid = new Grid { RowSpacing = 4 };
        for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var symbols = CalendarGrid.WeekdaySymbols(Culture);
        for (var i = 0; i < 7; i++)
        {
            grid.Children.Add(At(WeekdaySymbol(symbols[i], 10.5), i));
            var day = days[i];
            var cell = DayCell(day, day.Month == middle.Month, dots.TryGetValue(day, out var dot) ? dot : null, 28);
            var summary = Summary(_calendar.On(day));
            if (summary.Length > 0) ToolTipService.SetToolTip(cell, summary);
            Grid.SetRow(cell, 1);
            grid.Children.Add(At(cell, i));
        }
        stack.Children.Add(grid);

        var line = Ui.Text(10, alpha: 0x99);
        line.Text = DaySummary();
        line.Margin = new Thickness(0, 3, 0, 0);
        stack.Children.Add(line);
        return stack;
    }

    /// <summary>Wide: the month and the picked day.</summary>
    private FrameworkElement Full()
    {
        var layout = new Grid
        {
            RowSpacing = 8,
            RowDefinitions = { AutoRow(), AutoRow(), AutoRow(), new RowDefinition() },
        };
        layout.Children.Add(Header(_month, 15, ShiftMonth, "Calendar_PrevMonth", "Calendar_NextMonth"));
        layout.Children.Add(Row(MonthGrid(), 1));
        layout.Children.Add(Row(new Border { Height = 1, Background = Ui.White(0x14) }, 2));
        layout.Children.Add(Row(DayList(), 3));
        return layout;
    }

    private FrameworkElement MonthGrid()
    {
        var days = CalendarGrid.MonthDays(_month, FirstDay);
        var dots = _calendar.EventDays(days[0], days[^1]);
        var grid = new Grid { RowSpacing = 4 };
        for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        var weeks = days.Count / 7;
        for (var r = 0; r <= weeks; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var symbols = CalendarGrid.WeekdaySymbols(Culture);
        for (var i = 0; i < 7; i++)
        {
            var symbol = WeekdaySymbol(symbols[i], 9);
            symbol.Margin = new Thickness(0, 0, 0, 2);
            grid.Children.Add(At(symbol, i));
        }
        for (var index = 0; index < days.Count; index++)
        {
            var day = days[index];
            var cell = DayCell(day, day.Month == _month.Month, dots.TryGetValue(day, out var dot) ? dot : null, 22);
            Grid.SetRow(cell, index / 7 + 1);
            grid.Children.Add(At(cell, index % 7));
        }
        return grid;
    }

    private FrameworkElement DayList()
    {
        var title = Ui.Text(12, semibold: true);
        title.Text = DayTitle();
        title.VerticalAlignment = VerticalAlignment.Center;
        var add = Ui.IconButton(_adding ? Glyphs.Close : Glyphs.Add, 22, 11, L.Get(_adding ? "Calendar_Cancel" : "Calendar_AddEvent"), () =>
        {
            _adding = !_adding;
            if (_adding) _keyboard();
            Build();
        }, out _);
        add.Background = Ui.White(0x14);
        var heading = new Grid { ColumnDefinitions = { Star(), Auto() } };
        heading.Children.Add(title);
        heading.Children.Add(At(add, 1));

        var list = new StackPanel { Spacing = 5 };
        var stack = new Grid { RowSpacing = 5, RowDefinitions = { AutoRow(), AutoRow(), new RowDefinition() } };
        stack.Children.Add(heading);
        if (_adding)
        {
            var box = new TextBox
            {
                PlaceholderText = L.Get("Calendar_AddPlaceholder"),
                Text = _draft,
                FontSize = 12,
                Height = 24,
                MinHeight = 24,
                Padding = new Thickness(8, 2, 8, 2),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(0),
                Background = Ui.White(0x14),
            };
            box.TextChanged += (_, _) => _draft = box.Text;
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    e.Handled = true;
                    AddDraft();
                }
                else if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    e.Handled = true;
                    _adding = false;
                    Build();
                }
            };
            box.Loaded += (_, _) => box.Focus(FocusState.Programmatic);
            stack.Children.Add(Row(box, 1));
        }
        var events = _calendar.On(_selected);
        if (events.Count == 0 && !_adding)
        {
            var empty = Ui.Text(11, alpha: 0x73);
            empty.Text = L.Get("Calendar_DayEmpty");
            list.Children.Add(empty);
        }
        foreach (var entry in events) list.Children.Add(EventRow(entry));
        stack.Children.Add(Row(new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        }, 2));
        return stack;
    }

    // MARK: Pieces

    /// <summary>"Ekim 2026" (a click goes back to today) and the arrows that page by month or week.</summary>
    private Grid Header(DateOnly showing, double size, Action<int> step, string previous, string next)
    {
        var month = Ui.Text(size, semibold: true);
        month.Text = Culture.DateTimeFormat.MonthNames[showing.Month - 1];
        var year = Ui.Text(size, alpha: 0x80);
        year.Text = showing.Year.ToString(CultureInfo.InvariantCulture);
        var today = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { month, year } },
            Padding = new Thickness(0),
            Background = Ui.Clear(),
            BorderThickness = new Thickness(0),
        };
        today.Resources["ButtonBackgroundPointerOver"] = Ui.Clear();
        today.Resources["ButtonBackgroundPressed"] = Ui.Clear();
        ToolTipService.SetToolTip(today, L.Get("Calendar_BackToToday"));
        today.Click += (_, _) =>
        {
            GoToToday();
            Build();
        };

        var grid = new Grid { ColumnSpacing = 4, ColumnDefinitions = { Star(), Auto(), Auto() } };
        grid.Children.Add(today);
        grid.Children.Add(At(Arrow(Glyphs.Back, previous, () => step(-1)), 1));
        grid.Children.Add(At(Arrow(Glyphs.Forward, next, () => step(1)), 2));
        return grid;
    }

    private static Button Arrow(string glyph, string help, Action click)
    {
        var button = Ui.IconButton(glyph, 22, 11, L.Get(help), click, out _);
        button.Width = 24;
        button.CornerRadius = new CornerRadius(11);
        return button;
    }

    private FrameworkElement DayCell(DateOnly day, bool inMonth, string? dot, double size)
    {
        var isToday = day == Today;
        var isSelected = day == _selected;
        var text = Ui.Text(size / 2, alpha: isToday ? (byte)0xFF : inMonth ? (byte)0xE6 : (byte)0x47);
        text.FontWeight = isToday ? FontWeights.Bold : FontWeights.Medium;
        text.Text = day.Day.ToString(CultureInfo.InvariantCulture);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        var circle = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size / 2),
            Background = isToday ? _theme.Accent.ToBrush() : null,
            BorderBrush = !isToday && isSelected ? _theme.Accent.ToBrush() : null,
            BorderThickness = new Thickness(!isToday && isSelected ? 1.5 : 0),
            Child = text,
        };
        var cell = new Grid { Background = Ui.Clear(), Children = { circle } };
        if (dot is not null && !isToday)
        {
            // Room under each row for the dot, clear of a ring or the today circle.
            cell.Children.Add(new Ellipse
            {
                Width = 3.5,
                Height = 3.5,
                Fill = new SolidColorBrush(Hex(dot)),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, -5),
            });
        }
        cell.Tapped += (_, _) =>
        {
            _selected = day;
            Build();
        };
        return cell;
    }

    private static FrameworkElement EventRow(CalendarEntry entry)
    {
        var bar = new Rectangle { Width = 3, Height = 16, RadiusX = 1.5, RadiusY = 1.5, Fill = new SolidColorBrush(Hex(entry.ColorHex)) };
        var time = Ui.Text(10, alpha: 0x99);
        time.FontWeight = FontWeights.Medium;
        time.Text = entry.IsAllDay ? L.Get("Calendar_AllDay") : Time(entry.Start);
        time.Width = 44;
        var title = Ui.Text(11, semibold: true);
        title.Text = entry.Title;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title } };
        if (entry.Detail is { } detail)
        {
            var detailText = Ui.Text(9, alpha: 0x73);
            detailText.Text = detail;
            text.Children.Add(detailText);
        }
        var row = new Grid { ColumnSpacing = 7, ColumnDefinitions = { Auto(), Auto(), Star(), Auto() } };
        row.Children.Add(bar);
        time.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(At(time, 1));
        row.Children.Add(At(text, 2));
        if (entry.MeetingLink is { } link)
        {
            var join = Ui.IconButton(Glyphs.Video, 18, 9, L.Get("Calendar_Join"), () => Open(link), out _);
            join.Width = 20;
            join.CornerRadius = new CornerRadius(9);
            join.Background = Ui.White(0x26);
            row.Children.Add(At(join, 3));
        }
        return row;
    }

    private static TextBlock WeekdaySymbol(string symbol, double size)
    {
        var text = Ui.Text(size, alpha: 0x73);
        text.FontWeight = FontWeights.Medium;
        text.Text = symbol;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        return text;
    }

    private void ShiftMonth(int step)
    {
        _month = _month.AddMonths(step);
        // The pick follows to the same day of the new month, or today when that's the month.
        _selected = _month.Year == Today.Year && _month.Month == Today.Month ? Today : _month;
        Build();
    }

    private void ShiftWeek(int step)
    {
        _week = _week.AddDays(7 * step);
        // The pick moves along to the same weekday.
        _selected = _selected.AddDays(7 * step);
        Build();
    }

    private string DayTitle()
    {
        var format = Culture.DateTimeFormat;
        var date = Culture.TwoLetterISOLanguageName == "tr"
            ? $"{_selected.Day} {format.AbbreviatedMonthNames[_selected.Month - 1]} {format.AbbreviatedDayNames[(int)_selected.DayOfWeek]}"
            : $"{format.AbbreviatedDayNames[(int)_selected.DayOfWeek]}, {format.AbbreviatedMonthNames[_selected.Month - 1]} {_selected.Day}";
        return _selected == Today ? L.Format("Calendar_TodayTitle", date) : date;
    }

    /// <summary>"Per 29 · 14:00 Toplantı +1", or that the day is free.</summary>
    private string DaySummary()
    {
        var day = $"{Culture.DateTimeFormat.AbbreviatedDayNames[(int)_selected.DayOfWeek]} {_selected.Day}";
        var events = _calendar.On(_selected);
        if (events.Count == 0) return L.Format("Calendar_DayFree", day);
        var first = events[0];
        var time = first.IsAllDay ? L.Get("Calendar_AllDay") : Time(first.Start);
        var more = events.Count > 1 ? $" +{events.Count - 1}" : "";
        return $"{day} · {time} {first.Title}{more}";
    }

    /// <summary>"10:00 Toplantı", a line per event; empty, so no tooltip, on a free day.</summary>
    private static string Summary(IReadOnlyList<CalendarEntry> events) =>
        string.Join("\n", events.Select(e => $"{(e.IsAllDay ? L.Get("Calendar_AllDay") : Time(e.Start))} {e.Title}"));

    private async void AddDraft()
    {
        if (CalendarService.ParseQuickAdd(_draft, _selected, DateTimeOffset.Now) is not { } parsed) return;
        try
        {
            // Windows' calendars can't be written to from here: the event goes to the default
            // calendar app as a file, which offers to add it (PORTING.md §3.6).
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ISLe8-{Guid.NewGuid():N}.ics");
            await System.IO.File.WriteAllTextAsync(path, IcsParser.Write(parsed.Title, parsed.Start, TimeSpan.FromHours(1), DateTimeOffset.Now));
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            await Windows.System.Launcher.LaunchFileAsync(file);
            _draft = "";
            _adding = false;
            Build();
        }
        catch (Exception e)
        {
            Core.Diagnostics.Log.Error(e, "calendar add");
        }
    }

    // MARK: Text

    /// <summary>"Şimdi · bitiş 15:30", "5 dk sonra · 15:00", "15:00 – 16:00", "Yarın · 09:30", "Tüm gün" (UpcomingText).</summary>
    public static string When(CalendarEntry item, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var startDay = DateOnly.FromDateTime(item.Start.LocalDateTime);
        if (item.IsAllDay) return startDay == today || item.Start < now ? L.Get("Calendar_AllDay") : L.Get("Calendar_TomorrowAllDay");
        if (item.Start <= now) return L.Format("Calendar_NowEnds", Time(item.End));
        var minutes = (int)((item.Start - now).TotalMinutes);
        if (minutes < 60) return L.Format("Calendar_InMinutes", Math.Max(minutes, 1), Time(item.Start));
        var range = $"{Time(item.Start)} – {Time(item.End)}";
        return startDay == today.AddDays(1) ? L.Format("Calendar_Tomorrow", range) : range;
    }

    private static string Time(DateTimeOffset time) => time.LocalDateTime.ToString("t", Culture);

    public static Color Hex(string? hex)
    {
        if (hex is { Length: 7 } && uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        }
        return Color.FromArgb(255, 0x8E, 0x8E, 0x93);
    }

    public static async void Open(string link)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri)) await Windows.System.Launcher.LaunchUriAsync(uri);
    }

    public static Button Capsule(string title, string glyph, Action click)
    {
        var text = Ui.Text(11, semibold: true);
        text.Text = title;
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new FontIcon { Glyph = glyph, FontSize = 11 }, text } },
            Height = 24,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0),
            Background = Ui.White(0x1A),
        };
        button.Resources["ButtonBackgroundPointerOver"] = Ui.White(0x2E);
        button.Resources["ButtonBackgroundPressed"] = Ui.White(0x24);
        button.Click += (_, _) => click();
        return button;
    }

    private static ColumnDefinition Auto() => new() { Width = GridLength.Auto };
    private static ColumnDefinition Star() => new() { Width = new GridLength(1, GridUnitType.Star) };
    private static RowDefinition AutoRow() => new() { Height = GridLength.Auto };

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

/// <summary>The heads-up banner a few minutes before a meeting (MeetingAlertView).</summary>
internal sealed class MeetingBanner
{
    private readonly FontIcon _icon = new() { Glyph = Glyphs.Calendar, FontSize = 15 };
    private readonly Border _badge;
    private readonly TextBlock _title = Ui.Text(12, semibold: true);
    private readonly TextBlock _when = Ui.Text(11, alpha: 0xA6);
    private readonly Button _join;
    private string? _link;

    public MeetingBanner(Action joined)
    {
        _badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Child = _icon, VerticalAlignment = VerticalAlignment.Center };
        _join = CalendarColumn.Capsule(L.Get("Calendar_Join"), Glyphs.Video, () =>
        {
            if (_link is { } link) CalendarColumn.Open(link);
            joined();
        });
        _join.VerticalAlignment = VerticalAlignment.Center;
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _when } };
        var row = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            Children = { _badge, text, _join },
        };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(_join, 2);
        Root = row;
    }

    public FrameworkElement Root { get; }

    /// <summary>Whether the banner has a button the pointer should reach.</summary>
    public bool HasJoin => _link is not null;

    public void Show(CalendarEntry entry)
    {
        var color = CalendarColumn.Hex(entry.ColorHex);
        _icon.Foreground = new SolidColorBrush(color);
        _badge.Background = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B));
        _title.Text = entry.Title;
        _when.Text = CalendarColumn.When(entry, DateTimeOffset.Now);
        _link = entry.MeetingLink;
        _join.Visibility = _link is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
