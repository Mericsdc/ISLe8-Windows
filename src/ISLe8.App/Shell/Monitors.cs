using System.Runtime.InteropServices;
using ISLe8.Core.Geometry;
using ISLe8.Core.Settings;
using ISLe8.Interop;

namespace ISLe8.Shell;

internal readonly record struct MonitorInfo(
    nint Handle, string Device, PixelRect Bounds, PixelRect WorkArea, uint Dpi, bool IsPrimary)
{
    public double Scale => Dpi / 96.0;

    /// <summary>"1" for <c>\\.\DISPLAY1</c>: the number Windows shows in Settings › Display.</summary>
    public string Number
    {
        get
        {
            var start = Device.Length;
            while (start > 0 && char.IsAsciiDigit(Device[start - 1])) start--;
            return Device[start..];
        }
    }
}

internal static unsafe class Monitors
{
    public static List<MonitorInfo> All()
    {
        var found = new List<MonitorInfo>();
        var handle = GCHandle.Alloc(found);
        try
        {
            Win32.EnumDisplayMonitors(0, 0, &Collect, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return found.OrderBy(monitor => monitor.Bounds.X).ThenBy(monitor => monitor.Bounds.Y).ToList();
    }

    /// <summary>The monitor the settings ask for (<see cref="DisplayChoice"/>); <paramref name="active"/> is the one you last used.</summary>
    public static MonitorInfo Pick(AppSettings settings, nint active = 0)
    {
        var all = All();
        var index = DisplayChoice.Pick(
            all.Select(monitor => new DisplaySummary(monitor.Device, monitor.IsPrimary)).ToList(),
            settings,
            all.FindIndex(monitor => monitor.Handle == active && active != 0));
        return index >= 0
            ? all[index]
            : new MonitorInfo(0, "", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 96, true);
    }

    /// <summary>The DPI of the main monitor, where the taskbar and its notification area are.</summary>
    public static uint PrimaryDpi() => All().FirstOrDefault(monitor => monitor.IsPrimary) is { Handle: not 0 } primary ? primary.Dpi : 96;

    public static bool TryGet(nint monitor, out MonitorInfo info)
    {
        var raw = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
        if (!Win32.GetMonitorInfo(monitor, ref raw))
        {
            info = default;
            return false;
        }
        if (Win32.GetDpiForMonitor(monitor, Win32.MDT_EFFECTIVE_DPI, out var dpi, out _) != 0 || dpi == 0) dpi = 96;
        info = new MonitorInfo(
            monitor,
            new string(raw.szDevice),
            raw.rcMonitor.ToPixelRect(),
            raw.rcWork.ToPixelRect(),
            dpi,
            (raw.dwFlags & Win32.MONITORINFOF_PRIMARY) != 0);
        return true;
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint monitor, nint hdc, RECT* bounds, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<MonitorInfo> found && TryGet(monitor, out var info))
        {
            found.Add(info);
        }
        return 1;
    }
}
