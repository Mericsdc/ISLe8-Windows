using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using Microsoft.UI.Dispatching;

namespace ISLe8.Status;

/// <summary>
/// Keyboard cleaning (KeyboardLock.swift, PORTING.md §3.21): while on, a low-level keyboard hook keeps every key,
/// modifier and media key from Windows and every program, typed or sent (SendInput), so the keyboard can be wiped
/// without typing anything. The mouse turns it off from the island, whenever the user likes: unlike the Mac's, it
/// has no two-minute limit (user's request). It also goes when the session is locked (Ctrl+Alt+Del › Lock, the way
/// out without a mouse) and with ISLe8 (Windows removes a quitting program's hooks). No permission is needed,
/// unlike the Mac's tap. Windows still takes Ctrl+Alt+Del, a window
/// running as administrator still gets keys while it is in front, and keys a laptop handles in its firmware (some
/// Fn keys) never reach the hook. The hook is there only while on, on a thread of its own that does nothing else,
/// as for <see cref="VolumeKeys"/>: Windows drops a hook whose thread is slow to answer.
/// </summary>
internal sealed unsafe partial class KeyboardLock : IDisposable
{
    private const uint WM_QUIT = 0x0012;
    private const uint Hook = 0x8000 + 1;   // WM_APP + 1
    private const uint Unhook = 0x8000 + 2; // WM_APP + 2

    /// <summary>Keys pressed while on, for the log.</summary>
    private static int s_kept;

    private readonly DispatcherQueue _queue;
    private uint _threadId;
    /// <summary>Environment.TickCount64 when it was turned on, for the log.</summary>
    private long _started;

    public KeyboardLock(DispatcherQueue queue) => _queue = queue;

    public bool IsOn { get; private set; }

    /// <summary>Turned on or off, on the UI thread.</summary>
    public event Action? Changed;

    public void Set(bool on)
    {
        if (on == IsOn) return;
        if (on)
        {
            Interlocked.Exchange(ref s_kept, 0);
            IsOn = true;
            _started = Environment.TickCount64;
            PostThreadMessageW(HookThread(), Hook, 0, 0);
            Log.Info("keyboard lock: on");
        }
        else
        {
            PostThreadMessageW(_threadId, Unhook, 0, 0);
            Log.Info($"keyboard lock: off after {TimeSpan.FromMilliseconds(Environment.TickCount64 - _started):h\\:mm\\:ss}, {Volatile.Read(ref s_kept)} keys kept");
            IsOn = false;
        }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        IsOn = false;
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, 0, 0);
        _threadId = 0;
    }

    /// <summary>The hook's thread, started the first time; it waits for messages to put the hook in and take it out, in order.</summary>
    private uint HookThread()
    {
        if (_threadId != 0) return _threadId;
        var ready = new ManualResetEventSlim();
        new Thread(() => Run(ready)) { IsBackground = true, Name = "ISLe8 keyboard lock", Priority = ThreadPriority.AboveNormal }.Start();
        ready.Wait();
        return _threadId;
    }

    private void Run(ManualResetEventSlim ready)
    {
        Message message;
        // Gives this thread its message queue before anyone posts to it.
        PeekMessageW(&message, 0, 0, 0, 0 /* PM_NOREMOVE */);
        _threadId = GetCurrentThreadId();
        ready.Set();
        nint hook = 0;
        while (GetMessageW(&message, 0, 0, 0) > 0)
        {
            if (message.Id == Hook && hook == 0)
            {
                hook = SetWindowsHookExW(13 /* WH_KEYBOARD_LL */, &Proc, GetModuleHandleW(null), 0);
                if (hook != 0) continue;
                Log.Info($"keyboard lock: SetWindowsHookEx failed ({Marshal.GetLastPInvokeError()})");
                // The button shouldn't say locked while every key still works.
                _queue.TryEnqueue(() => Set(false));
            }
            else if (message.Id == Unhook && hook != 0)
            {
                UnhookWindowsHookEx(hook);
                hook = 0;
            }
        }
        if (hook != 0) UnhookWindowsHookEx(hook);
    }

    /// <summary>Every key, down and up, goes no further: the hook is only there while the lock is on.</summary>
    [UnmanagedCallersOnly]
    private static nint Proc(int code, nint wParam, nint lParam)
    {
        if (code < 0) return CallNextHookEx(0, code, wParam, lParam);
        if (wParam is 0x0100 or 0x0104) Interlocked.Increment(ref s_kept); // WM_KEYDOWN, WM_SYSKEYDOWN
        return 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int id, delegate* unmanaged<int, nint, nint, nint> proc, nint module, uint thread);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial int GetMessageW(Message* message, nint window, uint first, uint last);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(Message* message, nint window, uint first, uint last, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint thread, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
