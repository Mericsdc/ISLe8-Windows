using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.Interop;
using Microsoft.UI.Dispatching;

namespace ISLe8.Shell;

/// <summary>
/// Reports a mouse click anywhere on the screen, so a click elsewhere can close the open island.
/// The island never takes focus, so Windows tells it nothing about clicks outside it; a low-level
/// mouse hook is the only way. It is installed only while it matters (the island is open and the
/// pointer is away from it), and the hook itself only queues the click: nothing runs while the
/// island is closed, and the system's mouse never waits on ISLe8.
/// </summary>
internal sealed unsafe class OutsideClickWatcher : IDisposable
{
    /// <summary>The hook procedure is static; it reaches the one watcher through here.</summary>
    private static OutsideClickWatcher? s_active;

    private readonly DispatcherQueue _queue;
    private readonly Action _clicked;
    private nint _hook;

    public OutsideClickWatcher(DispatcherQueue queue, Action clicked)
    {
        _queue = queue;
        _clicked = clicked;
    }

    public bool Enabled
    {
        get => _hook != 0;
        set
        {
            if (value == Enabled) return;
            if (value) Install();
            else Remove();
        }
    }

    public void Dispose() => Remove();

    private void Install()
    {
        s_active = this;
        _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, &HookProc, Win32.GetModuleHandle(null), 0);
        if (_hook == 0) Log.Info("outside clicks: no mouse hook");
    }

    private void Remove()
    {
        if (_hook != 0) Win32.UnhookWindowsHookEx(_hook);
        _hook = 0;
        if (s_active == this) s_active = null;
    }

    [UnmanagedCallersOnly]
    private static nint HookProc(int code, nint message, nint data)
    {
        if (code >= 0
            && message is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN
            && s_active is { } watcher)
        {
            watcher._queue.TryEnqueue(() => watcher._clicked());
        }
        return Win32.CallNextHookEx(0, code, message, data);
    }
}
