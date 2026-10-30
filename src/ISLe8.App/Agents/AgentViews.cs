using ISLe8.Core.Agents;
using ISLe8.Core.Settings;
using ISLe8.Usage;
using ISLe8.Core.Theming;
using ISLe8.Core.Timer;
using ISLe8.Localization;
using ISLe8.Media;
using ISLe8.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace ISLe8.Agents;

/// <summary>The agents' faces and colours, and their actions in words (AgentViews.swift, AgentIcon; AgentsModule.swift, describe).</summary>
internal static class AgentLook
{
    public static readonly Color Orange = Color.FromArgb(255, 0xFF, 0x9F, 0x0A);
    public static readonly Color Green = Color.FromArgb(255, 0x30, 0xD1, 0x58);
    public static readonly Color Blue = Color.FromArgb(255, 0x0A, 0x84, 0xFF);

    /// <summary>The desktop apps whose icons stand for the agents: Claude, and ChatGPT for Codex.</summary>
    private static readonly Dictionary<AgentKind, string> Apps = new()
    {
        [AgentKind.Claude] = "Claude_pzs8sxrjxfjjc!Claude",
        [AgentKind.Codex] = "OpenAI.Codex_2p2nqsd0c76g0!App",
    };

    private static readonly Dictionary<AgentKind, ImageSource?> Logos = [];

    public static Color Tint(AgentKind agent) => agent == AgentKind.Claude ? Color.FromArgb(255, 217, 120, 87) : Color.FromArgb(255, 230, 230, 230);

    /// <summary>Reads the two apps' logos once; until then (or without the app) a tinted mark stands in.</summary>
    public static async Task LoadAsync()
    {
        foreach (var (agent, appId) in Apps)
        {
            try
            {
                if (Windows.ApplicationModel.AppInfo.GetFromAppUserModelId(appId)?.DisplayInfo.GetLogo(new Size(64, 64)) is not { } logo) continue;
                using var stream = await logo.OpenReadAsync();
                var bitmap = new BitmapImage();
                await bitmap.SetSourceAsync(stream);
                Logos[agent] = bitmap;
            }
            catch (Exception e) when (e is ArgumentException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or IOException)
            {
            }
        }
    }

