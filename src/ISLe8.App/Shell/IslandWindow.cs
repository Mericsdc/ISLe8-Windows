using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.Geometry;
using ISLe8.Core.Settings;
using ISLe8.Interop;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace ISLe8.Shell;

/// <summary>
/// The island's window: borderless, always on top, never in the taskbar or Alt+Tab, never takes focus.
/// It covers the open island plus its shadow and never resizes while the island animates; a window
/// region decides which part takes the mouse, so clicks beside the pill reach the window below.
/// </summary>
internal sealed unsafe class IslandWindow : IDisposable
{
    /// <summary>A registered message, so it can't clash with anything WinUI uses on its own window.</summary>
    private static readonly uint AppBarCallback = Win32.RegisterWindowMessage("ISLe8.AppBar");

    /// <summary>The foreground hook's procedure is static; it reaches the window through here.</summary>
    private static IslandWindow? s_followingForeground;

    private readonly AppSettings _settings;
    private readonly Func<DipSize> _expandedSize;
    private readonly DispatcherQueue _queue;
    private readonly AppWindow _appWindow;
    private readonly List<Func<uint, nint, nint, nint?>> _messageHandlers = [];
    private GCHandle _self;
    private bool _subclassed;
    private nint _blurRegion;
    private bool _appBarRegistered;
    private int _demotions;
    private bool _placePending;
    private PixelRect? _hitRegion;
    private nint _foregroundHook;
    /// <summary>The monitor of the window you last brought forward, for Settings › Screen › Active.</summary>
    private nint _activeMonitor;

    public IslandWindow(AppSettings settings, Func<DipSize> expandedSize)
    {
        _settings = settings;
        _expandedSize = expandedSize;
        _queue = DispatcherQueue.GetForCurrentThread();

        Window = new Window { Title = "ISLe8", SystemBackdrop = new TransparentBackdrop() };
        _appWindow = Window.AppWindow;
        Hwnd = Win32Interop.GetWindowFromWindowId(_appWindow.Id);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        _appWindow.SetPresenter(presenter);
        _appWindow.IsShownInSwitchers = false;

        StripFrame();
        MakeTransparent();

        _self = GCHandle.Alloc(this);
        _subclassed = Win32.SetWindowSubclass(Hwnd, &SubclassProc, 1, (nuint)GCHandle.ToIntPtr(_self));
        RegisterAppBar();
    }

    public Window Window { get; }
    public nint Hwnd { get; }
    public MonitorInfo Monitor { get; private set; }
    public IslandLayout? Layout { get; private set; }
    public bool IsHiddenForFullscreen { get; private set; }

    /// <summary>After every placement: start, a monitor or DPI change, new display settings.</summary>
    public event Action<IslandLayout>? LayoutChanged;

    /// <summary>A full-screen app came up on the island's monitor (true) or went away (false).</summary>
    public event Action<bool>? FullscreenChanged;

    public void Show()
    {
        Place();
        // Shown without activation, so starting ISLe8 never takes focus from what you are doing.
        _appWindow.Show(false);
        // Showing puts the presenter's dialog frame back; without this a white 2 DIP border shows.
        StripFrame();
        RemoveDecorations();
    }

    /// <summary>Lets another part of ISLe8 (the tray icon) take messages sent to this window. Return null to pass one on.</summary>
    public void AddMessageHandler(Func<uint, nint, nint, nint?> handler) => _messageHandlers.Add(handler);

    /// <summary>Puts the window where Settings › Appearance › Island says on the chosen monitor, sized for that monitor's DPI.</summary>
    public void Place()
    {
        _placePending = false;
        FollowForeground(_settings.Monitor == MonitorChoice.Active);
        Monitor = Monitors.Pick(_settings, _activeMonitor);
        // The work area no longer counts the island's own strip: the island sits at the strip's outer edge.
        var workArea = Reserve(Monitor);
        var layout = IslandLayout.Compute(Monitor.Bounds, workArea, Monitor.Scale, _expandedSize(), _settings.Placement, _settings.FlushTop);
        Win32.SetWindowPos(
            Hwnd, Win32.HWND_TOPMOST,
            layout.Window.X, layout.Window.Y, layout.Window.Width, layout.Window.Height,
            Win32.SWP_NOACTIVATE);
        _hitRegion = null;
        Layout = layout;
        Log.Info($"placed on {Monitor.Device} {Monitor.Bounds.Width}x{Monitor.Bounds.Height} @{Monitor.Dpi} dpi: {layout.Window}");
        LayoutChanged?.Invoke(layout);
    }

