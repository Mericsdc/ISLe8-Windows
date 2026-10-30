using ISLe8.Core.Diagnostics;
using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Media;
using ISLe8.Core.Calendar;
using ISLe8.ClipboardTab;
using ISLe8.Core.Clipboard;
using ISLe8.Core.Notes;
using ISLe8.Core.Shelf;
using ISLe8.ShelfTab;
using ISLe8.SystemTab;
using ISLe8.Core.Timer;
using ISLe8.Core.Weather;
using ISLe8.Interop;
using ISLe8.Core.Motion;
using ISLe8.Core.Settings;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.Agents;
using ISLe8.Core.Agents;
using ISLe8.Devices;
using ISLe8.Core.Devices;
using ISLe8.Media;
using ISLe8.Notifications;
using ISLe8.Shell;
using ISLe8.Status;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ISLe8.Island;

/// <summary>
/// Draws the island in whatever state <see cref="IslandModel"/> puts it: pill, peek, a banner or
/// open on one of its tabs, the pill widening while music plays. Feeds the model pointer, menu, dock
/// and media events, and rebuilds the view when the theme or language changes.
/// </summary>
internal sealed class IslandController : IDisposable
{
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly DispatcherQueue _queue;
    private readonly MediaService _media;
    private readonly IslandWindow _window;
    private readonly IslandModel _model;
    private readonly OutsideClickWatcher _outsideClicks;
    /// <summary>Where the open island takes the hover, with the slack when it gets shorter.</summary>
    private readonly HoverOutline _outline = new();
    private readonly SwipeTracker _swipe = new();
    private IslandView _view = null!;
    private IslandSurface _surface = null!;
    /// <summary>False while the shape is still moving after it shrank.</summary>
    private bool _settled = true;
    /// <summary>The banner's shape last sprung to, so a HUD moving its bar doesn't spring again.</summary>
    private DipRect _alertShape;
    private readonly TrackChangeDetector _trackChanges = new();
    private readonly IslandTimer _timer;
    private readonly WeatherService _weather;
    private readonly CalendarService _calendar;
    private readonly NotesStore _notes;
    private readonly SystemSampler _system;
    private readonly ClipboardService _clipboard;
    private readonly ShelfStore _shelf;
    private readonly NotificationService _notifications;
    private readonly DeviceService _devices;
    private readonly AgentHub _agents;
    private readonly AgentServer _agentServer;
    private readonly Usage.UsageService _usage;
    private readonly SystemStatus _status;
    private readonly MemoryTrim _trim;
    private readonly Updater _updater;
    private HotKey? _clipboardKey;
    private nint _foregroundBeforeKeyboard;

    /// <summary>One client for the app's few requests (weather now; more later), as .NET recommends.</summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private nint _foregroundBeforeMenu;
    private string? _artworkKey;

    public IslandController(SettingsStore store)
    {
        _store = store;
        _settings = store.Load();
        _queue = DispatcherQueue.GetForCurrentThread();
        L.Use(_settings.Language);

        _media = new MediaService(_queue);
        // The window is as big as the largest tab in the current layout; it never resizes while open.
        _window = new IslandWindow(_settings, () => IslandSizes.Largest(_settings));
        var clock = new DispatcherClock(_queue);
        _model = new IslandModel(_settings, clock);
        _timer = new IslandTimer(_settings, clock);
        _weather = new WeatherService(_settings, Http, () => DateTimeOffset.Now, TimeZoneInfo.Local.Id);
        _calendar = new CalendarService(_settings, Http, clock);
        _notes = new NotesStore(AppPaths.DataFolder, () => DateTimeOffset.Now);
        _system = new SystemSampler(_queue);
        _clipboard = new ClipboardService(_settings, new ClipboardHistory(AppPaths.DataFolder), _window.Hwnd, _queue);
        _shelf = new ShelfStore(AppPaths.DataFolder, new NtfsFileLocator(), () => DateTimeOffset.Now);
        _notifications = new NotificationService(_settings, _queue, () => _store.Save(_settings));
        _devices = new DeviceService(_queue, clock);
        _status = new SystemStatus(_settings, _devices, _queue);
        _trim = new MemoryTrim(_queue);
        _updater = new Updater(_settings, () => _store.Save(_settings), _queue, () => ((App)Application.Current).Quit());
        _agents = new AgentHub(_settings, clock);
        _agentServer = new AgentServer(_agents, _queue);
        _usage = new Usage.UsageService(_settings, _queue, clock, AppPaths.DataFolder);
        _outsideClicks = new OutsideClickWatcher(_queue, OnClickedOutside);
    }

    /// <summary>The weather, for Settings › Widgets.</summary>
    public WeatherService Weather => _weather;

    /// <summary>The calendars, for Settings › Widgets.</summary>
    public CalendarService Calendar => _calendar;

    /// <summary>Updates from GitHub, for Settings › General and the tray menu.</summary>
    public Updater Updater => _updater;

    /// <summary>The notification mirror, for Settings › Notifications.</summary>
    public NotificationService Notifications => _notifications;

    /// <summary>Settings › Agents › Usage limits.</summary>
    public void ApplyUsageSettings() => _usage.ApplySettings();

    /// <summary>Settings › System › Wireless devices: the header's chip shows at once.</summary>
    public void ApplyDeviceSettings() => _view.DevicesChip.Refresh();

    /// <summary>Settings › System › HUDs and Battery, Appearance › Header: the chips show at once.</summary>
    public void ApplyStatusSettings()
    {
        _status.ApplySettings();
        _view.ShowStatusChips(_settings, _status);
    }

    /// <summary>The clipboard history, for Settings › Clipboard.</summary>
    public ClipboardService Clipboard => _clipboard;

    /// <summary>Settings › Clipboard: recording on or off, the limits.</summary>
    public void ApplyClipboardSettings()
    {
        _clipboard.ApplySettings();
        _clipboard.History.Prune(_settings.ClipboardLimit, TimeSpan.FromDays(_settings.ClipboardMaxAgeDays), DateTimeOffset.Now);
        _clipboard.Reload();
    }

