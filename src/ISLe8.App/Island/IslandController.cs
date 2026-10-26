using ISLe8.Core.Diagnostics;
using ISLe8.Core.Geometry;
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
/// Runs the island: pill ⇄ open on hover, the pill widening while music plays, the right-click
/// menu, and rebuilding the view when the theme or language changes.
/// </summary>
internal sealed class IslandController : IDisposable
{
    /// <summary>Players report "paused" for a moment between tracks; the pill waits this long before narrowing.</summary>
    private const int PillShrinkDelayMs = 1500;

    /// <summary>A finger lifting is not leaving: an island opened by touch stays this long.</summary>
    private const int TouchCloseDelayMs = 4000;

    private enum State
    {
        Collapsed,
        Expanded,
    }

    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly DispatcherQueue _queue;
    private readonly MediaService _media;
    private readonly IslandWindow _window;
    private readonly DispatcherQueueTimer _openTimer;
    private readonly DispatcherQueueTimer _closeTimer;
    private readonly DispatcherQueueTimer _shrinkTimer;
    private IslandView _view = null!;
    private IslandSurface _surface = null!;
    private State _state = State.Collapsed;
    /// <summary>The pill the island shows, or will show, while closed.</summary>
    private PillKind _pill = PillKind.Idle;
    /// <summary>False while the shape is still moving after a close.</summary>
    private bool _settled = true;
    private bool _pinned;
    private bool _menuOpen;
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
        _openTimer = OneShot(Expand);
        _closeTimer = OneShot(Collapse);
        _shrinkTimer = OneShot(() =>
        {
            if (_media.Current?.IsPlaying != true) SetPill(PillKind.Idle);
        });
    }

    public void Start()
    {
        Rebuild();
        _window.LayoutChanged += ApplyLayout;
        _window.FullscreenChanged += _ => UpdateBars();
        _window.Show();

        _media.Changed += OnMediaChanged;
        _ = _media.StartAsync();
    }

    public void Dispose()
    {
        _openTimer.Stop();
        _closeTimer.Stop();
        _shrinkTimer.Stop();
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
        _view.Area.PointerEntered += OnPointerEntered;
        _view.Area.PointerExited += OnPointerExited;
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
        var expanded = _state == State.Expanded;
        _settled = true;
        _view.ApplyLayout(layout);
        _surface.SetLayout(layout);
        _surface.JumpTo(
            expanded ? layout.Expanded : layout.Pill(_pill),
            expanded ? IslandMetrics.ExpandedCornerRadius : IslandMetrics.PillCornerRadius,
            shadow: expanded);
        _window.SetHitRegion(expanded ? layout.ExpandedRegion : layout.PillRegion(_pill));

        _view.ExpandedLayer.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        _view.ExpandedLayer.IsHitTestVisible = expanded;
        IslandSurface.SetOpacity(_view.ExpandedLayer, expanded ? 1 : 0);
        IslandSurface.SetOpacity(_view.PillLayer, !expanded && _pill == PillKind.Compact ? 1 : 0);
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

    // MARK: Open and close

    private void Expand()
    {
        if (_state == State.Expanded || _window.Layout is not { } layout) return;
        _state = State.Expanded;
        _settled = false;
        _closeTimer.Stop();

        _window.SetHitRegion(layout.ExpandedRegion);
        if (_view.ExpandedLayer.Visibility == Visibility.Collapsed)
        {
            // Back from fully closed; mid-close, the player fades back in from where it is.
            _view.ExpandedLayer.Visibility = Visibility.Visible;
            IslandSurface.SetOpacity(_view.ExpandedLayer, 0);
        }
        _view.ExpandedLayer.IsHitTestVisible = true;
        _surface.Fade(_view.ExpandedLayer, 1, MotionTimings.ContentDelay, MotionTimings.ContentFadeIn);
        _surface.Fade(_view.PillLayer, 0, TimeSpan.Zero, MotionTimings.ContentFadeOut);
        _surface.MorphTo(layout.Expanded, IslandMetrics.ExpandedCornerRadius, Spring(SpringSpec.Open), shadow: true, Settle);

        _view.Player.SetActive(true);
        UpdateBars();
    }

    private void Collapse()
    {
        if (_state == State.Collapsed || _pinned || _menuOpen || _window.Layout is not { } layout) return;
        _state = State.Collapsed;
        _settled = false;

        _view.Player.SetActive(false);
        _view.ExpandedLayer.IsHitTestVisible = false;
        _surface.Fade(_view.ExpandedLayer, 0, TimeSpan.Zero, MotionTimings.ContentFadeOut);
        if (_pill == PillKind.Compact)
        {
            _surface.Fade(_view.PillLayer, 1, MotionTimings.ContentDelay, MotionTimings.ContentFadeIn);
        }
        _surface.MorphTo(layout.Pill(_pill), IslandMetrics.PillCornerRadius, Spring(SpringSpec.Close), shadow: false, Settle);
    }

    /// <summary>The shape came to rest. Once closed, the player leaves the tree and the window shrinks to the pill.</summary>
    private void Settle()
    {
        if (_state != State.Collapsed || _window.Layout is not { } layout) return;
        _settled = true;
        _view.ExpandedLayer.Visibility = Visibility.Collapsed;
        _window.SetHitRegion(layout.PillRegion(_pill));
        UpdateBars();
    }

    private SpringSpec Spring(SpringSpec spring) => spring.WithSpeed(_settings.AnimationSpeed);

    /// <summary>The part of the window that counts as "on the island" right now, in window pixels.</summary>
    private PixelRect? HoverRegion => _window.Layout is not { } layout
        ? null
        : _state == State.Expanded ? layout.AreaRegion : layout.PillRegion(_pill);

    // MARK: Pointer

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _closeTimer.Stop();
        if (_state != State.Collapsed || _menuOpen) return;
        if (_settings.HoverDelayMs <= 0) Expand();
        else Restart(_openTimer, _settings.HoverDelayMs);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var touch = e.Pointer.PointerDeviceType == PointerDeviceType.Touch;
        // Pointer events bubble, so leaving a button inside the island lands here too. Ask the
        // system where the mouse really is; the event's own position may be the last one inside.
        if (!touch && HoverRegion is { } region && _window.CursorIn(region)) return;

        _openTimer.Stop();
        if (_state == State.Expanded && !_pinned && !_menuOpen)
        {
            Restart(_closeTimer, touch ? TouchCloseDelayMs : _settings.CloseDelayMs);
        }
    }

    /// <summary>A click or tap on the pill opens it at once, without waiting for the hover delay.</summary>
    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_view.Area);
        if (_state != State.Collapsed || !(point.Properties.IsLeftButtonPressed || e.Pointer.PointerDeviceType == PointerDeviceType.Touch)) return;
        _openTimer.Stop();
        Expand();
    }

    private DispatcherQueueTimer OneShot(Action action)
    {
        var timer = _queue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => action();
        return timer;
    }

    private static void Restart(DispatcherQueueTimer timer, int milliseconds)
    {
        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        timer.Start();
    }

    // MARK: Media

    private void OnMediaChanged()
    {
        _view.Player.Refresh();
        LoadArtwork();

        if (_media.Current?.IsPlaying == true)
        {
            _shrinkTimer.Stop();
            SetPill(PillKind.Compact);
        }
        else if (_pill == PillKind.Compact && !_shrinkTimer.IsRunning)
        {
            Restart(_shrinkTimer, PillShrinkDelayMs);
        }
        UpdateBars();
    }

    /// <summary>Widens or narrows the closed pill; while open, only remembers it for the next close.</summary>
    private void SetPill(PillKind pill)
    {
        if (pill == _pill) return;
        _pill = pill;
        if (_state != State.Collapsed || _window.Layout is not { } layout) return;

        // Growing takes the wider region now; shrinking keeps it until the shape has settled.
        if (_settled && pill == PillKind.Compact) _window.SetHitRegion(layout.PillRegion(pill));
        _settled = false;
        _surface.Fade(
            _view.PillLayer, pill == PillKind.Compact ? 1 : 0,
            pill == PillKind.Compact ? MotionTimings.ContentDelay : TimeSpan.Zero,
            pill == PillKind.Compact ? MotionTimings.ContentFadeIn : MotionTimings.ContentFadeOut);
        _surface.MorphTo(layout.Pill(pill), IslandMetrics.PillCornerRadius, Spring(SpringSpec.Open), shadow: false, Settle);
        UpdateBars();
    }

    /// <summary>Bars only move while they can be seen: compact pill, playing, window shown.</summary>
    private void UpdateBars()
    {
        var visible = _state == State.Collapsed
            && _pill == PillKind.Compact
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
            _menuOpen = true;
            _openTimer.Stop();
            _closeTimer.Stop();
            // The island never takes focus, but a menu only closes on an outside click once its
            // window has it. Handed back when the menu closes.
            _foregroundBeforeMenu = _window.TakeForeground();
            FillMenu(menu);
        };
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            _window.ReturnForeground(_foregroundBeforeMenu);
            _foregroundBeforeMenu = 0;
            var inside = HoverRegion is { } region && _window.CursorIn(region);
            if (!inside && _state == State.Expanded && !_pinned) Restart(_closeTimer, _settings.CloseDelayMs);
        };
        return menu;
    }

    private void FillMenu(MenuFlyout menu)
    {
        menu.Items.Clear();

        var pin = new ToggleMenuFlyoutItem { Text = L.Get("Menu_Pin"), IsChecked = _pinned, Icon = new FontIcon { Glyph = Glyphs.Pin } };
        pin.Click += (_, _) =>
        {
            _pinned = pin.IsChecked;
            if (_pinned) Expand();
        };
        menu.Items.Add(pin);
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
        displays.Items.Add(Choice(L.Get("Menu_DisplayPrimary"), "display", _settings.Monitor == MonitorChoice.Primary, () =>
        {
            _settings.Monitor = MonitorChoice.Primary;
            _settings.MonitorDevice = null;
            Save();
            _window.Place();
        }));
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
                _settings.Language = code;
                Save();
                L.Use(code);
                Rebuild();
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