    /// <summary>Places the window again once the current message is handled; repeated calls coalesce.</summary>
    public void RequestPlace()
    {
        if (_placePending) return;
        _placePending = true;
        _queue.TryEnqueue(Place);
    }

    /// <summary>
    /// Whether the mouse is inside <paramref name="region"/> (window pixels) right now. Asked of the
    /// system rather than taken from a pointer event, whose last position may still be inside.
    /// </summary>
    public bool CursorIn(PixelRect region) => CursorPoint(out var x, out var y) && region.Contains(x, y);

    /// <summary>Where the mouse is right now, relative to the window, in pixels.</summary>
    public bool CursorPoint(out int x, out int y)
    {
        x = y = 0;
        if (Layout is not { } layout || !Win32.GetCursorPos(out var cursor)) return false;
        x = cursor.X - layout.Window.X;
        y = cursor.Y - layout.Window.Y;
        return true;
    }

    /// <summary>Settings › Appearance: kept out of screenshots, recordings and shared screens.</summary>
    public void SetExcludedFromCapture(bool excluded) =>
        Win32.SetWindowDisplayAffinity(Hwnd, excluded ? Win32.WDA_EXCLUDEFROMCAPTURE : Win32.WDA_NONE);

    /// <summary>
    /// Brings the island forward for its menu, so a click anywhere else closes the menu as usual.
    /// Returns the window that had the foreground, to hand it back with <see cref="ReturnForeground"/>.
    /// </summary>
    public nint TakeForeground()
    {
        var previous = Win32.GetForegroundWindow();
        Win32.SetForegroundWindow(Hwnd);
        return previous == Hwnd ? 0 : previous;
    }

    public void ReturnForeground(nint previous)
    {
        if (previous != 0 && Win32.GetForegroundWindow() == Hwnd) Win32.SetForegroundWindow(previous);
    }

    /// <summary>The part of the window that takes the mouse, relative to the window, in pixels.</summary>
    public void SetHitRegion(PixelRect rect)
    {
        if (_hitRegion == rect) return;
        var region = Win32.CreateRectRgn(rect.X, rect.Y, rect.Right, rect.Bottom);
        if (Win32.SetWindowRgn(Hwnd, region, true) == 0)
        {
            Win32.DeleteObject(region);
            return;
        }
        // The window owns the region now.
        _hitRegion = rect;
    }

    public void Dispose()
    {
        FollowForeground(false);
        Detach();
        if (_blurRegion != 0)
        {
            Win32.DeleteObject(_blurRegion);
            _blurRegion = 0;
        }
        Window.Close();
    }

    private void StripFrame()
    {
        var style = (long)Win32.GetWindowLongPtr(Hwnd, Win32.GWL_STYLE);
        style &= ~(Win32.WS_CAPTION | Win32.WS_THICKFRAME | Win32.WS_SYSMENU | Win32.WS_MINIMIZEBOX | Win32.WS_MAXIMIZEBOX);
        Win32.SetWindowLongPtr(Hwnd, Win32.GWL_STYLE, (nint)(style | Win32.WS_POPUP));

        var exStyle = (long)Win32.GetWindowLongPtr(Hwnd, Win32.GWL_EXSTYLE);
        exStyle = (exStyle | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE) & ~Win32.WS_EX_APPWINDOW;
        Win32.SetWindowLongPtr(Hwnd, Win32.GWL_EXSTYLE, (nint)exStyle);

        Win32.SetWindowPos(
            Hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_FRAMECHANGED);
    }

    private void MakeTransparent()
    {
        // DWM honours the window's per-pixel alpha once blur-behind is on, here over an empty region.
        _blurRegion = Win32.CreateRectRgn(-2, -2, -1, -1);
        var blur = new DWM_BLURBEHIND
        {
            dwFlags = Win32.DWM_BB_ENABLE | Win32.DWM_BB_BLURREGION,
            fEnable = 1,
            hRgnBlur = _blurRegion,
        };
        Win32.DwmEnableBlurBehindWindow(Hwnd, in blur);
        RemoveDecorations();
    }