    public static FrameworkElement Icon(AgentKind agent, double size)
    {
        if (Logos.GetValueOrDefault(agent) is { } logo) return new Image { Source = logo, Width = size, Height = size };
        if (agent == AgentKind.Codex) return new FontIcon { Glyph = "", FontSize = size * 0.7, Foreground = new SolidColorBrush(Tint(agent)), Width = size, Height = size };
        // Claude's spark: four strokes through the middle.
        var canvas = new Grid { Width = 24, Height = 24 };
        for (var i = 0; i < 4; i++)
        {
            var angle = i * Math.PI / 4;
            var (dx, dy) = (Math.Cos(angle) * 9, Math.Sin(angle) * 9);
            canvas.Children.Add(new Line
            {
                X1 = 12 - dx, Y1 = 12 - dy, X2 = 12 + dx, Y2 = 12 + dy,
                Stroke = new SolidColorBrush(Tint(agent)),
                StrokeThickness = 3,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    public static string Describe(AgentAction action) => action.Kind switch
    {
        ActionKind.Thinking => L.Get("Agents_Thinking"),
        ActionKind.WaitingInput => L.Get("Agents_WaitingInput"),
        ActionKind.Done => L.Get("Agents_Done"),
        ActionKind.Command => "$ " + action.Detail,
        ActionKind.Reading => L.Format("Agents_Reading", action.Detail),
        ActionKind.Writing => L.Format("Agents_Writing", action.Detail),
        ActionKind.Editing => L.Format("Agents_Editing", action.Detail),
        ActionKind.Patching => L.Get("Agents_Patching"),
        ActionKind.Searching => L.Format("Agents_Searching", action.Detail),
        ActionKind.Web => "Web: " + action.Detail,
        ActionKind.WebSearch => L.Format("Agents_WebSearch", action.Detail),
        ActionKind.Subagent => L.Format("Agents_Subagent", action.Detail),
        ActionKind.PlanUpdated => L.Get("Agents_PlanUpdated"),
        ActionKind.Message or ActionKind.Tool => action.Detail,
        _ => "",
    };

    public static string StepGlyph(StepIcon icon) => icon switch
    {
        StepIcon.Terminal => "",
        StepIcon.Read => "",
        StepIcon.Write => "",
        StepIcon.Edit => "",
        StepIcon.Search => "",
        StepIcon.Web => "",
        StepIcon.Subagent => "",
        StepIcon.Plan => "",
        _ => "",
    };

    /// <summary>A script's icon: a Segoe Fluent Icons code point ("E768") or a character; a terminal without one.</summary>
    public static string ScriptGlyph(string icon) =>
        icon.Length is 4 or 5 && int.TryParse(icon, System.Globalization.NumberStyles.HexNumber, null, out var code) ? char.ConvertFromUtf32(code)
        : icon.Length is 1 or 2 ? icon
        : "";

    /// <summary>"4,2 sn" under ten seconds, "13 sn" after (AgentViews.swift, StepRow.seconds).</summary>
    public static string Seconds(TimeSpan interval) => L.Format("Settings_Seconds",
        interval.TotalSeconds < 10 ? interval.TotalSeconds.ToString("0.0", L.Culture) : Math.Round(interval.TotalSeconds).ToString(L.Culture));

    public static string Headline(AgentSession session) => session.Status switch
    {
        AgentStatus.Done => string.Join(" · ", new[]
        {
            L.Get("Agents_Done"),
            session.TurnSeconds is { } seconds ? L.Format("Agents_TurnSeconds", seconds) : null,
            session.ChangedFiles.Count switch { 0 => null, 1 => L.Format("Agents_FileChanged", 1), var n => L.Format("Agents_FilesChanged", n) },
        }.OfType<string>()),
        AgentStatus.WaitingApproval => L.Get("Agents_WaitingApproval"),
        AgentStatus.NeedsInput => session.Action.Kind == ActionKind.None ? L.Get("Agents_WaitingForYou") : Describe(session.Action),
        AgentStatus.Working => session.Action.Kind == ActionKind.None ? L.Get("Agents_Thinking") : Describe(session.Action),
        _ => session.Action.Kind == ActionKind.None ? L.Get("Agents_Ready") : Describe(session.Action),
    };

    public static string Progress(AgentSession session) => session.CurrentStep is { } step
        ? L.Format("Agents_Step", step.Step, step.Total)
        : L.Format(session.Steps == 1 ? "Agents_StepOne" : "Agents_Steps", session.Steps);

    public static Color StatusColor(AgentStatus status) => status switch
    {
        AgentStatus.Working => Blue,
        AgentStatus.NeedsInput or AgentStatus.WaitingApproval => Orange,
        AgentStatus.Done => Green,
        _ => Color.FromArgb(0x66, 255, 255, 255),
    };

    public static FontFamily Mono { get; } = new("Cascadia Mono, Consolas");

    /// <summary>A capsule button: the accent-filled "Onayla", the faint "Reddet".</summary>
    public static Button Capsule(string text, string glyph, Brush background, Action click)
    {
        var label = Ui.Text(11, semibold: true);
        label.Text = text;
        label.VerticalAlignment = VerticalAlignment.Center;
        var button = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 5,
                Children = { new FontIcon { Glyph = glyph, FontSize = 10, Foreground = Ui.White(0xFF) }, label },
            },
            Height = 24,
            Padding = new Thickness(12, 0, 12, 0),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0),
            Background = background,
        };
        button.Resources["ButtonBackgroundPointerOver"] = background;
        button.Resources["ButtonBackgroundPressed"] = background;
        button.Resources["ButtonBorderBrushPointerOver"] = Ui.Clear();
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A plain text button, as the Mac's "Terminalde sor" and "Oturuma git".</summary>
    public static Button Plain(UIElement content, Action click)
    {
        var button = new Button { Content = content, Padding = new Thickness(4, 2, 4, 2), BorderThickness = new Thickness(0), Background = Ui.Clear() };
        button.Resources["ButtonBackgroundPointerOver"] = Ui.White(0x14);
        button.Resources["ButtonBackgroundPressed"] = Ui.White(0x0D);
        button.Resources["ButtonBorderBrushPointerOver"] = Ui.Clear();
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>Onayla / Reddet / Terminalde sor (AgentViews.swift, ApprovalButtons).</summary>
    public static Grid ApprovalButtons(IslandTheme theme, AgentApproval approval, Action<AgentApproval, bool?> decide)
    {
        var approve = Capsule(L.Get("Agents_Approve"), "", theme.Accent.ToBrush(), () => decide(approval, true));
        var deny = Capsule(L.Get("Agents_Deny"), "", Ui.White(0x1A), () => decide(approval, false));
        var ask = Ui.Text(11, alpha: 0x8C);
        ask.Text = L.Get("Agents_AskInTerminal");
        var terminal = Plain(ask, () => decide(approval, null));
        var row = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            Children = { approve, deny, terminal },
        };
        Grid.SetColumn(deny, 1);
        Grid.SetColumn(terminal, 3);
        return row;
    }
}

/// <summary>
/// The AI tab (AgentViews.swift, AgentsTab): an approval first, then the run card of the session
/// being followed, after componest's agent status component; a script's activity below; the usage
/// limits under the runs on the dashboard, or in a 270-wide column beside them in the shorter
/// layouts. It scrolls inside the island's fixed height. Drawn only while on screen; the clock ticks
/// only while an agent works.
/// </summary>
internal sealed class AgentsTabView
{
    private readonly AgentHub _hub;
    private readonly IslandTheme _theme;
    private readonly Action<AgentApproval, bool?> _decide;
    private readonly Action<int> _goToSession;
    private readonly StackPanel _stack = new() { Spacing = 8 };
    private readonly UsageService _usage;
    private readonly AppSettings _settings;
    private readonly Grid _layout = new() { ColumnSpacing = 10 };
    private readonly StackPanel _scrolled = new() { Spacing = 8 };
    private readonly DispatcherQueueTimer _ticker;
    private readonly List<Action> _tickers = [];
    private string? _followed;
    private bool _active;
    private bool _stale = true;

    public AgentsTabView(AgentHub hub, UsageService usage, AppSettings settings, IslandTheme theme, DispatcherQueue queue, Action<AgentApproval, bool?> decide, Action<int> goToSession)
    {
        _hub = hub;
        _usage = usage;
        _settings = settings;
        _theme = theme;
        _decide = decide;
        _goToSession = goToSession;
        _scrolled.Children.Add(_stack);
        var scroller = new ScrollViewer
        {
            Content = _scrolled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _layout.ColumnDefinitions.Add(new ColumnDefinition());
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _layout.Children.Add(scroller);
        Root = _layout;
        _ticker = queue.CreateTimer();
        _ticker.Interval = TimeSpan.FromSeconds(1);
        _ticker.Tick += (_, _) =>
        {
            foreach (var tick in _tickers) tick();
        };
        _hub.Changed += OnChanged;
        _usage.Changed += OnChanged;
    }

    public Grid Root { get; }

    public void SetActive(bool active)
    {
        _active = active;
        if (active && _stale) Build();
        UpdateTicker();
    }

    public void Detach()
    {
        _hub.Changed -= OnChanged;
        _usage.Changed -= OnChanged;
        _ticker.Stop();
    }

    private void OnChanged()
    {
        _stale = true;
        if (_active) Build();
    }

    private void UpdateTicker()
    {
        if (_active && _tickers.Count > 0 && _hub.Sessions.Any(session => session.Status == AgentStatus.Working)) _ticker.Start();
        else _ticker.Stop();
    }

    private void Build()
    {
        _stale = false;
        _stack.Children.Clear();
        _tickers.Clear();
        if (_hub.Approvals.FirstOrDefault() is { } approval) _stack.Children.Add(ApprovalCard(approval));
        if (_hub.Sessions.Count == 0 && _hub.Activities.Count == 0)
        {
            if (_hub.Approvals.Count == 0) _stack.Children.Add(EmptyState());
        }
        else
        {
            if (_hub.Sessions.Count > 1) _stack.Children.Add(Switcher());
            if (_hub.Followed(_followed) is { } session) _stack.Children.Add(RunCard(session));
            foreach (var activity in _hub.Activities.Take(_hub.Sessions.Count == 0 ? 3 : 1)) _stack.Children.Add(ScriptRow(activity));
        }
        PlaceUsage();
        UpdateTicker();
    }

    /// <summary>The limits card: under the runs on the dashboard; beside them in the strips, which are too short to stack it.</summary>
    private void PlaceUsage()
    {
        while (_scrolled.Children.Count > 1) _scrolled.Children.RemoveAt(1);
        while (_layout.Children.Count > 1) _layout.Children.RemoveAt(1);
        var dashboard = _settings.HomeStyle == HomeStyle.Dashboard;
        var strip = new UsageStrip(_usage, _theme, stacked: !dashboard);
        var shown = strip.Build();
        _layout.ColumnDefinitions[1].Width = shown && !dashboard ? new GridLength(270) : new GridLength(0);
        if (!shown) return;
        if (dashboard)
        {
            _scrolled.Children.Add(strip.Root);
            return;
        }
        strip.Root.VerticalAlignment = VerticalAlignment.Stretch;
        Grid.SetColumn(strip.Root, 1);
        _layout.Children.Add(strip.Root);
    }

    private Border EmptyState()
    {
        var title = Ui.Text(12, semibold: true);
        title.Text = L.Get("Agents_NoneRunning");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var note = Ui.Text(11, alpha: 0x80);
        note.Text = L.Get("Agents_EmptyNote");
        note.TextWrapping = TextWrapping.Wrap;
        note.MaxLines = 3;
        note.TextAlignment = TextAlignment.Center;
        var stack = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { new FontIcon { Glyph = Glyphs.Agents, FontSize = 18, Foreground = _theme.Accent.ToBrush() }, title, note },
        };
        return Tiles.Tile(_theme, stack, new Thickness(14));
    }

    private ScrollViewer Switcher()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var selected = _hub.Followed(_followed)?.Id;
        foreach (var session in _hub.Sessions)
        {
            var name = Ui.Text(11, semibold: true);
            name.Text = session.Project;
            name.VerticalAlignment = VerticalAlignment.Center;
            var dot = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(AgentLook.StatusColor(session.Status)), VerticalAlignment = VerticalAlignment.Center };
            var icon = AgentLook.Icon(session.Agent, 14);
            icon.VerticalAlignment = VerticalAlignment.Center;
            var chip = new Border
            {
                Height = 24,
                Padding = new Thickness(9, 0, 9, 0),
                CornerRadius = new CornerRadius(12),
                Background = Ui.White(session.Id == selected ? (byte)0x24 : (byte)0x0D),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { icon, name, dot } },
            };
            var id = session.Id;
            chip.Tapped += (_, _) =>
            {
                _followed = id;
                Build();
            };
            row.Children.Add(chip);
        }
        return new ScrollViewer
        {
            Content = row,
            Height = 24,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
    }