    /// <summary>The one settings object; the settings window changes it in place.</summary>
    public AppSettings Settings => _settings;

    /// <summary>The island's window, which also takes the tray icon's messages.</summary>
    public nint WindowHandle => _window.Hwnd;

    public void AddWindowMessageHandler(Func<uint, nint, nint, nint?> handler) => _window.AddMessageHandler(handler);

    /// <summary>Moves the island to the screen the settings ask for now.</summary>
    public void PlaceIsland() => _window.Place();

    /// <summary>Settings › Appearance › Theme.</summary>
    public void ApplyTheme() => Rebuild();

    /// <summary>Settings › Appearance › Home layout or width: the window is sized again for the largest tab.</summary>
    public void ApplyLayoutSettings() => _window.Place();

    /// <summary>Settings › Layout › Tabs.</summary>
    public void ApplyTabs()
    {
        _model.TabsChanged();
        _view.Dock.SetTabs(_model.VisibleTabs, _model.Tab);
    }

    /// <summary>Settings › Layout › Home columns. The strips are as wide as their columns, so the window is placed again.</summary>
    public void ApplyHomeColumns() => _window.Place();

    /// <summary>Settings › Media.</summary>
    public void ApplyMediaSettings() => UpdateBars();

    /// <summary>Settings › Appearance › Island and General: where the island sits, its strip, the clock and the title.</summary>
    public void ApplyIslandSettings()
    {
        _window.Place();
        UpdateClock(animate: false);
        UpdateBars();
    }

    /// <summary>Settings › General › Hide in full screen.</summary>
    public void ApplyFullscreenSetting() => _window.ApplyFullscreenSetting();

    /// <summary>Settings › Appearance › Hide in screenshots and screen sharing.</summary>
    public void ApplyCapture() => _window.SetExcludedFromCapture(_settings.HideFromScreenCapture);

    public void Start()
    {
        Rebuild();
        _model.StateChanged += OnStateChanged;
        _model.PillChanged += OnPillChanged;
        _model.TabChanged += OnTabChanged;
        _model.Changed += OnModelChanged;
        _model.AlertChanged += OnAlertChanged;
        _model.PendingNotificationsChanged += () => _view.NotificationBanner.SetPending(_model.PendingNotificationCount);
        _timer.Changed += OnTimerChanged;
        _timer.PhaseEnded += OnTimerPhaseEnded;
        _calendar.MeetingSoon += entry =>
        {
            if (!_window.IsHiddenForFullscreen) _model.ShowAlert(new MeetingAlert(entry), AlertDurations.Meeting);
        };
        _ = _calendar.Start();
        _notifications.Arrived += notification =>
        {
            if (!_window.IsHiddenForFullscreen) _model.ShowNotification(notification);
        };
        _notifications.ApplySettings();
        _devices.Alert += (device, isLow) =>
        {
            if (_window.IsHiddenForFullscreen || !(isLow ? _settings.ShowDeviceLowBatteryAlert : _settings.ShowDeviceConnectedAlert)) return;
            _model.ShowAlert(new DeviceAlert(device, isLow), isLow ? DeviceAlertDurations.Low : DeviceAlertDurations.Connected);
        };
        _devices.Start();
        StartStatus();
        _agents.Changed += OnAgentsChanged;
        _agents.ApprovalRequested += approval =>
        {
            if (!_window.IsHiddenForFullscreen) _model.ShowAlert(new ApprovalAlert(approval), AlertDurations.Approval);
        };
        _agents.SessionFinished += session =>
        {
            if (!_window.IsHiddenForFullscreen) _model.ShowAlert(new AgentDoneAlert(session), AlertDurations.AgentDone);
        };
        // isle8 open [tab] and isle8 close.
        _agents.IslandCommand += (action, tab) =>
        {
            if (action == "close") _model.Close();
            // Hidden behind a full-screen app, it would open unseen.
            else if (!_window.IsHiddenForFullscreen) _model.Open(IslandTabs.Parse(tab));
        };
        // The answer is what was last read (DevicesChip.summary).
        _agents.DevicesQuery = () => _devices.Devices.Select(BatteryColors.Summary).ToList();
        // A module that needs the user wins the tab the island opens on (AppDelegate.swift).
        _model.PreferredTab = () => _agents.NeedsAttention ? IslandTab.Agents : null;
        _ = AgentLook.LoadAsync();
        _agentServer.Start();
        _usage.LimitWarning += (snapshot, window) =>
        {
            if (!_window.IsHiddenForFullscreen) _model.ShowAlert(new UsageAlert(snapshot, window), AlertDurations.UsageLimit);
        };
        // Nothing asks a CLI while Windows sleeps.
        _window.AddMessageHandler((message, wParam, _) =>
        {
            if (message == 0x0218 /* WM_POWERBROADCAST */)
            {
                if (wParam == 4 /* PBT_APMSUSPEND */) _usage.SetSleeping(true);
                else if (wParam is 7 or 0x12 /* PBT_APMRESUMESUSPEND, PBT_APMRESUMEAUTOMATIC */) _usage.SetSleeping(false);
            }
            return null;
        });
        _usage.Start();
        // Windows asks once, the first time the installed ISLe8 starts with the mirror on.
        if (_notifications.Access == NotificationAccess.NotAsked) _ = _notifications.RequestAccessAsync();
        // A locked session shows no banners and keeps none for later (IslandModel.swift, setLocked).
        Win32.WTSRegisterSessionNotification(_window.Hwnd, Win32.NOTIFY_FOR_THIS_SESSION);
        _window.AddMessageHandler((message, wParam, _) =>
        {
            if (message == Win32.WM_WTSSESSION_CHANGE && wParam is Win32.WTS_SESSION_LOCK or Win32.WTS_SESSION_UNLOCK)
            {
                _model.SetLocked(wParam == Win32.WTS_SESSION_LOCK);
            }
            return null;
        });
        _window.AddMessageHandler(_clipboard.OnMessage);
        _clipboard.ApplySettings();
        // Ctrl+Alt+V opens the clipboard from anywhere, its search ready for typing (PORTING.md §3.9).
        _clipboardKey = new HotKey(_window.Hwnd, 0x4953, 0x56, () =>
        {
            // Not over a full-screen game or video: the island is hidden there, and its search would take the keyboard from it.
            if (_window.IsHiddenForFullscreen) return;
            _model.Toggle(IslandTab.Clipboard);
            if (_model.State == IslandState.Expanded) _view.Clipboard.FocusSearch();
        });
        _window.AddMessageHandler(_clipboardKey.OnMessage);
        ApplyCapture();
        _window.LayoutChanged += ApplyLayout;
        _window.FullscreenChanged += _ => UpdateBars();
        _window.Show();
        // Once starting up (the usage CLIs, the first layout) is over.
        _trim.Schedule();
        _updater.Start();

        _media.Changed += OnMediaChanged;
        _ = _media.StartAsync();
    }