    /// <summary>Windows 11 rounds and outlines top-level windows; the island draws its own shape.</summary>
    private void RemoveDecorations()
    {
        var corner = Win32.DWMWCP_DONOTROUND;
        Win32.DwmSetWindowAttribute(Hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        var border = Win32.DWMWA_COLOR_NONE;
        Win32.DwmSetWindowAttribute(Hwnd, Win32.DWMWA_BORDER_COLOR, &border, sizeof(uint));
    }

    /// <summary>
    /// Keeps a strip on the island's edge of its screen free (Settings › General), as the taskbar does,
    /// so maximised windows end at the pill instead of running under it; or none. At the top the strip
    /// is the pill's; on the taskbar it sits on top of the taskbar. Returns the work area as it is without
    /// the island's own strip, whose outer edge the island is placed at. The shell stacks the strip
    /// inside any other app bar on that edge (the taskbar).
    /// </summary>
    private PixelRect Reserve(MonitorInfo monitor)
    {
        if (!_appBarRegistered) return monitor.WorkArea;
        var bounds = monitor.Bounds;
        var bottom = _settings.Placement != IslandPlacement.Top;
        var gap = !bottom && _settings.FlushTop ? 0 : IslandMetrics.TopMargin;
        var height = _settings.ReserveTopStrip ? IslandMetrics.ReservedPixels(monitor.Scale, gap) : 0;
        var data = new APPBARDATA
        {
            cbSize = (uint)sizeof(APPBARDATA),
            hWnd = Hwnd,
            uEdge = bottom ? Win32.ABE_BOTTOM : Win32.ABE_TOP,
            rc = bottom
                ? new RECT { Left = bounds.X, Top = bounds.Bottom - height, Right = bounds.Right, Bottom = bounds.Bottom }
                : new RECT { Left = bounds.X, Top = bounds.Y, Right = bounds.Right, Bottom = bounds.Y + height },
        };
        Win32.SHAppBarMessage(Win32.ABM_QUERYPOS, ref data);
        var work = monitor.WorkArea;
        if (bottom)
        {
            var edge = data.rc.Bottom;
            data.rc.Top = edge - height;
            Win32.SHAppBarMessage(Win32.ABM_SETPOS, ref data);
            return PixelRect.FromEdges(work.X, work.Y, work.Right, Math.Max(edge, work.Bottom));
        }
        var top = data.rc.Top;
        data.rc.Bottom = top + height;
        Win32.SHAppBarMessage(Win32.ABM_SETPOS, ref data);
        return PixelRect.FromEdges(work.X, Math.Min(top, work.Y), work.Right, work.Bottom);
    }

    /// <summary>
    /// An app bar: the shell tells the window when a full-screen app (a game, a video, a presentation)
    /// comes and goes, and it can keep the strip at the top free (<see cref="Reserve"/>). No polling.
    /// </summary>
    private void RegisterAppBar()
    {
        var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = Hwnd, uCallbackMessage = AppBarCallback };
        _appBarRegistered = Win32.SHAppBarMessage(Win32.ABM_NEW, ref data) != 0;
    }

