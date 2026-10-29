using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.Interop;
using ISLe8.Localization;
using Microsoft.Win32;

namespace ISLe8.Shell;

/// <summary>
/// ISLe8's icon in the notification area, the Windows counterpart of the Mac's menu bar icon
/// (StatusItemController.swift): the island has no taskbar button, so this is how you reach
/// Settings or quit. A click opens Settings; the context menu has Settings and Quit, as on the Mac.
/// It listens on the island's window, so it costs no window of its own.
/// </summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint Id = 1;
    private const int SettingsCommand = 1;
    private const int QuitCommand = 2;
    private const int UpdateCommand = 3;

    private static readonly uint CallbackMessage = Win32.RegisterWindowMessage("ISLe8.Tray");
    /// <summary>Sent to every top-level window when Explorer (re)starts; the icon must be added again.</summary>
    private static readonly uint TaskbarCreated = Win32.RegisterWindowMessage("TaskbarCreated");

    private readonly nint _hwnd;
    private readonly Func<uint> _dpi;
    private nint _icon;
    private bool _added;

    /// <param name="hwnd">The window that takes the icon's messages; it must pass them to <see cref="HandleMessage"/>.</param>
    /// <param name="dpi">The DPI of the screen with the taskbar, for the icon's size.</param>
    public TrayIcon(nint hwnd, Func<uint> dpi)
    {
        _hwnd = hwnd;
        _dpi = dpi;
    }

    public event Action? SettingsRequested;
    public event Action? QuitRequested;

    /// <summary>The Update item, shown first in the menu while a newer ISLe8 is out.</summary>
    public event Action? UpdateRequested;

    /// <summary>The Update item's text, or null for none.</summary>
    public string? UpdateLabel { get; set; }

    public void Show()
    {
        LoadIcon();
        Add();
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = Data(0);
            Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
            _added = false;
        }
        if (_icon != 0)
        {
            Win32.DestroyIcon(_icon);
            _icon = 0;
        }
    }

    /// <summary>A message for the island's window; returns a result when it was the icon's.</summary>
    public nint? HandleMessage(uint message, nint wParam, nint lParam)
    {
        if (message == CallbackMessage)
        {
            // NOTIFYICON_VERSION_4: the event in the low word of lParam, the anchor point in wParam.
            switch ((uint)(lParam & 0xFFFF))
            {
                case Win32.NIN_SELECT or Win32.NIN_KEYSELECT:
                    SettingsRequested?.Invoke();
                    break;
                case Win32.WM_CONTEXTMENU:
                    ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                    break;
            }
            return 0;
        }
        if (message == TaskbarCreated)
        {
            _added = false;
            Show();
        }
        else if (message is Win32.WM_SETTINGCHANGE && lParam != 0 && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet"
            || message is Win32.WM_DPICHANGED or Win32.WM_DISPLAYCHANGE)
        {
            // The taskbar went light or dark, or the screen's scale changed: the icon follows.
            LoadIcon();
            if (_added)
            {
                var data = Data(Win32.NIF_ICON);
                Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
            }
        }
        return null;
    }

    private void Add()
    {
        if (_added) return;
        var data = Data(Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP);
        if (!Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data))
        {
            // No taskbar yet (signing in): TaskbarCreated will bring it.
            Log.Info("tray: not added");
            return;
        }
        data.uVersion = Win32.NOTIFYICON_VERSION_4;
        Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref data);
        _added = true;
    }

    private NOTIFYICONDATAW Data(uint flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = Id,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        "ISLe8".AsSpan().CopyTo(new Span<char>(data.szTip, 127));
        return data;
    }

    /// <summary>A white capsule on a dark taskbar, a black one on a light taskbar, at the taskbar's scale.</summary>
    private void LoadIcon()
    {
        var name = TaskbarIsLight() ? "TrayOnLight.ico" : "TrayOnDark.ico";
        var size = Win32.GetSystemMetricsForDpi(Win32.SM_CXSMICON, _dpi());
        var icon = Win32.LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", name), Win32.IMAGE_ICON, size, size, Win32.LR_LOADFROMFILE);
        if (icon == 0)
        {
            Log.Info($"tray: {name} not loaded");
            return;
        }
        if (_icon != 0) Win32.DestroyIcon(_icon);
        _icon = icon;
    }

    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int light && light != 0;
    }

    private void ShowMenu(int x, int y)
    {
        var menu = Win32.CreatePopupMenu();
        if (UpdateLabel is { } update)
        {
            Win32.AppendMenu(menu, Win32.MF_STRING, UpdateCommand, update);
            Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
        }
        Win32.AppendMenu(menu, Win32.MF_STRING, SettingsCommand, L.Get("Tray_Settings"));
        Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
        Win32.AppendMenu(menu, Win32.MF_STRING, QuitCommand, L.Get("Menu_Quit"));

        // A tray menu only closes on a click elsewhere if its window has the foreground first.
        Win32.SetForegroundWindow(_hwnd);
        var command = Win32.TrackPopupMenuEx(menu, Win32.TPM_RIGHTBUTTON | Win32.TPM_RETURNCMD | Win32.TPM_NONOTIFY, x, y, _hwnd, 0);
        Win32.PostMessage(_hwnd, Win32.WM_NULL, 0, 0);
        Win32.DestroyMenu(menu);

        if (command == SettingsCommand) SettingsRequested?.Invoke();
        else if (command == QuitCommand) QuitRequested?.Invoke();
        else if (command == UpdateCommand) UpdateRequested?.Invoke();
    }
}