    public void Dispose()
    {
        _outsideClicks.Dispose();
        _clipboardKey?.Dispose();
        _notifications.Dispose();
        _devices.Dispose();
        _agents.Stop();
        _agentServer.Dispose();
        _usage.Dispose();
        _status.Dispose();
        Win32.WTSUnRegisterSessionNotification(_window.Hwnd);
        StopView();
        _media.Dispose();
        _window.Dispose();
    }

    // MARK: Building

    /// <summary>Builds the view for the current theme and language and shows it in its current state.</summary>
    private void Rebuild()
    {
        var theme = _settings.Theme;
        Ui.Font = Ui.FontFor(theme.Typeface);

        // The old view's clock and bars would otherwise keep running, detached from the window.
        if (_view is not null) StopView();

        _view = new IslandView(
            theme, _settings, _media, _timer, _weather, _calendar, _notes, _system, _clipboard, _shelf, _notifications, _devices,
            _agents, DecideApproval, GoToSession, _usage, _status, _window.Hwnd, _queue,
            takeKeyboard: TakeKeyboard,
            dismissAlert: _model.DismissAlert,
            close: _model.Close,
            dropToShelf: async items =>
            {
                await ShelfActions.AddDropped(items, _shelf, _settings.CopyFilesToShelf);
                _model.ShowShelfAfterDrop();
            },
            dropToShare: items =>
            {
                _model.Close();
                ShelfActions.Share(items, _window.Hwnd, L.Get("Shelf_Title"));
                return Task.CompletedTask;
            });
        _view.Area.PointerEntered += (_, e) => UpdateHover(e, touchInside: true);
        _view.Area.PointerMoved += (_, e) => UpdateHover(e, touchInside: true);
        _view.Area.PointerExited += (_, e) => UpdateHover(e, touchInside: false);
        _view.Area.PointerPressed += OnPointerPressed;
        _view.Area.PointerWheelChanged += OnPointerWheelChanged;
        // Files dragged onto the pill open the drop target (PORTING.md §3.10).
        _view.Area.DragEnter += (_, e) =>
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) return;
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            _model.SetFileDragNearby(true);
        };
        _view.Area.DragOver += (_, e) =>
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems)) e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        };
        // A leave also bubbles up from a zone; only leaving the island itself counts.
        _view.Area.DragLeave += (_, _) => _queue.TryEnqueue(() =>
        {
            if (_model.State == IslandState.DropTarget && !CursorOnIsland()) _model.SetFileDragNearby(false);
        });
        _view.Area.Drop += (_, _) => _queue.TryEnqueue(() => _model.SetFileDragNearby(false));
        _view.Area.ContextFlyout = BuildMenu();
        _view.Dock.SetTabs(_model.VisibleTabs, _model.Tab);
        _view.Dock.Chosen += _model.Select;
        _view.Header.SetPinned(_model.IsPinned);
        _view.Header.PinClicked += () => _model.SetPinned(!_model.IsPinned);
        _view.Header.SettingsClicked += () =>
        {
            // As on the Mac: the island steps aside for the window it opened.
            ((App)Application.Current).OpenSettings();
            _model.SetPinned(false);
            _model.Close();
        };
        _view.ShowTab(_model.Tab);
        _view.Root.Loaded += (_, _) =>
        {
            if (_view.Root.XamlRoot is { } root) root.Changed += OnXamlRootChanged;
        };

        _surface = new IslandSurface(_view.BackgroundHost, _view.Area);
        _surface.SetTheme(theme);
        _window.Window.Content = _view.Root;

        _view.Banner.Refresh();
        _view.ShowStatusChips(_settings, _status);
        if (_window.Layout is { } layout) ApplyLayout(layout);
        _artworkKey = null;
        LoadArtwork();
    }

    private void StopView()
    {
        _view.Home.Detach();
        _view.Notes.Detach();
        _view.System.Detach();
        _view.Clipboard.Detach();
        _view.Shelf.Detach();
        _view.Devices.Detach();
        _view.Agents.Detach();
        _view.AgentEar.Stop();
        _view.DevicesChip.Detach();
        _view.Clock.Stop();
        _view.Bars.Stop();
        _view.Banner.Bars.Stop();
        _view.TimerEar.Stop();
    }

    /// <summary>A new monitor, DPI, layout or view: everything jumps to where it belongs, without animating.</summary>
    private void ApplyLayout(IslandLayout layout)
    {
        var state = _model.State;
        var expanded = state == IslandState.Expanded;
        var alert = state == IslandState.Alert;
        var dropTarget = state == IslandState.DropTarget;
        var shape = Shape(layout, state);
        var open = OpenRect(layout);
        _settled = true;
        _view.ApplyLayout(layout, open, _settings.HomeStyle, _model.Tab);
        _view.SetHomeColumns(_settings.HomeStyle, _settings.HomeTiles, open.Width);
        _alertShape = AlertRect(layout);
        _view.SetAlert(_alertShape);
        _view.SetDropTarget(layout.Open(IslandSizes.DropTarget));
        _surface.SetLayout(layout);
        _surface.JumpTo(shape, Corner(shape, state), shadow: expanded, Shoulder(state));
        _surface.SetOpen(expanded || alert || dropTarget, animate: false);
        _window.SetHitRegion(Region(layout, state));
        _outline.Reset(layout.OpenRegion(open));

        _view.ExpandedLayer.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        _view.ExpandedLayer.IsHitTestVisible = expanded;
        IslandSurface.SetContentShown(_view.ExpandedLayer, expanded);
        _view.AlertLayer.Visibility = alert ? Visibility.Visible : Visibility.Collapsed;
        IslandSurface.SetOpacity(_view.AlertLayer, alert ? 1 : 0);
        _view.DropTarget.Root.Visibility = dropTarget ? Visibility.Visible : Visibility.Collapsed;
        IslandSurface.SetOpacity(_view.DropTarget.Root, dropTarget ? 1 : 0);
        IslandSurface.SetOpacity(_view.PillLayer, state is IslandState.Pill or IslandState.Peek && _model.Pill == PillKind.Compact ? 1 : 0);
        UpdateClock(animate: false);
        UpdatePlayer();
        UpdateBars();
    }

    /// <summary>The open island for the current tab in the current layout.</summary>
    private DipRect OpenRect(IslandLayout layout) => layout.Open(IslandSizes.Open(_settings, _model.Tab));

    /// <summary>The banner for the current alert, centred under the pill's row.</summary>
    private DipRect AlertRect(IslandLayout layout) => layout.Open(IslandSizes.Alert(_model.Alert ?? new NowPlayingAlert()));

    /// <summary>Home only draws, and its clock only ticks, while it can be seen: open, on Home.</summary>
    private bool HomeShown => _model.State == IslandState.Expanded && _model.Tab == IslandTab.Home;

    /// <summary>What is on screen is live: Home's columns, and the System tab's sampling.</summary>
    private void UpdatePlayer()
    {
        _view.Home.SetActive(HomeShown);
        _view.System.SetActive(_model.State == IslandState.Expanded && _model.Tab == IslandTab.System);
        var expanded = _model.State == IslandState.Expanded;
        var devices = expanded && _model.Tab == IslandTab.Devices;
        _view.Devices.SetActive(devices);
        _view.Agents.SetActive(expanded && _model.Tab == IslandTab.Agents);
        _devices.SetVisible(expanded);
        _usage.SetVisible(expanded);
        _status.SetVisible(expanded);
        _devices.SetTabShown(devices);
    }

    /// <summary>
    /// XAML draws at the window's DPI, the layout uses the monitor's. They should agree after every
    /// WM_DPICHANGED; if Windows skipped one (say, while the window was hidden), place it again.
    /// </summary>
    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (_window.Layout is not { } layout || Math.Abs(sender.RasterizationScale - layout.Scale) < 0.01) return;
        Log.Info($"XAML scale {sender.RasterizationScale} differs from monitor scale {layout.Scale}; placing again");
        _window.RequestPlace();
    }

    // MARK: States

    private DipRect Shape(IslandLayout layout, IslandState state) => state switch
    {
        IslandState.Expanded => OpenRect(layout),
        IslandState.Alert => AlertRect(layout),
        IslandState.DropTarget => layout.Open(IslandSizes.DropTarget),
        IslandState.Peek => layout.Peek(_model.Pill),
        _ => layout.Pill(_model.Pill),
    };

    /// <summary>
    /// Pills and the peek are fully round at the ends; the open island follows the theme's corners.
    /// Flush with the top, the island is the Mac's notch shape and takes its bottom corners.
    /// </summary>
    private double Corner(DipRect shape, IslandState state) => _window.Layout is { Flush: true }
        ? IslandSizes.FlushRadius(state, _model.Pill, _settings.Theme)
        : state switch
    {
        IslandState.Expanded => IslandSizes.CornerRadius(_settings.Theme),
        // The HUD and the status ears are the pill's height: round at the ends, like the pill.
        IslandState.Alert => _model.Alert.IsEars() ? shape.Height / 2 : IslandSizes.AlertCornerRadius,
        IslandState.DropTarget => IslandSizes.CornerRadius(_settings.Theme),
        _ => shape.Height / 2,
    };

    /// <summary>The notch shape's shoulders when flush with the top; none otherwise.</summary>
    private double Shoulder(IslandState state) => _window.Layout is { Flush: true } ? IslandSizes.FlushShoulder(state, _model.Pill) : 0;

    /// <summary>The part of the window that takes the mouse in <paramref name="state"/>, in window pixels.</summary>
    private PixelRect Region(IslandLayout layout, IslandState state)
    {
        var region = state switch
        {
            IslandState.Expanded => layout.ExpandedRegion,
            IslandState.Alert => layout.OpenRegion(AlertRect(layout)),
            IslandState.DropTarget => layout.OpenRegion(layout.Open(IslandSizes.DropTarget)),
            IslandState.Peek => layout.PeekRegion(_model.Pill),
            _ => layout.PillRegion(_model.Pill),
        };
        // The window region also clips what is drawn: the notch shape's shoulders reach past the shape.
        var shoulder = (int)Math.Ceiling(Shoulder(state) * layout.Scale);
        if (shoulder == 0 || state == IslandState.Expanded) return region;
        return PixelRect.FromEdges(Math.Max(region.X - shoulder, 0), region.Y, Math.Min(region.Right + shoulder, layout.Window.Width), region.Bottom);
    }

    /// <summary>The part of the window that counts as "on the island" right now: only the island itself.</summary>
    private PixelRect? HoverRegion => _window.Layout is not { } layout
        ? null
        : _model.State switch
        {
            IslandState.Expanded => layout.OpenRegion(OpenRect(layout)),
            IslandState.Alert => layout.OpenRegion(AlertRect(layout)),
            IslandState.DropTarget => layout.OpenRegion(layout.Open(IslandSizes.DropTarget)),
            IslandState.Peek => layout.PeekRegion(_model.Pill),
            _ => layout.PillRegion(_model.Pill),
        };

    /// <summary>Whether the cursor is on the island; while open, through the outline and its slack.</summary>
    private bool CursorOnIsland()
    {
        if (!_window.CursorPoint(out var x, out var y)) return false;
        if (_model.State == IslandState.Expanded) return _outline.Contains(x, y);
        return HoverRegion is { } region && region.Contains(x, y);
    }

    private void OnStateChanged(IslandState from)
    {
        if (_window.Layout is not { } layout) return;
        var state = _model.State;
        var shape = Shape(layout, state);
        _settled = false;

        if (state != IslandState.Expanded) ReturnKeyboard();
        if (state == IslandState.Expanded) _trim.Cancel();
        // Whatever happened to the window's place meanwhile, what opens is drawn above every window.
        if (state != IslandState.Pill) _window.BringToTop();
        if (state == IslandState.Expanded)
        {
            _window.SetHitRegion(layout.ExpandedRegion);
            _outline.Reset(layout.OpenRegion(shape));
            if (_view.ExpandedLayer.Visibility == Visibility.Collapsed)
            {
                // Back from fully closed; mid-close, the content grows back from where it is.
                _view.ExpandedLayer.Visibility = Visibility.Visible;
                IslandSurface.SetContentShown(_view.ExpandedLayer, false);
            }
            _view.ExpandedLayer.IsHitTestVisible = true;
            _surface.Reveal(_view.ExpandedLayer, show: true);
            _surface.Fade(_view.PillLayer, 0, TimeSpan.Zero, MotionTimings.ContentFadeOut);
        }
        else if (state == IslandState.Alert)
        {
            _view.ShowAlert(_model.Alert, _model.PendingNotificationCount, swapped: false, _status.Network.Link);
            _alertShape = shape;
            _view.SetAlert(shape);
            _view.AlertLayer.Visibility = Visibility.Visible;
            _surface.Fade(_view.AlertLayer, 1, MotionTimings.ContentDelay, MotionTimings.ContentFadeIn);
            _surface.Fade(_view.PillLayer, 0, TimeSpan.Zero, MotionTimings.ContentFadeOut);
        }
        else if (state == IslandState.DropTarget)
        {
            _view.SetDropTarget(shape);
            _view.DropTarget.Root.Visibility = Visibility.Visible;
            _view.DropTarget.Root.IsHitTestVisible = true;
            _surface.Fade(_view.DropTarget.Root, 1, MotionTimings.ContentDelay, MotionTimings.ContentFadeIn);
            _surface.Fade(_view.PillLayer, 0, TimeSpan.Zero, MotionTimings.ContentFadeOut);
        }
        if (from == IslandState.Expanded && state != IslandState.Expanded)
        {
            _view.ExpandedLayer.IsHitTestVisible = false;
            _surface.Reveal(_view.ExpandedLayer, show: false);
        }
        // A layer on its way out takes no more clicks, even while it fades over the open island.
        if (from == IslandState.Alert)
        {
            _view.AlertLayer.IsHitTestVisible = false;
            _surface.Fade(_view.AlertLayer, 0, TimeSpan.Zero, MotionTimings.ContentFadeOut);
        }
        if (from == IslandState.DropTarget)
        {
            _view.DropTarget.Root.IsHitTestVisible = false;
            _surface.Fade(_view.DropTarget.Root, 0, TimeSpan.Zero, MotionTimings.ContentFadeOut);
        }
        if (state == IslandState.Pill && from is IslandState.Expanded or IslandState.Alert or IslandState.DropTarget && _model.Pill == PillKind.Compact)
        {
            _surface.Fade(_view.PillLayer, 1, MotionTimings.ContentDelay, MotionTimings.ContentFadeIn);
        }

        // Growing takes the bigger mouse region now; shrinking keeps it until the shape has settled.
        if (state is IslandState.Peek or IslandState.Alert or IslandState.DropTarget) _window.SetHitRegion(Region(layout, state));
        _surface.SetOpen(state is IslandState.Expanded or IslandState.Alert or IslandState.DropTarget, animate: true);
        _surface.MorphTo(shape, Corner(shape, state), Spring(), shadow: state == IslandState.Expanded, Settle, Shoulder(state));
        UpdateClock(animate: true);
        UpdatePlayer();
        UpdateBars();
    }

    /// <summary>
    /// A tab from the dock, a swipe, or the tab the island reopens on. In the wide and compact
    /// layouts every tab has the island's size, so only the content crossfades; in the minimal one
    /// Home is a strip and the island springs between the two heights.
    /// </summary>
    private void OnTabChanged(IslandTab from)
    {
        var tab = _model.Tab;
        _view.Dock.SetSelected(tab);
        if (_window.Layout is not { } layout) return;
        var open = OpenRect(layout);
        var before = layout.Open(IslandSizes.Open(_settings, from));
        _view.SetOpen(open, _settings.HomeStyle, tab);

        if (_model.State != IslandState.Expanded)
        {
            // Closed: the next opening simply shows it. A tab left with a crossfade kept its visual at 0.
            _view.ShowTab(tab);
            IslandSurface.SetOpacity(_view.TabView(tab), 1);
            _outline.Reset(layout.OpenRegion(open));
            UpdatePlayer();
            return;
        }

        var outgoing = _view.TabView(from);
        var incoming = _view.TabView(tab);
        incoming.Visibility = Visibility.Visible;
        IslandSurface.SetOpacity(incoming, 0);
        _surface.Crossfade(outgoing, incoming, () => outgoing.Visibility = Visibility.Collapsed);

        if (open.Height != before.Height)
        {
            if (_window.CursorPoint(out var x, out var y)) _outline.Resize(layout.OpenRegion(open), x, y);
            else _outline.Reset(layout.OpenRegion(open));
            var spring = (open.Height > before.Height ? SpringSpec.Open : SpringSpec.Close).WithSpeed(_settings.AnimationSpeed);
            _surface.MorphTo(open, Corner(open, IslandState.Expanded), spring, shadow: true, Settle, Shoulder(IslandState.Expanded));
        }
        UpdatePlayer();
    }

    /// <summary>Widens or narrows the resting pill; while open, the model only remembers it for the next close.</summary>
    private void OnPillChanged()
    {
        if (_model.State == IslandState.Expanded || _window.Layout is not { } layout) return;
        var compact = _model.Pill == PillKind.Compact;
        var shape = Shape(layout, _model.State);

        if (_settled && compact) _window.SetHitRegion(Region(layout, _model.State));
        _settled = false;
        _surface.Fade(
            _view.PillLayer, compact ? 1 : 0,
            compact ? MotionTimings.ContentDelay : TimeSpan.Zero,
            compact ? MotionTimings.ContentFadeIn : MotionTimings.ContentFadeOut);
        _surface.MorphTo(shape, Corner(shape, _model.State), Spring(), shadow: false, Settle, Shoulder(_model.State));
        UpdateClock(animate: true);
        UpdateBars();
    }

    /// <summary>
    /// The time on the idle pill, when Settings › Appearance › Island asks for it: shown while the
    /// island rests as the plain pill, faded with the pill's other content otherwise.
    /// </summary>
    private void UpdateClock(bool animate)
    {
        var wanted = _settings.ShowIdleClock;
        _view.Clock.SetRunning(wanted);
        var shown = wanted && _model.State is IslandState.Pill or IslandState.Peek && _model.Pill == PillKind.Idle;
        if (!animate)
        {
            IslandSurface.SetOpacity(_view.Clock.Root, shown ? 1 : 0);
            return;
        }
        _surface.Fade(_view.Clock.Root, shown ? 1 : 0, shown ? MotionTimings.ContentDelay : TimeSpan.Zero, shown ? MotionTimings.ContentFadeIn : MotionTimings.ContentFadeOut);
    }

    /// <summary>The shape came to rest. Back at the pill, the player leaves the tree and the mouse region shrinks to it.</summary>
    private void Settle()
    {
        // Gone once the shape has come to rest, whatever the island became; opening from a banner or a drop included.
        if (_model.State != IslandState.Alert) _view.AlertLayer.Visibility = Visibility.Collapsed;
        if (_model.State != IslandState.DropTarget) _view.DropTarget.Root.Visibility = Visibility.Collapsed;
        if (_model.State == IslandState.Expanded || _window.Layout is not { } layout) return;
        _settled = true;
        if (_model.State == IslandState.Pill)
        {
            _view.ExpandedLayer.Visibility = Visibility.Collapsed;
            _trim.Schedule();
        }
        _window.SetHitRegion(Region(layout, _model.State));
        UpdateBars();
    }

    private void OnModelChanged()
    {
        _outsideClicks.Enabled = _model.WantsOutsideClicks;
        _view.Header.SetPinned(_model.IsPinned);
        UpdateBars();
    }

    /// <summary>The model picks the spring (a status banner bounces); the setting sets its speed.</summary>
    private SpringSpec Spring() => _model.Spring.WithSpeed(_settings.AnimationSpeed);

    // MARK: Pointer

    /// <summary>
    /// Pointer events bubble from the island's buttons, and the column they come from is bigger
    /// than the island while it is a pill or shrinking back to one. So every event is checked
    /// against where the cursor really is; only the island itself counts. A touch has no cursor.
    /// </summary>
    private void UpdateHover(PointerRoutedEventArgs e, bool touchInside)
    {
        var touch = e.Pointer.PointerDeviceType == PointerDeviceType.Touch;
        var inside = touch ? touchInside : CursorOnIsland();
        if (inside != _model.IsHovering) _model.SetHovering(inside, touch);
    }

    /// <summary>
    /// Two fingers sideways on the open island switch tabs. On the shelf and the clipboard, whose
    /// content will scroll sideways itself, only the header row and the dock take the gesture.
    /// </summary>
    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_view.ExpandedLayer);
        if (!point.Properties.IsHorizontalMouseWheel || _model.State != IslandState.Expanded || !_settings.SwipeToChangeTabs) return;
        e.Handled = true;
        if (_model.Tab is IslandTab.Shelf or IslandTab.Clipboard
            && point.Position.Y > IslandSizes.HeaderHeight
            && point.Position.Y < _view.ExpandedLayer.Height - IslandSizes.DockArea)
        {
            return;
        }
        var step = _swipe.Add(point.Properties.MouseWheelDelta, DateTimeOffset.Now);
        if (step != 0) _model.SelectAdjacent(step);
    }

    /// <summary>A click or tap on the pill opens it at once, without waiting for the hover delay.</summary>
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_view.Area);
        if (_model.State == IslandState.Expanded || !(point.Properties.IsLeftButtonPressed || e.Pointer.PointerDeviceType == PointerDeviceType.Touch)) return;
        // A notification has done its job once clicked: its app comes forward (NotificationViews.swift).
        if (_model.State == IslandState.Alert && _model.Alert is NotificationAlert notification)
        {
            SenderApp.Open(notification.Notification.AppId);
            _model.DismissAlert();
            return;
        }
        _model.Click();
    }

    /// <summary>The hook saw a click somewhere; it only counts if it was off the island.</summary>
    private void OnClickedOutside()
    {
        if (CursorOnIsland()) return;
        _model.ClickedOutside();
    }

    // MARK: Media

    /// <summary>
    /// Closed, a media event only moves the pill, its artwork and bars; Home catches up when it is
    /// next shown (PORTING.md §3.1). A new song gets a banner.
    /// </summary>
    private void OnMediaChanged()
    {
        var now = _media.Current;
        if (HomeShown) _view.Home.Media?.Refresh();
        if (_model.State == IslandState.Alert) _view.Banner.Refresh();
        LoadArtwork();
        _model.SetLive(now?.IsPlaying == true);
        if (_trackChanges.Update(now) && _settings.ShowTrackChangeAlert && !_window.IsHiddenForFullscreen)
        {
            _model.ShowAlert(new NowPlayingAlert(), AlertDurations.NowPlaying);
        }
        UpdateBars();
    }

    // MARK: Agents

    /// <summary>The ears follow the agents; the AI tab redraws itself; a banner for an answered approval goes.</summary>
    private void OnAgentsChanged()
    {
        _view.AgentEar.Show(_agents.Ear);
        _model.SetAgentLive(_agents.Ear is not null);
        if (_model.State == IslandState.Alert && _model.Alert is ApprovalAlert shown && !_agents.Approvals.Contains(shown.Approval)) _model.DismissAlert();
        UpdateBars();
    }

    /// <summary>Onayla / Reddet / Terminalde sor, from the banner or the AI tab.</summary>
    private void DecideApproval(AgentApproval approval, bool? allow)
    {
        _agents.Decide(approval, allow);
        if (_model.State == IslandState.Alert && _model.Alert is ApprovalAlert) _model.DismissAlert();
    }

    /// <summary>"Oturuma git": the terminal or app the agent runs in comes forward, and the island steps aside.</summary>
    private void GoToSession(int processId)
    {
        if (AppWindows.Activate(processId)) _model.Close();
    }

    /// <summary>
    /// One banner took another's place, or a HUD moved: the content changes, and the island springs
    /// to the new banner's size if it has another (IslandModel.swift, onLayoutChange).
    /// </summary>
    private void OnAlertChanged()
    {
        _view.ShowAlert(_model.Alert, _model.PendingNotificationCount, swapped: true, _status.Network.Link);
        if (_model.State != IslandState.Alert || _window.Layout is not { } layout) return;
        var shape = AlertRect(layout);
        if (shape == _alertShape) return;
        _alertShape = shape;
        _settled = false;
        _view.SetAlert(shape);
        // Growing takes the bigger mouse region now; shrinking keeps it until the shape has settled.
        if (_model.IsGrowing) _window.SetHitRegion(Region(layout, IslandState.Alert));
        _surface.MorphTo(shape, Corner(shape, IslandState.Alert), Spring(), shadow: false, Settle, Shoulder(IslandState.Alert));
    }

    // MARK: Battery, cable, volume

    /// <summary>
    /// Plugged in or out and a cable in or out open the status ears; low and critical are banners;
    /// volume and brightness the HUD (AppDelegate.swift). None of them while a full-screen app hides the island.
    /// </summary>
    private void StartStatus()
    {
        _status.BatteryEvent += battery =>
        {
            if (!_window.IsHiddenForFullscreen) _model.ShowAlert(new BatteryAlert(battery), AlertDurations.Battery);
        };
        _status.WiredChanged += (connected, link) =>
        {
            if (!_window.IsHiddenForFullscreen && _settings.ShowNetworkChip) _model.ShowAlert(new WiredAlert(connected, link), AlertDurations.Wired);
        };
        _status.Hud += value =>
        {
            if (!_window.IsHiddenForFullscreen) _model.ShowHud(value);
        };
        _status.Changed += () =>
        {
            _view.ShowStatusChips(_settings, _status);
            if (_model.State == IslandState.Alert && _model.Alert is WiredAlert { Connected: false }) _view.UpdateWiredOut(_status.Network.Link);
        };
        _model.RetractingChanged += () =>
        {
            if (_model.IsRetractingStatus) _view.StatusEars.Retract();
        };
        _status.Brightness.AttachWindow(_window.Hwnd);
        _window.AddMessageHandler(_status.Brightness.OnMessage);
        _status.Start();
    }

    // MARK: Keyboard

    /// <summary>
    /// The island never takes focus, but typing needs it (the calendar's add line, as the Mac's
    /// requestKeyboard). It is handed back to the window that had it once the island closes.
    /// </summary>
    private void TakeKeyboard()
    {
        if (_foregroundBeforeKeyboard == 0) _foregroundBeforeKeyboard = _window.TakeForeground();
    }

    private void ReturnKeyboard()
    {
        if (_foregroundBeforeKeyboard == 0) return;
        _window.ReturnForeground(_foregroundBeforeKeyboard);
        _foregroundBeforeKeyboard = 0;
    }

    // MARK: Timer

    /// <summary>Set, paused or stopped: the ears and the Home tile follow (the tile on its own).</summary>
    private void OnTimerChanged()
    {
        _model.SetTimerLive(_timer.Mode != TimerMode.Idle);
        _view.TimerEar.Refresh();
        UpdateBars();
    }

    /// <summary>A phase ended: the sound (Settings › Widgets) and a 4 s banner.</summary>
    private void OnTimerPhaseEnded(TimerEvent e)
    {
        if (_settings.TimerSound) Win32.PlaySound("Notification.Reminder", 0, Win32.SND_ALIAS | Win32.SND_ASYNC);
        if (!_window.IsHiddenForFullscreen) _model.ShowAlert(new TimerAlert(e), AlertDurations.Timer);
    }

    /// <summary>
    /// Bars only move while they can be seen and something plays: in the wide pill or its peek (if
    /// Settings › Media wants them), and in the song banner.
    /// </summary>
    private void UpdateBars()
    {
        var playing = _media.Current?.IsPlaying == true && !_window.IsHiddenForFullscreen;
        var state = _model.State;
        var earsSeen = state is IslandState.Pill or IslandState.Peek && _model.Pill == PillKind.Compact && !_window.IsHiddenForFullscreen;
        _view.ShowEar(_model.Activity, earsSeen);
        if (playing && _settings.AudioBars && earsSeen && _model.Activity == PillActivity.Media) _view.Bars.Start();
        else _view.Bars.Stop();
        if (playing && state == IslandState.Alert && _model.Alert is NowPlayingAlert) _view.Banner.Bars.Start();
        else _view.Banner.Bars.Stop();
        // The title scrolls only while the wide pill can be seen; otherwise it waits where it started.
        var title = _settings.ShowPillTitle && _media.Current is { Title: { Length: > 0 } name } ? name : null;
        _view.PillTitle.Show(title, running: earsSeen && _model.Activity == PillActivity.Media && title is not null);
    }

    private async void LoadArtwork()
    {
        var now = _media.Current;
        var thumbnail = _media.Thumbnail;
        var key = now is null || thumbnail is null ? null : now.ArtworkKey;
        if (key == _artworkKey) return;
        _artworkKey = key;
        if (key is null || thumbnail is null)
        {
            _view.SetArtwork(null);
            return;
        }
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var bitmap = new BitmapImage
            {
                DecodePixelType = DecodePixelType.Logical,
                DecodePixelWidth = (int)MediaColumn.LargestArtwork,
            };
            await bitmap.SetSourceAsync(stream);
            if (_artworkKey == key) _view.SetArtwork(bitmap);
        }
        catch (Exception e)
        {
            Log.Error(e, "artwork");
            if (_artworkKey == key) _view.SetArtwork(null);
        }
    }

    // MARK: Menu

    private MenuFlyout BuildMenu()
    {
        var menu = new MenuFlyout { ShouldConstrainToRootBounds = false };
        menu.Opening += (_, _) =>
        {
            _model.SetMenuOpen(true, pointerInside: true);
            // The island never takes focus, but a menu only closes on an outside click once its
            // window has it. Handed back when the menu closes.
            _foregroundBeforeMenu = _window.TakeForeground();
            FillMenu(menu);
        };
        menu.Closed += (_, _) =>
        {
            _window.ReturnForeground(_foregroundBeforeMenu);
            _foregroundBeforeMenu = 0;
            _model.SetMenuOpen(false, pointerInside: CursorOnIsland());
        };
        return menu;
    }

    private void FillMenu(MenuFlyout menu)
    {
        menu.Items.Clear();

        var pin = new ToggleMenuFlyoutItem { Text = L.Get("Menu_Pin"), IsChecked = _model.IsPinned, Icon = new FontIcon { Glyph = Glyphs.Pin } };
        pin.Click += (_, _) => _model.SetPinned(pin.IsChecked);
        menu.Items.Add(pin);
        var settings = new MenuFlyoutItem { Text = L.Get("Tray_Settings"), Icon = new FontIcon { Glyph = Glyphs.Settings } };
        settings.Click += (_, _) => ((App)Application.Current).OpenSettings();
        menu.Items.Add(settings);
        menu.Items.Add(new MenuFlyoutSeparator());

        var themes = new MenuFlyoutSubItem { Text = L.Get("Menu_Theme"), Icon = new FontIcon { Glyph = Glyphs.Palette } };
        foreach (var theme in IslandTheme.Presets.Concat(_settings.CustomThemes))
        {
            var id = theme.Id;
            themes.Items.Add(Choice(ThemeName(theme), "theme", _settings.Theme.Id == id, () =>
            {
                _settings.ThemeId = id;
                Save();
                Rebuild();
            }));
        }
        menu.Items.Add(themes);

        var displays = new MenuFlyoutSubItem { Text = L.Get("Menu_Display"), Icon = new FontIcon { Glyph = Glyphs.Display } };
        foreach (var (choice, key) in new[] { (MonitorChoice.Primary, "Menu_DisplayPrimary"), (MonitorChoice.Active, "Menu_DisplayActive") })
        {
            displays.Items.Add(Choice(L.Get(key), "display", _settings.Monitor == choice, () =>
            {
                _settings.Monitor = choice;
                _settings.MonitorDevice = null;
                Save();
                _window.Place();
            }));
        }
        foreach (var monitor in Monitors.All())
        {
            var device = monitor.Device;
            var label = L.Format("Menu_DisplayItem", monitor.Number, monitor.Bounds.Width, monitor.Bounds.Height, (int)Math.Round(monitor.Scale * 100));
            var chosen = _settings.Monitor == MonitorChoice.Named && _settings.MonitorDevice == device;
            displays.Items.Add(Choice(label, "display", chosen, () =>
            {
                _settings.Monitor = MonitorChoice.Named;
                _settings.MonitorDevice = device;
                Save();
                _window.Place();
            }));
        }
        menu.Items.Add(displays);

        var languages = new MenuFlyoutSubItem { Text = L.Get("Menu_Language"), Icon = new FontIcon { Glyph = Glyphs.Language } };
        foreach (var (code, name) in new[] { ("system", L.Get("Menu_LanguageSystem")), ("tr-TR", "Türkçe"), ("en-US", "English") })
        {
            languages.Items.Add(Choice(name, "language", _settings.Language == code, () =>
            {
                if (_settings.Language == code) return;
                _settings.Language = code;
                Save();
                // As in Settings: every window is built in the new language from the start.
                ((App)Application.Current).Restart();
            }));
        }
        menu.Items.Add(languages);
        menu.Items.Add(new MenuFlyoutSeparator());

        var quit = new MenuFlyoutItem { Text = L.Get("Menu_Quit"), Icon = new FontIcon { Glyph = Glyphs.Quit } };
        quit.Click += (_, _) => ((App)Application.Current).Quit();
        menu.Items.Add(quit);
    }

    private static RadioMenuFlyoutItem Choice(string text, string group, bool chosen, Action choose)
    {
        var item = new RadioMenuFlyoutItem { Text = text, GroupName = group, IsChecked = chosen };
        item.Click += (_, _) => choose();
        return item;
    }

    /// <summary>Built-in themes are named in the current language; your own keep their names.</summary>
    private static string ThemeName(IslandTheme theme) =>
        theme.IsPreset ? L.Get("Theme_" + theme.Id.Replace('-', '_')) : theme.Name;

    private void Save() => _store.Save(_settings);
}