    /// <summary>Leaves the shell's app bar list and unhooks the window. Safe to call twice.</summary>
    private void Detach()
    {
        if (_appBarRegistered)
        {
            var data = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = Hwnd };
            Win32.SHAppBarMessage(Win32.ABM_REMOVE, ref data);
            _appBarRegistered = false;
        }
        if (_subclassed)
        {
            Win32.RemoveWindowSubclass(Hwnd, &SubclassProc, 1);
            _subclassed = false;
        }
        if (_self.IsAllocated) _self.Free();
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message == AppBarCallback)
        {
            if (wParam == Win32.ABN_FULLSCREENAPP) OnFullscreenApp(lParam != 0);
            // Another app bar on the edge moved or a screen changed: the strip is asked for again.
            else if (wParam == Win32.ABN_POSCHANGED) RequestPlace();
            return 0;
        }
        foreach (var handler in _messageHandlers)
        {
            if (handler(message, wParam, lParam) is { } handled) return handled;
        }
        switch (message)
        {
            // No non-client area, whatever styles WinUI puts back: the client fills the window.
            case Win32.WM_NCCALCSIZE when wParam != 0:
                return 0;
            // WinUI resizes the window to the suggested rectangle first; the island then lays itself
            // out again for the new DPI, which also covers moving to another monitor.
            case Win32.WM_DPICHANGED:
            case Win32.WM_DISPLAYCHANGE:
                RequestPlace();
                break;
            case Win32.WM_SETTINGCHANGE when wParam == Win32.SPI_SETWORKAREA:
                RequestPlace();
                break;
            case Win32.WM_WINDOWPOSCHANGING when lParam != 0:
                KeepOnTop((WINDOWPOS*)lParam);
                break;
            // Windows can tear the window down without Dispose (logoff, taskkill); the subclass
            // must go before the window does.
            case Win32.WM_NCDESTROY:
                Detach();
                break;
        }
        return null;
    }

    /// <summary>
    /// The island stays above every ordinary window. It was once found under a maximised browser,
    /// its "topmost" gone though ISLe8 never asks for that, so a move that would take it out of the
    /// topmost band (to the bottom, or after an ordinary window) is turned into one that keeps it.
    /// </summary>
    private void KeepOnTop(WINDOWPOS* position)
    {
        if ((position->flags & Win32.SWP_NOZORDER) != 0) return;
        var after = position->hwndInsertAfter;
        if (after == Win32.HWND_TOPMOST || after == Win32.HWND_TOP) return;
        var demotes = after == Win32.HWND_NOTOPMOST || after == Win32.HWND_BOTTOM
            || ((long)Win32.GetWindowLongPtr(after, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOPMOST) == 0;
        if (!demotes) return;
        position->hwndInsertAfter = Win32.HWND_TOPMOST;
        // Who did it is worth knowing, but not a line per move.
        if (_demotions++ < 3) Log.Info($"kept on top (asked to go after {after}, {Win32.ClassName(after)})");
    }

    /// <summary>Puts the island back at the top of the topmost windows, without moving or activating it.</summary>
    public void BringToTop() =>
        Win32.SetWindowPos(Hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);

    /// <summary>Settings › General › Hide in full screen turned off while hidden: the island comes back now.</summary>
    public void ApplyFullscreenSetting()
    {
        if (!_settings.HideInFullscreen && IsHiddenForFullscreen) OnFullscreenApp(false);
    }

    private void OnFullscreenApp(bool opened)
    {
        if (opened)
        {
            if (!_settings.HideInFullscreen || IsHiddenForFullscreen) return;
            var foreground = Win32.GetForegroundWindow();
            if (foreground == 0 || foreground == Hwnd || IsDesktop(foreground)) return;
            if (Win32.MonitorFromWindow(foreground, Win32.MONITOR_DEFAULTTONEAREST) != Monitor.Handle) return;
            IsHiddenForFullscreen = true;
            Win32.ShowWindow(Hwnd, Win32.SW_HIDE);
            FullscreenChanged?.Invoke(true);
        }
        else if (IsHiddenForFullscreen)
        {
            IsHiddenForFullscreen = false;
            Win32.ShowWindow(Hwnd, Win32.SW_SHOWNOACTIVATE);
            FullscreenChanged?.Invoke(false);
        }
    }

    /// <summary>The desktop and the taskbar also count as "full screen" to the shell.</summary>
    private static bool IsDesktop(nint hwnd) =>
        Win32.ClassName(hwnd) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    /// <summary>
    /// Settings › Screen › Active: the island moves to the monitor of whatever window you bring
    /// forward. Windows says when the foreground window changes, so nothing polls, and the hook is
    /// only installed in that mode.
    /// </summary>
    private void FollowForeground(bool follow)
    {
        if (follow == (_foregroundHook != 0)) return;
        if (follow)
        {
            s_followingForeground = this;
            _foregroundHook = Win32.SetWinEventHook(
                Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, 0, &ForegroundProc, 0, 0,
                Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS);
            if (_foregroundHook == 0) Log.Info("active screen: no foreground hook");
            _activeMonitor = MonitorOf(Win32.GetForegroundWindow());
        }
        else
        {
            Win32.UnhookWinEvent(_foregroundHook);
            _foregroundHook = 0;
            _activeMonitor = 0;
            if (s_followingForeground == this) s_followingForeground = null;
        }
    }

    /// <summary>The desktop spans every monitor; for it, the monitor under the pointer that clicked it.</summary>
    private static nint MonitorOf(nint window)
    {
        if (window == 0) return 0;
        if (IsDesktop(window) && Win32.GetCursorPos(out var cursor)) return Win32.MonitorFromPoint(cursor, Win32.MONITOR_DEFAULTTONEAREST);
        return Win32.MonitorFromWindow(window, Win32.MONITOR_DEFAULTTONEAREST);
    }

    [UnmanagedCallersOnly]
    private static void ForegroundProc(nint hook, uint @event, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (s_followingForeground is not { } window) return;
        var monitor = MonitorOf(hwnd);
        if (monitor == 0 || monitor == window.Monitor.Handle) return;
        window._activeMonitor = monitor;
        window.RequestPlace();
    }

    [UnmanagedCallersOnly]
    private static nint SubclassProc(nint hwnd, uint message, nint wParam, nint lParam, nuint id, nuint refData)
    {
        try
        {
            if (GCHandle.FromIntPtr((nint)refData).Target is IslandWindow window
                && window.OnMessage(message, wParam, lParam) is { } result)
            {
                return result;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "window message");
        }
        return Win32.DefSubclassProc(hwnd, message, wParam, lParam);
    }
}