    /// <summary>One session's turn: what it does now, how far along it is, the last few steps (AgentViews.swift, RunCard).</summary>
    private Border RunCard(AgentSession session)
    {
        var headline = Ui.Text(13);
        headline.FontWeight = FontWeights.Medium;
        headline.Text = AgentLook.Headline(session);
        headline.VerticalAlignment = VerticalAlignment.Center;
        var elapsed = Ui.Text(12, alpha: 0xB3);
        elapsed.FontWeight = FontWeights.Medium;
        elapsed.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(elapsed, FontNumeralAlignment.Tabular);
        void Tick()
        {
            if (session.TurnStarted is not { } started) return;
            var end = session.Status == AgentStatus.Working ? DateTimeOffset.Now : session.TurnEnded ?? session.Updated;
            elapsed.Text = TimerText.Format(end - started, roundingUp: false);
        }
        Tick();
        _tickers.Add(Tick);
        var top = Row(StatusGlyph(session.Status), headline, elapsed);

        var agentLine = Ui.Text(11, alpha: 0x80);
        agentLine.Text = $"{AgentKinds.Name(session.Agent)} · {session.Project}";
        var progress = Ui.Text(11, alpha: 0x80);
        progress.Text = AgentLook.Progress(session);
        var agentIcon = AgentLook.Icon(session.Agent, 13);
        var second = Row(agentIcon, agentLine, progress, spacing: 6);

        var stack = new StackPanel { Spacing = 8, Children = { top, second, Segments(session) } };
        var steps = new StackPanel { Spacing = 5 };
        foreach (var step in session.Log.TakeLast(4)) steps.Children.Add(StepRow(step, step.Finished is null && session.Status == AgentStatus.Working));
        // While it thinks the headline already says so; after a turn without tools, its last words.
        if (session.Log.Count == 0 && session.Status != AgentStatus.Working && session.LastMessage is { } message)
        {
            var words = Ui.Text(11, alpha: 0x80);
            words.Text = message;
            words.TextWrapping = TextWrapping.Wrap;
            words.MaxLines = 2;
            steps.Children.Add(words);
        }
        if (steps.Children.Count > 0) stack.Children.Add(steps);

        if (session.AppPid is { } pid)
        {
            var label = Ui.Text(11, alpha: 0xBF);
            label.FontWeight = FontWeights.Medium;
            label.Text = L.Get("Agents_GoToSession");
            var go = AgentLook.Plain(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { label, new FontIcon { Glyph = "", FontSize = 10, Foreground = Ui.White(0xBF) } },
            }, () => _goToSession(pid));
            go.HorizontalAlignment = HorizontalAlignment.Right;
            stack.Children.Add(go);
        }
        return Tiles.Tile(_theme, stack, new Thickness(12));
    }

    private static FrameworkElement StatusGlyph(AgentStatus status) => status switch
    {
        AgentStatus.Working => new ProgressRing { IsActive = true, Width = 14, Height = 14, MinWidth = 14, MinHeight = 14 },
        AgentStatus.Done => new FontIcon { Glyph = "", FontSize = 14, Foreground = new SolidColorBrush(AgentLook.Green) },
        AgentStatus.NeedsInput or AgentStatus.WaitingApproval => new FontIcon { Glyph = "", FontSize = 14, Foreground = new SolidColorBrush(AgentLook.Orange) },
        _ => new FontIcon { Glyph = "", FontSize = 14, Foreground = Ui.White(0x80) },
    };

    /// <summary>One segment per plan item; without a plan, one per step so far (AgentViews.swift, SegmentedBar).</summary>
    private Grid Segments(AgentSession session)
    {
        var bar = new Grid { ColumnSpacing = 3, Height = 4 };
        var accent = _theme.Accent.ToColor();
        foreach (var (state, index) in session.Segments.Select((state, index) => (state, index)))
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            var segment = new Border
            {
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(state switch
                {
                    PlanState.Done => accent,
                    PlanState.Active => Color.FromArgb(0x73, accent.R, accent.G, accent.B),
                    _ => Color.FromArgb(0x1A, 255, 255, 255),
                }),
            };
            Grid.SetColumn(segment, index);
            bar.Children.Add(segment);
        }
        return bar;
    }

    private Grid StepRow(AgentStep step, bool isCurrent)
    {
        var alpha = isCurrent ? (byte)0x8C : (byte)0xD9;
        var icon = new FontIcon { Glyph = AgentLook.StepGlyph(step.Icon), FontSize = 10, Width = 14, Foreground = Ui.White(alpha) };
        var text = Ui.Text(11, alpha: alpha);
        text.Text = AgentLook.Describe(step.Action);
        var time = Ui.Text(11, alpha: 0x66);
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        void Tick()
        {
            if (step.Finished is { } finished) time.Text = AgentLook.Seconds(finished - step.Started);
            else if (isCurrent) time.Text = AgentLook.Seconds(DateTimeOffset.Now - step.Started);
        }
        Tick();
        if (isCurrent) _tickers.Add(Tick);
        return Row(icon, text, time, spacing: 8);
    }

    private Border ApprovalCard(AgentApproval approval)
    {
        var who = Ui.Text(11, alpha: 0xB3, semibold: true);
        who.Text = $"{AgentKinds.Name(approval.Agent)} · {approval.Project}";
        var waiting = Ui.Text(10, semibold: true);
        waiting.Text = L.Get("Agents_WaitingApproval");
        waiting.Foreground = new SolidColorBrush(AgentLook.Orange);
        var detail = new TextBlock
        {
            Text = AgentLook.Describe(approval.Detail),
            FontFamily = AgentLook.Mono,
            FontSize = 12,
            Foreground = Ui.White(0xFF),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsTextSelectionEnabled = true,
        };
        var stack = new StackPanel
        {
            Spacing = 8,
            Children = { Row(AgentLook.Icon(approval.Agent, 18), who, waiting), detail },
        };
        // Approve allows all of it, so all of it is shown: every line of a command, a file's full path.
        if (approval.Request.Length > 0)
        {
            if (approval.RequestLines > 1)
            {
                var lines = Ui.Text(10, semibold: true);
                lines.Text = L.Format("Agents_RequestLines", approval.RequestLines);
                lines.Foreground = new SolidColorBrush(AgentLook.Orange);
                stack.Children.Add(lines);
            }
            stack.Children.Add(new ScrollViewer
            {
                MaxHeight = 120,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = approval.Request,
                    FontFamily = AgentLook.Mono,
                    FontSize = 11,
                    Foreground = Ui.White(0xD9),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                },
            });
        }
        stack.Children.Add(AgentLook.ApprovalButtons(_theme, approval, _decide));
        var card = Tiles.Tile(_theme, stack, new Thickness(10));
        card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x73, AgentLook.Orange.R, AgentLook.Orange.G, AgentLook.Orange.B));
        card.BorderThickness = new Thickness(1);
        return card;
    }

    private Border ScriptRow(ScriptActivity activity)
    {
        var title = Ui.Text(12, semibold: true);
        title.Text = activity.Title;
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { title } };
        if (activity.Subtitle.Length > 0)
        {
            var subtitle = Ui.Text(11, alpha: 0x99);
            subtitle.Text = activity.Subtitle;
            text.Children.Add(subtitle);
        }
        if (activity.Progress is { } progress)
        {
            text.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = progress, Foreground = _theme.Accent.ToBrush() });
        }
        var icon = new FontIcon { Glyph = AgentLook.ScriptGlyph(activity.Icon), FontSize = 13, Width = 22, Height = 22, Foreground = Ui.White(0xFF) };
        var row = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            Children = { icon, text },
        };
        Grid.SetColumn(text, 1);
        return Tiles.Tile(_theme, row, new Thickness(8));
    }

    /// <summary>A leading element, text that takes the room, a trailing one.</summary>
    private static Grid Row(FrameworkElement leading, FrameworkElement middle, FrameworkElement trailing, double spacing = 7)
    {
        leading.VerticalAlignment = VerticalAlignment.Center;
        middle.VerticalAlignment = VerticalAlignment.Center;
        trailing.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid
        {
            ColumnSpacing = spacing,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
            Children = { leading, middle, trailing },
        };
        Grid.SetColumn(middle, 1);
        Grid.SetColumn(trailing, 2);
        return row;
    }
}

