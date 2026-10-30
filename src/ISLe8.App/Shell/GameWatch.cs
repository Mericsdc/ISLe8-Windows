using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.Status;
using ISLe8.Interop;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace ISLe8.Shell;

/// <summary>
/// Whether a game is in front: the foreground window's program is one Windows took for a game
/// (<see cref="Games"/>), or Windows says a Direct3D program has the screen to itself. A game in a
/// window is what this is for: the island then stops what moves on it and, if asked, steps aside,
/// so the compositor isn't drawing it every frame over the game. Windows says when the foreground
/// window changes, so nothing polls. Windows only.
/// </summary>
internal sealed unsafe partial class GameWatch(DispatcherQueue queue) : IDisposable
{
    /// <summary>Game Bar can write a new game down a moment after it starts; it is looked for once more after this.</summary>
    private static readonly TimeSpan LookAgain = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ListAge = TimeSpan.FromMinutes(1);
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    private static GameWatch? s_current;
    private nint _hook;
    private HashSet<string> _games = [];
    private DateTimeOffset _gamesReadAt = DateTimeOffset.MinValue;
    private DispatcherQueueTimer? _again;
    private nint _waiting;

    public bool IsGameInFront { get; private set; }

    /// <summary>The game's window while <see cref="IsGameInFront"/>, for its monitor.</summary>
    public nint GameWindow { get; private set; }

    /// <summary>The game's process, for its frames.</summary>
    public uint GameProcess { get; private set; }

    /// <summary>A game came to the front or left it, on the UI thread.</summary>
    public event Action? Changed;

    public void Start()
    {
        if (_hook != 0) return;
        s_current = this;
        _hook = Win32.SetWinEventHook(
            Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, 0, &ForegroundProc, 0, 0,
            // ISLe8's own windows too: its Settings coming forward as a game quits must count as the game leaving.
            Win32.WINEVENT_OUTOFCONTEXT);
        if (_hook == 0) Log.Info("games: no foreground hook");
        Check(Win32.GetForegroundWindow(), lookAgain: true);
    }

    public void Dispose()
    {
        _again?.Stop();
        if (_hook != 0) Win32.UnhookWinEvent(_hook);
        _hook = 0;
        if (s_current == this) s_current = null;
    }

    /// <summary>Only hands the window over: an exception escaping a native callback would end the process unlogged.</summary>
    [UnmanagedCallersOnly]
    private static void ForegroundProc(nint hook, uint @event, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        s_current?.Arrived(hwnd);
    }

    private void Arrived(nint window) => queue.Post(() => Check(window, lookAgain: true));

    private void Check(nint window, bool lookAgain)
    {
        _again?.Stop();
        var game = window != 0 && (IsKnownGame(window, readAgain: !lookAgain) || HasScreenToItself(window));
        if (!game && lookAgain && window != 0)
        {
            if (_again is null)
            {
                _again = queue.CreateTimer();
                _again.IsRepeating = false;
                _again.Interval = LookAgain;
                _again.OnTick(() =>
                {
                    if (Win32.GetForegroundWindow() == _waiting) Check(_waiting, lookAgain: false);
                });
            }
            _waiting = window;
            _again.Start();
        }
        if (game == IsGameInFront && (!game || window == GameWindow)) return;
        IsGameInFront = game;
        GameWindow = game ? window : 0;
        GameProcess = game && GetWindowThreadProcessId(window, out var pid) != 0 ? pid : 0;
        Log.Info(game ? $"game in front: {Path.GetFileName(Program(window))}" : "game left the front");
        Changed?.Invoke();
    }

    private bool IsKnownGame(nint window, bool readAgain)
    {
        if (Program(window) is not { } program) return false;
        if (readAgain || DateTimeOffset.Now - _gamesReadAt > ListAge) ReadGames();
        return Games.IsGame(program, _games);
    }

    private void ReadGames()
    {
        _gamesReadAt = DateTimeOffset.Now;
        try
        {
            using var children = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore\Children");
            if (children is null) return;
            var paths = new List<string?>();
            foreach (var name in children.GetSubKeyNames())
            {
                using var child = children.OpenSubKey(name);
                paths.Add(child?.GetValue("MatchedExeFullPath") as string);
            }
            _games = Games.Set(paths);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error(e, "games");
        }
    }

    /// <summary>
    /// Windows says a Direct3D program has the screen to itself, and this window covers its monitor. Windows' answer is
    /// for the whole session: right after Alt+Tab out of a game it still says so for the window you switched to.
    /// </summary>
    private static bool HasScreenToItself(nint window) =>
        SHQueryUserNotificationState(out var state) == 0 && state == QUNS_RUNNING_D3D_FULL_SCREEN
        && GetWindowRect(window, out var rect) && Monitors.TryGet(Win32.MonitorFromWindow(window, Win32.MONITOR_DEFAULTTONEAREST), out var monitor)
        && rect.Left <= monitor.Bounds.X && rect.Top <= monitor.Bounds.Y && rect.Right >= monitor.Bounds.Right && rect.Bottom >= monitor.Bounds.Bottom;

    /// <summary>The window's program, as a full path; limited access is enough, even for a game its anti-cheat guards.</summary>
    private static string? Program(nint window)
    {
        if (GetWindowThreadProcessId(window, out var pid) == 0) return null;
        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (process == 0) return null;
        try
        {
            var buffer = stackalloc char[1024];
            var length = 1024;
            return QueryFullProcessImageNameW(process, 0, buffer, ref length) ? new string(buffer, 0, length) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(nint process, uint flags, char* name, ref int size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out RECT rect);

    [LibraryImport("shell32.dll")]
    private static partial int SHQueryUserNotificationState(out int state);
}
