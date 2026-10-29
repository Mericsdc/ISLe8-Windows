using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Settings;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.Calendar;
using ISLe8.Core.Calendar;
using ISLe8.Core.Notes;
using ISLe8.Core.Timer;
using ISLe8.Notes;
using ISLe8.ClipboardTab;
using ISLe8.Core.Shelf;
using ISLe8.ShelfTab;
using ISLe8.SystemTab;
using ISLe8.Core.Weather;
using ISLe8.Core.Status;
using ISLe8.Agents;
using ISLe8.Core.Agents;
using ISLe8.Devices;
using ISLe8.Media;
using ISLe8.Notifications;
using ISLe8.Status;
using ISLe8.Timer;
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
///    ├─ Clock         the time on the idle pill, if wanted
///    ├─ PillLayer     artwork + bars (and the song's title, if wanted), shown in the wide pill
///    ├─ AlertLayer    a banner under an empty top row, or the HUD and status ears in it (IslandView.swift, AlertView)
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
    private readonly FrameworkElement[] _mediaEar;
    private readonly AppSettings _settings;

    public IslandView(
        IslandTheme theme, AppSettings settings, MediaService media, IslandTimer timer, WeatherService weather, CalendarService calendar,
        NotesStore notes, SystemSampler system, ClipboardService clipboard, ShelfStore shelf, NotificationService notifications, DeviceService devices,
        AgentHub agents, Action<AgentApproval, bool?> decide, Action<int> goToSession, Usage.UsageService usage, SystemStatus status,
        nint window, DispatcherQueue queue,
        Action takeKeyboard, Action dismissAlert, Action close,
        Func<IReadOnlyList<Windows.Storage.IStorageItem>, Task> dropToShelf, Func<IReadOnlyList<Windows.Storage.IStorageItem>, Task> dropToShare)
    {
        _settings = settings;
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

        // The ears: the media's artwork and bars, or the timer's symbol and time.
        _mediaEar = [pillArtwork, barsHost];
        TimerEar = new TimerEar(timer, queue);
        TimerEar.Leading.HorizontalAlignment = HorizontalAlignment.Left;
        TimerEar.Trailing.HorizontalAlignment = HorizontalAlignment.Right;
        PillLayer = new Grid
        {
            Padding = new Thickness(5, 0, 14, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Children = { pillArtwork, barsHost, TimerEar.Leading, TimerEar.Trailing },
        };
        TimerEar.SetShown(false, seen: false);
        AgentEar = new AgentEarView();
        AgentEar.Leading.HorizontalAlignment = HorizontalAlignment.Left;
        PillLayer.Children.Add(AgentEar.Leading);
        PillLayer.Children.Add(AgentEar.Trailing);
        // Between the artwork and the bars, 8 from each.
        PillTitle = new PillTitle();
        PillTitle.Root.Margin = new Thickness(22 + 8, 0, AudioBars.Width + 8, 0);
        PillLayer.Children.Add(PillTitle.Root);
        Clock = new PillClock(queue);

        Home = new HomeView(media, status, timer, weather, calendar, settings, theme, queue, takeKeyboard);
        _tabs[IslandTab.Home] = Home.Root;
        Notes = new NotesTab(notes, theme, queue, takeKeyboard);
        _tabs[IslandTab.Notes] = Notes.Root;
        System = new SystemTabView(system, settings, theme);
        _tabs[IslandTab.System] = System.Root;
        Clipboard = new ClipboardTabView(clipboard, settings, theme, takeKeyboard, close);
        _tabs[IslandTab.Clipboard] = Clipboard.Root;
        Shelf = new ShelfTabView(shelf, () => settings.CopyFilesToShelf, window);
        _tabs[IslandTab.Shelf] = Shelf.Root;
        DropTarget = new DropTargetView(dropToShelf, dropToShare);
        Devices = new DevicesTabView(devices, theme);
        _tabs[IslandTab.Devices] = Devices.Root;
        Agents = new AgentsTabView(agents, usage, settings, theme, queue, decide, goToSession);
        _tabs[IslandTab.Agents] = Agents.Root;

        var content = new Grid { Padding = new Thickness(IslandSizes.ContentInset, IslandSizes.ContentTop, IslandSizes.ContentInset, 0) };
        foreach (var view in _tabs.Values)
        {
            view.Visibility = Visibility.Collapsed;
            content.Children.Add(view);
        }

        Header = new HeaderBar();
        // Battery, LAN, the watts, the devices: the devices chip goes first when they don't fit, then the watts.
        StatusChips = new StatusChips();
        Header.AddChip(StatusChips.Battery, drop: 0);
        Header.AddChip(StatusChips.Lan, drop: 0);
        Header.AddChip(StatusChips.Watts, drop: 2);
        DevicesChip = new DevicesChip(devices, () => settings.ShowDevicesChip);
        Header.AddChip(DevicesChip.Root, drop: 1);
        DevicesChip.Refreshed += () => Header.Want(DevicesChip.Root, DevicesChip.Wanted);
        Header.Want(DevicesChip.Root, DevicesChip.Wanted);
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

        Banner = new MediaBanner(media, theme);
        Banner.Root.Margin = new Thickness(14, 0, 14, 4);
        TimerBanner = new TimerBanner();
        TimerBanner.Root.Margin = new Thickness(14, 0, 14, 4);
        Grid.SetRow(TimerBanner.Root, 1);
        MeetingBanner = new MeetingBanner(dismissAlert);
        MeetingBanner.Root.Margin = new Thickness(14, 0, 14, 4);
        Grid.SetRow(MeetingBanner.Root, 1);
        NotificationBanner = new NotificationBanner(notifications);
        NotificationBanner.Root.Margin = new Thickness(14, 0, 14, 4);
        Grid.SetRow(NotificationBanner.Root, 1);
        DeviceBanner = new DeviceBanner();
        DeviceBanner.Root.Margin = new Thickness(14, 0, 14, 4);
        Grid.SetRow(DeviceBanner.Root, 1);
        ApprovalBanner = new ApprovalBanner(theme, decide);
        ApprovalBanner.Root.Margin = new Thickness(14, 0, 14, 6);
        Grid.SetRow(ApprovalBanner.Root, 1);
        AgentDoneBanner = new AgentDoneBanner();
        AgentDoneBanner.Root.Margin = new Thickness(14, 0, 14, 4);
        Grid.SetRow(AgentDoneBanner.Root, 1);
        UsageBanner = new Usage.UsageBanner(theme);
        UsageBanner.Root.Margin = new Thickness(14, 0, 14, 4);
        Grid.SetRow(UsageBanner.Root, 1);
        BatteryBanner = new BatteryBanner();
        BatteryBanner.Root.Margin = new Thickness(14, 0, 14, 4);
        Grid.SetRow(BatteryBanner.Root, 1);
        // The ears fill the pill's row; the row below stays empty.
        HudEars = new HudEars();
        StatusEars = new StatusEars();
        AlertLayer = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            RowDefinitions = { new RowDefinition { Height = new GridLength(IslandSizes.HeaderHeight) }, new RowDefinition() },
            Children = { Banner.Root, TimerBanner.Root, MeetingBanner.Root, NotificationBanner.Root, DeviceBanner.Root, ApprovalBanner.Root, AgentDoneBanner.Root, UsageBanner.Root, BatteryBanner.Root, HudEars.Root, StatusEars.Root },
        };
        Grid.SetRow(Banner.Root, 1);

        // A transparent (not null) background, so the whole column takes the pointer.
        Area = new Grid
        {
            Background = Ui.Clear(),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            AllowDrop = true,
            Children = { Clock.Root, PillLayer, AlertLayer, ExpandedLayer, DropTarget.Root },
        };

        Root = new Grid { Children = { BackgroundHost, Area } };
    }

    public Grid Root { get; }
    public Grid BackgroundHost { get; }
    public Grid Area { get; }
    public Grid PillLayer { get; }
    public Grid AlertLayer { get; }
    public Grid ExpandedLayer { get; }
    public HeaderBar Header { get; }
    public TabDock Dock { get; }
    public HomeView Home { get; }
    public NotesTab Notes { get; }
    public SystemTabView System { get; }
    public ClipboardTabView Clipboard { get; }
    public ShelfTabView Shelf { get; }
    public DropTargetView DropTarget { get; }
    public MediaBanner Banner { get; }
    public TimerBanner TimerBanner { get; }
    public MeetingBanner MeetingBanner { get; }
    public NotificationBanner NotificationBanner { get; }
    public DeviceBanner DeviceBanner { get; }
    public ApprovalBanner ApprovalBanner { get; }
    public AgentDoneBanner AgentDoneBanner { get; }
    public Usage.UsageBanner UsageBanner { get; }
    public BatteryBanner BatteryBanner { get; }
    public HudEars HudEars { get; }
    public StatusEars StatusEars { get; }
    public StatusChips StatusChips { get; }
    public AgentsTabView Agents { get; }
    public AgentEarView AgentEar { get; }
    public DevicesTabView Devices { get; }
    public DevicesChip DevicesChip { get; }
    public TimerEar TimerEar { get; }
    public AudioBars Bars { get; }
    public PillTitle PillTitle { get; }
    public PillClock Clock { get; }

    public void ApplyLayout(IslandLayout layout, DipRect open, HomeStyle style, IslandTab tab)
    {
        Place(Area, layout.Area);
        Place(PillLayer, layout.CompactPill);
        Place(Clock.Root, layout.IdlePill);
        SetOpen(open, style, tab);
        // On the taskbar a banner's empty pill row is at its bottom, where the pill was.
        var bottom = layout.FromBottom;
        AlertLayer.RowDefinitions[0].Height = bottom ? new GridLength(1, GridUnitType.Star) : new GridLength(IslandSizes.HeaderHeight);
        AlertLayer.RowDefinitions[1].Height = bottom ? new GridLength(IslandSizes.HeaderHeight) : new GridLength(1, GridUnitType.Star);
        foreach (var child in AlertLayer.Children)
        {
            var ear = child == HudEars.Root || child == StatusEars.Root;
            Grid.SetRow((FrameworkElement)child, ear == bottom ? 1 : 0);
        }
    }

    /// <summary>The ears show <paramref name="activity"/>: media's artwork and bars, or the timer, which ticks while <paramref name="seen"/>.</summary>
    public void ShowEar(PillActivity activity, bool seen)
    {
        foreach (var part in _mediaEar) part.Visibility = activity is PillActivity.Media or PillActivity.None ? Visibility.Visible : Visibility.Collapsed;
        PillTitle.Root.Visibility = activity is PillActivity.Media or PillActivity.None && _settings.ShowPillTitle ? Visibility.Visible : Visibility.Collapsed;
        TimerEar.SetShown(activity == PillActivity.Timer, seen);
        AgentEar.SetShown(activity == PillActivity.Agent, seen);
    }

    /// <summary>
    /// The banner for <paramref name="alert"/>: one content at a time. <paramref name="swapped"/>: it
    /// took another's place on screen, so a HUD eases its bar from where it was.
    /// </summary>
    public void ShowAlert(IslandAlert? alert, int pendingNotifications, bool swapped, NetworkLink link)
    {
        var wasHud = HudEars.Root.Visibility == Visibility.Visible;
        HudEars.Root.Visibility = alert is HudAlert ? Visibility.Visible : Visibility.Collapsed;
        StatusEars.Root.Visibility = alert.IsStatus() ? Visibility.Visible : Visibility.Collapsed;
        BatteryBanner.Root.Visibility = alert is BatteryAlert && !alert.IsStatus() ? Visibility.Visible : Visibility.Collapsed;
        Banner.Root.Visibility = alert is null or NowPlayingAlert ? Visibility.Visible : Visibility.Collapsed;
        if (alert is HudAlert hud) HudEars.Show(hud.Value, animate: swapped && wasHud);
        else if (alert is BatteryAlert { Event.IsStatus: true } battery) ShowStatus(battery.Event);
        else if (alert is BatteryAlert low) BatteryBanner.Show(low.Event);
        else if (alert is WiredAlert wired) ShowWired(wired.Connected, link);
        TimerBanner.Root.Visibility = alert is TimerAlert ? Visibility.Visible : Visibility.Collapsed;
        MeetingBanner.Root.Visibility = alert is MeetingAlert ? Visibility.Visible : Visibility.Collapsed;
        NotificationBanner.Root.Visibility = alert is NotificationAlert ? Visibility.Visible : Visibility.Collapsed;
        DeviceBanner.Root.Visibility = alert is DeviceAlert ? Visibility.Visible : Visibility.Collapsed;
        ApprovalBanner.Root.Visibility = alert is ApprovalAlert ? Visibility.Visible : Visibility.Collapsed;
        AgentDoneBanner.Root.Visibility = alert is AgentDoneAlert ? Visibility.Visible : Visibility.Collapsed;
        UsageBanner.Root.Visibility = alert is UsageAlert ? Visibility.Visible : Visibility.Collapsed;
        if (alert is TimerAlert timer) TimerBanner.Show(timer.Event);
        else if (alert is MeetingAlert meeting) MeetingBanner.Show(meeting.Entry);
        else if (alert is NotificationAlert notification) NotificationBanner.Show(notification.Notification, pendingNotifications);
        else if (alert is DeviceAlert device) DeviceBanner.Show(device.Device, device.IsLow);
        else if (alert is ApprovalAlert approval) ApprovalBanner.Show(approval.Approval);
        else if (alert is AgentDoneAlert done) AgentDoneBanner.Show(done.Session);
        else if (alert is UsageAlert usage) UsageBanner.Show(usage.Snapshot, usage.Window);
        else if (alert is null or NowPlayingAlert) Banner.Refresh();
        // Only a banner with a button takes the pointer itself; otherwise a click opens the island.
        AlertLayer.IsHitTestVisible = alert is MeetingAlert && MeetingBanner.HasJoin || alert is ApprovalAlert;
    }

    /// <summary>Plugged in (green, charging) or out (white): the percentage on the right (IslandView.swift, AlertView).</summary>
    private void ShowStatus(BatteryEvent battery)
    {
        var plugged = battery.Kind == BatteryEventKind.PluggedIn;
        StatusEars.Show(
            plugged ? StatusGlyphs.BatteryCharging : StatusGlyphs.Battery(75),
            14 * StatusGlyphs.BatteryScale,
            L.Format("Devices_Percent", battery.Percent),
            L.Get(plugged ? "Devices_Charging" : "Devices_OnBattery"),
            BatteryLook.Event(battery.Kind));
    }

    /// <summary>A cable in says LAN in green; out, in white, what took over: Wi-Fi, or offline.</summary>
    private void ShowWired(bool connected, NetworkLink link)
    {
        if (connected)
        {
            StatusEars.Show(StatusGlyphs.Cable, 14, "LAN", L.Get("Status_CableIn"), StatusTint.Green);
            return;
        }
        var (glyph, text) = WiredOut(link);
        StatusEars.Show(glyph, 14, text, L.Get("Status_CableOut"), StatusTint.White);
    }

    /// <summary>The cable-out ears follow the link as it settles, Wi-Fi a moment after offline.</summary>
    public void UpdateWiredOut(NetworkLink link)
    {
        var (glyph, text) = WiredOut(link);
        StatusEars.SetText(glyph, text);
    }

    private static (string Glyph, string Text) WiredOut(NetworkLink link) =>
        link == NetworkLink.Offline ? (Glyphs.WifiOff, L.Get("Status_Offline")) : (Glyphs.Wifi, "Wi-Fi");

    /// <summary>The header's battery, LAN and watts chips, as the settings and the readings say.</summary>
    public void ShowStatusChips(AppSettings settings, SystemStatus status)
    {
        if (status.Battery is { } battery) StatusChips.ShowBattery(battery);
        Header.Want(StatusChips.Battery, settings.ShowBatteryChip && status.Battery is not null);
        Header.Want(StatusChips.Lan, settings.ShowNetworkChip && status.Network.Link == NetworkLink.Wired);
        if (status.Watts.Watts is { } watts) StatusChips.ShowWatts(watts);
        Header.Want(StatusChips.Watts, settings.ShowBatteryChip && status.Watts.Watts is not null);
    }

    /// <summary>Where the drop target's zones sit while files are dragged onto the island.</summary>
    public void SetDropTarget(DipRect rect) => Place(DropTarget.Root, rect);

    /// <summary>Where the banner sits while one shows.</summary>
    public void SetAlert(DipRect alert) => Place(AlertLayer, alert);

    /// <summary>Home's columns across the open island, inside its margins.</summary>
    public void SetHomeColumns(HomeStyle style, IReadOnlyList<string> columns, double islandWidth)
    {
        var inset = IslandSizes.HomeInset(style);
        // The content already sits ContentInset in; the strips go a little further.
        Home.Root.Margin = new Thickness(inset - IslandSizes.ContentInset, 0, inset - IslandSizes.ContentInset, 0);
        Home.Build(style, columns, islandWidth - 2 * inset);
    }

    /// <summary>The open island's outline for <paramref name="tab"/>: the dock sits under that tab's content.</summary>
    public void SetOpen(DipRect open, HomeStyle style, IslandTab tab)
    {
        Place(ExpandedLayer, open);
        _contentRow.Height = new GridLength(IslandSizes.ContentTop + IslandSizes.ContentHeight(style, tab));
        Header.SetIslandWidth(open.Width);
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
        Home.SetArtwork(source);
        Banner.SetArtwork(source);
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
