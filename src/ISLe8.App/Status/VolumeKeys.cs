using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.UI;
using Microsoft.UI.Dispatching;

namespace ISLe8.Status;

/// <summary>
/// "Use ISLe8's HUD instead of the system's" (HUDModule.swift, MediaKeyTap; PORTING.md §3.2): the
/// volume keys reach ISLe8 first, through a low-level keyboard hook on a thread of its own, and ISLe8
/// sets the volume itself, so Windows never shows its own HUD. Only the three volume keys are
/// looked at; every other key goes straight on, unread. With no output device the keys are left to Windows. Brightness keys never come through a keyboard
/// hook, so Windows keeps its brightness HUD.
/// </summary>
internal sealed unsafe partial class VolumeKeys : IDisposable
{
    public const int Mute = 0xAD;
    public const int Down = 0xAE;
    public const int Up = 0xAF;

    private static VolumeKeys? s_active;

    private readonly DispatcherQueue _queue;
    private readonly Func<int, bool> _canHandle;
    private readonly Action<int> _press;
    private Thread? _thread;
    private uint _threadId;
    /// <summary>The keys whose press was kept from Windows: their release is kept too.</summary>
    private int _held;

    /// <param name="canHandle">Whether ISLe8 can act on this key now; asked on the hook's thread, so it must be quick.</param>
    /// <param name="press">The key, on the UI thread.</param>
    public VolumeKeys(DispatcherQueue queue, Func<int, bool> canHandle, Action<int> press)
    {
        _queue = queue;
        _canHandle = canHandle;
        _press = press;
    }

    public bool IsRunning => _thread is not null;

    public void SetEnabled(bool enabled)
    {
        if (enabled == IsRunning) return;
        if (enabled)
        {
            s_active = this;
            _thread = new Thread(Run) { IsBackground = true, Name = "ISLe8 volume keys", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }
        else
        {
            if (_threadId != 0) PostThreadMessageW(_threadId, 0x0012 /* WM_QUIT */, 0, 0);
            _thread = null;
            _threadId = 0;
            _held = 0;
        }
    }

    public void Dispose() => SetEnabled(false);

    /// <summary>The hook lives on this thread, which only waits for messages so Windows can call it.</summary>
    private void Run()
    {
        var self = Thread.CurrentThread;
        _threadId = GetCurrentThreadId();
        var hook = SetWindowsHookExW(13 /* WH_KEYBOARD_LL */, &Proc, GetModuleHandleW(null), 0);
        if (hook == 0)
        {
            Log.Info($"volume keys: SetWindowsHookEx failed ({Marshal.GetLastPInvokeError()})");
            return;
        }
        // Turned off again before this thread had a name to be told by.
        if (!ReferenceEquals(_thread, self))
        {
            UnhookWindowsHookEx(hook);
            return;
        }
        Log.Info("volume keys: taken over from Windows");
        Message message;
        while (GetMessageW(&message, 0, 0, 0) > 0)
        {
        }
        UnhookWindowsHookEx(hook);
        Log.Info("volume keys: back to Windows");
    }

    [UnmanagedCallersOnly]
    private static nint Proc(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && s_active is { } keys)
        {
            var vk = ((KeyboardHookData*)lParam)->VirtualKey;
            if (vk is Mute or Down or Up)
            {
                var bit = 1 << (int)(vk - Mute);
                var down = wParam is 0x0100 or 0x0104; // WM_KEYDOWN, WM_SYSKEYDOWN
                if (down && keys._canHandle((int)vk))
                {
                    keys._held |= bit;
                    keys._queue.Post(() => keys._press((int)vk));
                    return 1;
                }
                if (!down && (keys._held & bit) != 0)
                {
                    keys._held &= ~bit;
                    return 1;
                }
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
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
    private static partial bool PostThreadMessageW(uint thread, uint message, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