/// <summary>The banner when an agent asks to run a tool (AgentViews.swift, ApprovalAlertView). Its buttons take the pointer.</summary>
internal sealed class ApprovalBanner(IslandTheme theme, Action<AgentApproval, bool?> decide)
{
    public Grid Root { get; } = new();

    public void Show(AgentApproval approval)
    {
        var who = Ui.Text(11, alpha: 0xBF, semibold: true);
        who.Text = L.Format("Agents_WantsApproval", AgentKinds.Name(approval.Agent), approval.Project);
        who.VerticalAlignment = VerticalAlignment.Center;
        var icon = AgentLook.Icon(approval.Agent, 18);
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { icon, who } };
        var detail = new TextBlock
        {
            Text = AgentLook.Describe(approval.Detail),
            FontFamily = AgentLook.Mono,
            FontSize = 11,
            Foreground = Ui.White(0xFF),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = approval.IsLongerThanDetail ? 1 : 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var stack = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { heading, detail } };
        // The banner has room for a line; when the request is longer it says so, and the AI tab shows it all.
        if (approval.IsLongerThanDetail)
        {
            var more = Ui.Text(10, semibold: true);
            more.Text = L.Format("Agents_RequestMore", approval.RequestLines);
            more.Foreground = new SolidColorBrush(AgentLook.Orange);
            stack.Children.Add(more);
        }
        stack.Children.Add(AgentLook.ApprovalButtons(theme, approval, decide));
        Root.Children.Clear();
        Root.Children.Add(stack);
    }
}

