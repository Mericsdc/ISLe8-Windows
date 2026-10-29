using ISLe8.Core.Settings;
using ISLe8.Core.Theming;
using ISLe8.Core.Timer;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ISLe8.Timer;

/// <summary>The timer's symbols and colours (TimerViews.swift, TimerSymbols).</summary>
internal static class TimerLook
{
    public static readonly Color Orange = Color.FromArgb(255, 0xFF, 0x9F, 0x0A);
    public static readonly Color Green = Color.FromArgb(255, 0x30, 0xD1, 0x58);

    public static string Glyph(IslandTimer timer) => timer.Mode switch
    {
        TimerMode.Pomodoro => timer.IsBreak ? Glyphs.Break : Glyphs.Focus,
        TimerMode.Stopwatch => Glyphs.Stopwatch,
        _ => Glyphs.Timer,
    };

    public static Color Tint(IslandTimer timer) => timer is { Mode: TimerMode.Pomodoro, IsBreak: true } ? Green : Orange;

    /// <summary>A countdown rounds up, a stopwatch down.</summary>
    public static string Time(IslandTimer timer, DateTimeOffset at) =>
        TimerText.Format(timer.Value(at), roundingUp: timer.Mode != TimerMode.Stopwatch);

    public static TextBlock Digits(double size)
    {
        var text = Ui.Text(size, semibold: true);
        // The Mac's rounded face; Windows has none, its display cut is the softest.
        text.FontFamily = Ui.Font ?? new FontFamily("Segoe UI Variable Display");
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        return text;
    }
}

/// <summary>
/// Ticks once a second while something that shows the time is on screen and the timer runs; at
/// rest, and while paused, it doesn't run at all.
/// </summary>
internal sealed class SecondTicker
{
    private readonly DispatcherQueueTimer _timer;

    public SecondTicker(DispatcherQueue queue, Action tick, TimeSpan? every = null)
    {
        _timer = queue.CreateTimer();
        _timer.Interval = every ?? TimeSpan.FromSeconds(1);
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => tick();
    }

    public void Run(bool run)
    {
        if (run && !_timer.IsRunning) _timer.Start();
        else if (!run && _timer.IsRunning) _timer.Stop();
    }
}

/// <summary>
/// Home's timer column (TimerViews.swift, TimerTile). Idle: quick starts (5 and 15 min, Pomodoro,
/// stopwatch); running: what it is, the time, its progress, pause and stop. The minimal strip: the
/// time and stop, or two quick starts.
/// </summary>
internal sealed class TimerTile
{
    private readonly IslandTimer _timer;
    private readonly HomeStyle _style;
    private readonly Border _host;
    private readonly SecondTicker _ticker;
    private TextBlock? _time;
    private ProgressBar? _progress;
    private bool _active;

    public TimerTile(IslandTimer timer, IslandTheme theme, HomeStyle style, DispatcherQueue queue)
    {
        _timer = timer;
        _style = style;
        _host = new Border();
        Root = style == HomeStyle.Dashboard ? Tiles.Tile(theme, _host, new Thickness(10)) : _host;
        _ticker = new SecondTicker(queue, Tick);
        timer.Changed += OnChanged;
        Build();
    }

    public FrameworkElement Root { get; }

