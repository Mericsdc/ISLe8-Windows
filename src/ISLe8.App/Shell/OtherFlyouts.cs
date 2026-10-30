using System.Diagnostics;

namespace ISLe8.Shell;

/// <summary>
/// Whether FluentFlyout (FluentFlyout.exe, from the Store or GitHub) is running: it shows its own volume and
/// media flyouts on the same keys. Asked when a HUD or a banner is about to show, at most every 15 seconds.
/// </summary>
internal static class OtherFlyouts
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(15);
    private static DateTimeOffset s_askedAt = DateTimeOffset.MinValue;
    private static bool s_running;

    public static bool FluentFlyoutRunning()
    {
        if (DateTimeOffset.Now - s_askedAt < Every) return s_running;
        s_askedAt = DateTimeOffset.Now;
        var processes = Process.GetProcessesByName("FluentFlyout");
        s_running = processes.Length > 0;
        foreach (var process in processes) process.Dispose();
        return s_running;
    }
}