/// <summary>The banner when an agent's turn is done (AgentViews.swift, AgentDoneAlertView).</summary>
internal sealed class AgentDoneBanner
{
    public Grid Root { get; } = new();

    public void Show(AgentSession session)
    {
        var title = Ui.Text(12, semibold: true);
        title.Text = L.Format("Agents_Finished", AgentKinds.Name(session.Agent), session.Project);
        var message = Ui.Text(11, alpha: 0xA6);
        message.Text = session.LastMessage ?? L.Get("Agents_YourTurn");
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, message } };
        var icon = AgentLook.Icon(session.Agent, 28);
        icon.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid
        {
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            Children = { icon, text },
        };
        Grid.SetColumn(text, 1);
        Root.Children.Clear();
        Root.Children.Add(row);
    }
}

/// <summary>
/// The pill's ears for an agent (AgentViews.swift): its icon, then bars in its colour while it works,
/// a pulsing mark while it waits on the user, or a script's progress. All the motion is the compositor's.
/// </summary>
internal sealed class AgentEarView
{
    private readonly Grid _icon = new() { Width = 22, Height = 22 };
    private readonly Border _barsHost = new() { Width = AudioBars.Width, Height = 14, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly AudioBars _bars;
    private readonly FontIcon _attention = new() { Glyph = "", FontSize = 14, Foreground = new SolidColorBrush(AgentLook.Orange), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _percent = Ui.Text(11, semibold: true);
    private AgentEar? _ear;
    private bool _moving;

    public AgentEarView()
    {
        _bars = new AudioBars(_barsHost);
        _percent.HorizontalAlignment = HorizontalAlignment.Right;
        _percent.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        Leading = _icon;
        Trailing = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Children = { _barsHost, _attention, _percent } };
        SetShown(false, false);
    }

    public FrameworkElement Leading { get; }
    public FrameworkElement Trailing { get; }

    public void Show(AgentEar? ear)
    {
        // Narrowing keeps the last ear while it fades out.
        if (ear is null) return;
        var changed = _ear?.Agent != ear.Value.Agent || _ear?.Kind != ear.Value.Kind || _ear?.Script?.Icon != ear.Value.Script?.Icon;
        _ear = ear;
        if (changed)
        {
            _icon.Children.Clear();
            var icon = ear.Value.Agent is { } agent
                ? AgentLook.Icon(agent, 18)
                : new FontIcon { Glyph = AgentLook.ScriptGlyph(ear.Value.Script?.Icon ?? ""), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ui.White(0xFF) };
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            _icon.Children.Add(icon);
        }
        var kind = ear.Value.Kind;
        var progress = ear.Value.Script?.Progress;
        _bars.SetColor(ear.Value.Agent is { } tinted ? AgentLook.Tint(tinted) : Color.FromArgb(255, 255, 255, 255));
        _barsHost.Visibility = kind == AgentEarKind.Working || kind == AgentEarKind.Script && progress is null ? Visibility.Visible : Visibility.Collapsed;
        _attention.Visibility = kind == AgentEarKind.Attention ? Visibility.Visible : Visibility.Collapsed;
        _percent.Visibility = kind == AgentEarKind.Script && progress is not null ? Visibility.Visible : Visibility.Collapsed;
        if (progress is { } value) _percent.Text = L.Format("Devices_Percent", (int)Math.Round(value * 100));
        Animate();
    }

    /// <summary>In the ears; the bars and the pulse move only while <paramref name="seen"/>.</summary>
    public void SetShown(bool shown, bool seen)
    {
        Leading.Visibility = Trailing.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        _moving = shown && seen;
        Animate();
    }

    public void Stop()
    {
        _moving = false;
        Animate();
    }

    private void Animate()
    {
        if (_moving && _barsHost.Visibility == Visibility.Visible) _bars.Start();
        else _bars.Stop();
        var visual = ElementCompositionPreview.GetElementVisual(_attention);
        if (_moving && _attention.Visibility == Visibility.Visible)
        {
            var pulse = visual.Compositor.CreateScalarKeyFrameAnimation();
            pulse.InsertKeyFrame(0f, 1f);
            pulse.InsertKeyFrame(0.5f, 0.35f);
            pulse.InsertKeyFrame(1f, 1f);
            pulse.Duration = TimeSpan.FromMilliseconds(1200);
            pulse.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Opacity", pulse);
        }
        else
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = 1;
        }
    }
}