    /// <summary>True while it can be seen; only then does the time tick.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (active) Tick();
        _ticker.Run(_active && _timer.IsRunning);
    }

    /// <summary>The view is dropped; stop listening to the timer.</summary>
    public void Detach()
    {
        _timer.Changed -= OnChanged;
        _ticker.Run(false);
    }

    private void OnChanged()
    {
        Build();
        _ticker.Run(_active && _timer.IsRunning);
    }

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        if (_time is not null)
        {
            _time.Text = TimerLook.Time(_timer, now);
            _time.Opacity = _timer.IsRunning || _style != HomeStyle.Minimal ? 1 : 0.5;
        }
        if (_progress is not null) _progress.Value = _timer.Progress(now);
    }

    private void Build()
    {
        _time = null;
        _progress = null;
        _host.Child = _style == HomeStyle.Minimal ? Minimal() : _timer.Mode == TimerMode.Idle ? Presets() : Running();
        Tick();
    }

    /// <summary>The minimal strip: the time left while running, else two quick starts.</summary>
    private FrameworkElement Minimal()
    {
        var row = new Grid { ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var idle = _timer.Mode == TimerMode.Idle;
        var icon = new FontIcon
        {
            Glyph = TimerLook.Glyph(_timer),
            FontSize = 12,
            Foreground = idle ? Ui.White(0x99) : new SolidColorBrush(TimerLook.Tint(_timer)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(icon);
        if (idle)
        {
            Add(row, Preset(L.Get("Timer_Five"), null, null, () => _timer.StartCountdown(5)), star: true);
            Add(row, Preset(L.Get("Timer_Fifteen"), null, null, () => _timer.StartCountdown(15)), star: true);
            return row;
        }
        _time = TimerLook.Digits(16);
        _time.VerticalAlignment = VerticalAlignment.Center;
        Add(row, _time, star: true);
        var stop = Preset(null, Glyphs.Stop, L.Get("Timer_Stop"), _timer.Reset);
        stop.Width = 30;
        Add(row, stop, star: false);
        return row;
    }

    private FrameworkElement Presets()
    {
        var title = Ui.Text(11, alpha: 0x8C, semibold: true);
        title.Text = L.Get("Home_Timer");
        var quick = Pair(
            Preset(L.Get("Timer_Five"), null, null, () => _timer.StartCountdown(5)),
            Preset(L.Get("Timer_Fifteen"), null, null, () => _timer.StartCountdown(15)));
        var modes = Pair(
            Preset(null, Glyphs.Focus, L.Get("Timer_StartPomodoro"), _timer.StartPomodoro),
            Preset(null, Glyphs.Stopwatch, L.Get("Timer_StartStopwatch"), _timer.StartStopwatch));
        var stack = new Grid
        {
            RowSpacing = 6,
            RowDefinitions = { Auto(), new RowDefinition(), Auto(), Auto() },
        };
        stack.Children.Add(title);
        stack.Children.Add(Row(quick, 2));
        stack.Children.Add(Row(modes, 3));
        return stack;
    }

    private FrameworkElement Running()
    {
        var tint = new SolidColorBrush(TimerLook.Tint(_timer));
        var label = Ui.Text(11, alpha: 0xB3, semibold: true);
        label.Text = _timer.Mode switch
        {
            TimerMode.Pomodoro => L.Format(_timer.IsBreak ? "Timer_BreakRound" : "Timer_FocusRound", _timer.Round),
            TimerMode.Stopwatch => L.Get("Timer_Stopwatch"),
            _ => L.Get("Home_Timer"),
        };
        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children = { new FontIcon { Glyph = TimerLook.Glyph(_timer), FontSize = 11, Foreground = tint }, label },
        };

        _time = TimerLook.Digits(26);
        var stack = new Grid { RowSpacing = 4, RowDefinitions = { Auto(), Auto(), Auto(), new RowDefinition(), Auto() } };
        stack.Children.Add(heading);
        // The Mac shrinks the time to fit a narrow column; a Viewbox does that here.
        stack.Children.Add(Row(new Viewbox { Child = _time, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left }, 1));
        if (_timer.Mode != TimerMode.Stopwatch)
        {
            _progress = new ProgressBar { Minimum = 0, Maximum = 1, Foreground = tint, Background = Ui.White(0x26), MinHeight = 3 };
            stack.Children.Add(Row(_progress, 2));
        }
        var controls = Pair(
            _timer.IsRunning
                ? Preset(null, Glyphs.Pause, L.Get("Media_Pause"), _timer.Pause)
                : Preset(null, Glyphs.Play, L.Get("Timer_Resume"), _timer.Resume),
            Preset(null, Glyphs.Stop, L.Get("Timer_Stop"), _timer.Reset));
        stack.Children.Add(Row(controls, 4));
        return stack;
    }

    /// <summary>A capsule 24 high, full width (PresetButton).</summary>
    private static Button Preset(string? title, string? glyph, string? help, Action click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        if (glyph is not null) content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 11 });
        if (title is not null)
        {
            var text = Ui.Text(11, semibold: true);
            text.Text = title;
            content.Children.Add(text);
        }
        var button = new Button
        {
            Content = content,
            Height = 24,
            Padding = new Thickness(6, 0, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0),
            Background = Ui.White(0x1A),
        };
        button.Resources["ButtonBackgroundPointerOver"] = Ui.White(0x2E);
        button.Resources["ButtonBackgroundPressed"] = Ui.White(0x24);
        button.Resources["ButtonForegroundPointerOver"] = Ui.White(0xFF);
        button.Resources["ButtonForegroundPressed"] = Ui.White(0xCC);
        if (help is not null) ToolTipService.SetToolTip(button, help);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, help ?? title ?? "");
        button.Click += (_, _) => click();
        return button;
    }

    private static Grid Pair(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition() } };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static void Add(Grid row, FrameworkElement element, bool star)
    {
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = star ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Grid.SetColumn(element, row.ColumnDefinitions.Count - 1);
        row.Children.Add(element);
    }

    private static RowDefinition Auto() => new() { Height = GridLength.Auto };

    private static FrameworkElement Row(FrameworkElement element, int row)
    {
        Grid.SetRow(element, row);
        return element;
    }
}

