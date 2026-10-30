using System.Runtime.InteropServices;
using ISLe8.UI;
using Microsoft.UI.Dispatching;

namespace ISLe8.Shell;

/// <summary>
/// Once the island has been back at the pill for a few seconds, the memory only the open island
/// touches (its tabs' text, images and render caches) is handed back to Windows, as Windows does
/// for a minimised window. The pages stay in Windows' standby list, so reopening is just as quick;
/// what Task Manager shows for ISLe8 is then what the pill really uses.
/// </summary>
internal sealed partial class MemoryTrim(DispatcherQueue queue)
{
    public static readonly TimeSpan Quiet = TimeSpan.FromSeconds(5);

    private DispatcherQueueTimer? _timer;

    /// <summary>The island came to rest: trims after <see cref="Quiet"/>, unless it opens again first.</summary>
    public void Schedule()
    {
        if (_timer is null)
        {
            _timer = queue.CreateTimer();
            _timer.IsRepeating = false;
            _timer.Interval = Quiet;
            _timer.OnTick(() => SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1));
        }
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>The island opened: nothing to trim.</summary>
    public void Cancel() => _timer?.Stop();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSize(nint process, nint minimum, nint maximum);
}
