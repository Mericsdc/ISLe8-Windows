using ISLe8.Core.Diagnostics;
using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Motion;
using ISLe8.Core.Settings;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.Media;
using ISLe8.Shell;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ISLe8.Island;

/// <summary>
/// Draws the island in whatever state <see cref="IslandModel"/> puts it: pill, peek or open, the
/// pill widening while music plays. Feeds the model pointer, menu and media events, and rebuilds
/// the view when the theme or language changes.
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
    private IslandView _view = null!;
    private IslandSurface _surface = null!;
    /// <summary>False while the shape is still moving after it shrank.</summary>
    private bool _settled = true;
    private nint _foregroundBeforeMenu;
    private string? _artworkKey;

    public IslandController(SettingsStore store)
    {
        _store = store;
        _settings = store.Load();
        _queue = DispatcherQueue.GetForCurrentThread();
        L.Use(_settings.Language);

        _media = new MediaService(_queue);
        _window = new IslandWindow(_settings, () => IslandMetrics.DefaultExpanded);
        _model = new IslandModel(_settings, new DispatcherClock(_queue));
        _outsideClicks = new OutsideClickWatcher(_queue, OnClickedOutside);
    }

    /// <summary>The one settings object; the settings window changes it in place.</summary>
    public AppSettings Settings => _settings;

    /// <summary>The island's window, which also takes the tray icon's messages.</summary>
    public nint WindowHandle => _window.Hwnd;

    public void AddWindowMessageHandler(Func<uint, nint, nint, nint?> handler) => _window.AddMessageHandler(handler);

    /// <summary>Moves the island to the screen the settings ask for now.</summary>
    public void PlaceIsland() => _window.Place();

    public void Start()
    {
        Rebuild();
        _model.StateChanged += OnStateChanged;
        _model.PillChanged += OnPillChanged;
        _model.Changed += OnModelChanged;
        _window.LayoutChanged += ApplyLayout;
        _window.FullscreenChanged += _ => UpdateBars();
        _window.Show();

        _media.Changed += OnMediaChanged;
        _ = _media.StartAsync();
    }

    public void Dispose()
    {
        _outsideClicks.Dispose();
        _view.Player.SetActive(false);
        _view.Bars.Stop();
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
        if (_view is not null)
        {
            _view.Player.SetActive(false);
            _view.Bars.Stop();
        }

        _view = new IslandView(theme, _media, _queue);
        _view.Area.PointerEntered += (_, e) => UpdateHover(e, touchInside: true);
        _view.Area.PointerMoved += (_, e) => UpdateHover(e, touchInside: true);
        _view.Area.PointerExited += (_, e) => UpdateHover(e, touchInside: false);
        _view.Area.PointerPressed += OnPointerPressed;
        _view.Area.ContextFlyout = BuildMenu();
        _view.Root.Loaded += (_, _) =>
        {
            if (_view.Root.XamlRoot is { } root) root.Changed += OnXamlRootChanged;
        };

        _surface = new IslandSurface(_view.BackgroundHost, _view.Area);
        _surface.SetTheme(theme);
        _window.Window.Content = _view.Root;

        if (_window.Layout is { } layout) ApplyLayout(layout);
        _view.Player.Refresh();
        _artworkKey = null;
        LoadArtwork();
    }

    /// <summary>A new monitor, DPI or view: everything jumps to where it belongs, without animating.</summary>
    private void ApplyLayout(IslandLayout layout)
    {
        var state = _model.State;
        var expanded = state == IslandState.Expanded;
        var shape = Shape(layout, state);
        _settled = true;
        _view.ApplyLayout(layout);
        _surface.SetLayout(layout);
        _surface.JumpTo(shape, Corner(shape, state), shadow: expanded);
        _window.SetHitRegion(Region(layout, state));

        _view.ExpandedLayer.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        _view.ExpandedLayer.IsHitTestVisible = expanded;
        IslandSurface.SetContentShown(_view.ExpandedLayer, expanded);
        IslandSurface.SetOpacity(_view.PillLayer, !expanded && _model.Pill == PillKind.Compact ? 1 : 0);
        _view.Player.SetActive(expanded);
        UpdateBars();
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
        IslandState.Expanded => layout.Expanded,
        IslandState.Peek => layout.Peek(_model.Pill),
        _ => layout.Pill(_model.Pill),
    };

    /// <summary>Pills and the peek are fully round at the ends; the open island has its own corners.</summary>
    private static double Corner(DipRect shape, IslandState state) =>
        state == IslandState.Expanded ? IslandMetrics.ExpandedCornerRadius : shape.Height / 2;

    /// <summary>The part of the window that takes the mouse in <paramref name="state"/>, in window pixels.</summary>
    private PixelRect Region(IslandLayout layout, IslandState state) => state switch
    {
        IslandState.Expanded => layout.ExpandedRegion,
        IslandState.Peek => layout.PeekRegion(_model.Pill),
        _ => layout.PillRegion(_model.Pill),
    };

    /// <summary>The part of the window that counts as "on the island" right now: only the island itself.</summary>
    private PixelRect? HoverRegion => _window.Layout is not { } layout
        ? null
        : _model.State switch
        {
            IslandState.Expanded => layout.AreaRegion,
            IslandState.Peek => layout.PeekRegion(_model.Pill),
            _ => layout.PillRegion(_model.Pill),
        };

    private void OnStateChanged(IslandState from)
    {
        if (_window.Layout is not { } layout) return;
        var state = _model.State;
        var shape = Shape(layout, state);
        _settled = false;

        if (state == IslandState.Expanded)
        {
            _window.SetHitRegion(layout.ExpandedRegion);
            if (_view.ExpandedLayer.Visibility == Visibility.Collapsed)
            {
                // Back from fully closed; mid-close, the content grows back from where it is.
                _view.ExpandedLayer.Visibility = Visibility.Visible;
                IslandSurface.SetContentShown(_view.ExpandedLayer, false);
            }
            _view.ExpandedLayer.IsHitTestVisible = true;
            _surface.Reveal(_view.ExpandedLayer, show: true);
            _surface.Fade(_view.PillLayer, 0, TimeSpan.Zero, MotionTimings.ContentFadeOut);
            _view.Player.SetActive(true);
        }
        else if (from == IslandState.Expanded)
        {
            _view.Player.SetActive(false);
            _view.ExpandedLayer.IsHitTestVisible = false;
            _surface.Reveal(_view.ExpandedLayer, show: false);
            if (_model.Pill == PillKind.Compact)
            {
                _surface.Fade(_view.PillLayer, 1, MotionTimings.ContentDelay, MotionTimings.ContentFadeIn);
            }
        }

        // Growing takes the bigger mouse region now; shrinking keeps it until the shape has settled.
        if (state == IslandState.Peek) _window.SetHitRegion(Region(layout, state));
        _surface.MorphTo(shape, Corner(shape, state), Spring(), shadow: state == IslandState.Expanded, Settle);
        UpdateBars();
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
        _surface.MorphTo(shape, Corner(shape, _model.State), Spring(), shadow: false, Settle);
        UpdateBars();
    }

    /// <summary>The shape came to rest. Back at the pill, the player leaves the tree and the mouse region shrinks to it.</summary>
    private void Settle()
    {
        if (_model.State == IslandState.Expanded || _window.Layout is not { } layout) return;
        _settled = true;
        if (_model.State == IslandState.Pill) _view.ExpandedLayer.Visibility = Visibility.Collapsed;
        _window.SetHitRegion(Region(layout, _model.State));
        UpdateBars();
    }

    private void OnModelChanged()
    {
        _outsideClicks.Enabled = _model.WantsOutsideClicks;
        UpdateBars();
    }

    private SpringSpec Spring() =>
        (_model.IsGrowing ? SpringSpec.Open : SpringSpec.Close).WithSpeed(_settings.AnimationSpeed);

    // MARK: Pointer

    /// <summary>
    /// Pointer events bubble from the island's buttons, and the column they come from is bigger
    /// than the island while it is a pill or shrinking back to one. So every event is checked
    /// against where the cursor really is; only the island itself counts. A touch has no cursor.
    /// </summary>
    private void UpdateHover(PointerRoutedEventArgs e, bool touchInside)
    {
        var touch = e.Pointer.PointerDeviceType == PointerDeviceType.Touch;
        var inside = touch ? touchInside : HoverRegion is { } region && _window.CursorIn(region);
        if (inside != _model.IsHovering) _model.SetHovering(inside, touch);
    }

    /// <summary>A click or tap on the pill opens it at once, without waiting for the hover delay.</summary>
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_view.Area);
        if (_model.State == IslandState.Expanded || !(point.Properties.IsLeftButtonPressed || e.Pointer.PointerDeviceType == PointerDeviceType.Touch)) return;
        _model.Click();
    }

    /// <summary>The hook saw a click somewhere; it only counts if it was off the island.</summary>
    private void OnClickedOutside()
    {
        if (HoverRegion is { } region && _window.CursorIn(region)) return;
        _model.ClickedOutside();
    }

    // MARK: Media

    private void OnMediaChanged()
    {
        _view.Player.Refresh();
        LoadArtwork();
        _model.SetLive(_media.Current?.IsPlaying == true);
        UpdateBars();
    }

    /// <summary>Bars only move while they can be seen: the wide pill or its peek, playing, window shown.</summary>
    private void UpdateBars()
    {
        var visible = _model.State != IslandState.Expanded
            && _model.Pill == PillKind.Compact
            && _media.Current?.IsPlaying == true
            && _settings.AudioBars
            && !_window.IsHiddenForFullscreen;
        if (visible) _view.Bars.Start();
        else _view.Bars.Stop();
    }

    private async void LoadArtwork()
    {
        var now = _media.Current;
        var thumbnail = _media.Thumbnail;
        var key = now is null || thumbnail is null ? null : now.TrackKey;
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
                DecodePixelWidth = (int)MediaPlayerView.ArtworkSize,
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
            _model.SetMenuOpen(false, pointerInside: HoverRegion is { } region && _window.CursorIn(region));
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
