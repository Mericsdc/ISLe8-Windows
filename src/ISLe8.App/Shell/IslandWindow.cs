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

    private readonly AppSettings _settings;
    private readonly Func<DipSize> _expandedSize;
    private readonly DispatcherQueue _queue;
    private readonly AppWindow _appWindow;
    private GCHandle _self;
    private bool _subclassed;
    private nint _blurRegion;
    private bool _appBarRegistered;
    private bool _placePending;
    private PixelRect? _hitRegion;

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
    }

    /// <summary>Puts the window at the top centre of the chosen monitor, sized for that monitor's DPI.</summary>
    public void Place()
    {
        _placePending = false;
        Monitor = Monitors.Pick(_settings);
        var layout = IslandLayout.Compute(Monitor.Bounds, Monitor.WorkArea, Monitor.Scale, _expandedSize());
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
    public bool CursorIn(PixelRect region)
    {
        if (Layout is not { } layout || !Win32.GetCursorPos(out var cursor)) return false;
        var x = cursor.X - layout.Window.X;
        var y = cursor.Y - layout.Window.Y;
        return x >= region.X && x < region.Right && y >= region.Y && y < region.Bottom;
    }

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

        // Windows 11 rounds and outlines top-level windows; the island draws its own shape.
        var corner = Win32.DWMWCP_DONOTROUND;
        Win32.DwmSetWindowAttribute(Hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, &corner, sizeof(int));
        var border = Win32.DWMWA_COLOR_NONE;
        Win32.DwmSetWindowAttribute(Hwnd, Win32.DWMWA_BORDER_COLOR, &border, sizeof(uint));
    }

    /// <summary>
    /// An app bar that reserves no space: the shell then tells the window when a full-screen app
    /// (a game, a video, a presentation) comes and goes. No polling.
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
            return 0;
        }
        switch (message)
        {
            // WinUI resizes the window to the suggested rectangle first; the island then lays itself
            // out again for the new DPI, which also covers moving to another monitor.
            case Win32.WM_DPICHANGED:
            case Win32.WM_DISPLAYCHANGE:
                RequestPlace();
                break;
            case Win32.WM_SETTINGCHANGE when wParam == Win32.SPI_SETWORKAREA:
                RequestPlace();
                break;
            // Windows can tear the window down without Dispose (logoff, taskkill); the subclass
            // must go before the window does.
            case Win32.WM_NCDESTROY:
                Detach();
                break;
        }
        return null;
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