/// <summary>The timer in the wide pill's ears: its symbol on the left, the time on the right (TimerEarIcon, TimerEarTime).</summary>
internal sealed class TimerEar
{
    private readonly IslandTimer _timer;
    private readonly FontIcon _icon = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _time = TimerLook.Digits(11);
    private readonly SecondTicker _ticker;
    private bool _shown;

    public TimerEar(IslandTimer timer, DispatcherQueue queue)
    {
        _timer = timer;
        _ticker = new SecondTicker(queue, Tick);
        _time.VerticalAlignment = VerticalAlignment.Center;
        _time.HorizontalAlignment = HorizontalAlignment.Right;
        Leading = new Grid { Width = 22, Height = 22, Children = { _icon } };
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
    }

    public FrameworkElement Leading { get; }
    public TextBlock Trailing => _time;

    /// <summary>In the ears (<paramref name="shown"/>); it ticks only while <paramref name="seen"/> and running.</summary>
    public void SetShown(bool shown, bool seen)
    {
        _shown = shown && seen;
        Leading.Visibility = _time.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    public void Refresh()
    {
        if (_timer.Mode != TimerMode.Idle)
        {
            _icon.Glyph = TimerLook.Glyph(_timer);
            _icon.Foreground = new SolidColorBrush(TimerLook.Tint(_timer));
        }
        Tick();
        _ticker.Run(_shown && _timer.IsRunning);
    }

    private void Tick()
    {
        if (_timer.Mode == TimerMode.Idle) return;
        _time.Text = TimerLook.Time(_timer, DateTimeOffset.Now);
        _time.Opacity = _timer.IsRunning ? 1 : 0.5;
    }

    public void Stop() => _ticker.Run(false);
}

/// <summary>The banner when a phase ends (TimerAlertView): a tinted symbol, what happened, what's next.</summary>
internal sealed class TimerBanner
{
    private readonly FontIcon _icon = new() { FontSize = 15 };
    private readonly Border _badge;
    private readonly TextBlock _title = Ui.Text(12, semibold: true);
    private readonly TextBlock _detail = Ui.Text(11, alpha: 0x99);

    public TimerBanner()
    {
        _badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), Child = _icon, VerticalAlignment = VerticalAlignment.Center };
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _detail } };
        var row = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            Children = { _badge, text },
        };
        Grid.SetColumn(text, 1);
        Root = row;
    }

    public FrameworkElement Root { get; }

    public void Show(TimerEvent e)
    {
        var tint = e is TimerEvent.BreakStarts ? TimerLook.Green : TimerLook.Orange;
        _icon.Glyph = e switch
        {
            TimerEvent.BreakStarts => Glyphs.Break,
            TimerEvent.WorkStarts => Glyphs.Focus,
            _ => Glyphs.Timer,
        };
        _icon.Foreground = new SolidColorBrush(tint);
        _badge.Background = new SolidColorBrush(Color.FromArgb(0x33, tint.R, tint.G, tint.B));
        (_title.Text, _detail.Text) = e switch
        {
            TimerEvent.BreakStarts b => (L.Get("Timer_BreakTime"), L.Format(b.Minutes == 1 ? "Timer_RestOne" : "Timer_Rest", b.Minutes)),
            TimerEvent.WorkStarts w => (L.Get("Timer_FocusTime"), L.Format("Timer_NewRound", w.Minutes)),
            _ => (L.Get("Timer_Finished"), L.Get("Timer_FinishedDetail")),
        };
    }
}
