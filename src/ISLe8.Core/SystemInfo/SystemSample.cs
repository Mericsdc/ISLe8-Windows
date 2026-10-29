using System.Globalization;

namespace ISLe8.Core.SystemInfo;

/// <summary>One of the user's processes using CPU (SystemStats.swift, Process).</summary>
/// <param name="Cpu">Percent of one core, as Task Manager's details and Activity Monitor show it; above 100 on several cores.</param>
public sealed record ProcessUse(int Id, string Name, double Cpu, ulong Memory);

/// <summary>The System tab's figures at one moment (SystemStats.swift, Sample).</summary>
public sealed record SystemSample
{
    public required double Cpu { get; init; }
    public required int Cores { get; init; }
    public required ulong MemoryUsed { get; init; }
    public required ulong MemoryTotal { get; init; }
    public required long DiskTotal { get; init; }
    public required long DiskFree { get; init; }

    /// <summary>Bytes a second over Ethernet and Wi-Fi.</summary>
    public required double Download { get; init; }
    public required double Upload { get; init; }
    public required IReadOnlyList<ProcessUse> Top { get; init; }
}

public enum NetworkLink
{
    Wired,
    WiFi,
    Cellular,
    Other,
    Offline,
}

/// <summary>The arithmetic behind the samples, kept apart from Windows so it can be tested.</summary>
public static class SystemMath
{
    /// <summary>
    /// The share of all cores busy between two GetSystemTimes readings. Windows counts idle time
    /// inside kernel time, so busy = kernel − idle + user.
    /// </summary>
    public static double Cpu((ulong Idle, ulong Kernel, ulong User) before, (ulong Idle, ulong Kernel, ulong User) after)
    {
        var idle = (double)(after.Idle - before.Idle);
        var total = (double)(after.Kernel - before.Kernel) + (after.User - before.User);
        return total > 0 ? Math.Clamp((total - idle) / total, 0, 1) : 0;
    }

    /// <summary>A process's CPU over <paramref name="seconds"/>, in percent of one core, from its times in 100 ns ticks.</summary>
    public static double ProcessCpu(long ticksBefore, long ticksAfter, double seconds) =>
        seconds > 0 && ticksAfter >= ticksBefore ? (ticksAfter - ticksBefore) / (seconds * 1e7) * 100 : 0;

    /// <summary>Bytes a second; nothing when a counter went backwards (an adapter came or went).</summary>
    public static double Rate(ulong before, ulong after, double seconds) =>
        seconds > 0 && after >= before ? (after - before) / seconds : 0;

    /// <summary>Gauges go green, orange from 60 %, red from 85 %.</summary>
    public static GaugeLevel Level(double fraction) => fraction >= 0.85 ? GaugeLevel.High : fraction >= 0.6 ? GaugeLevel.Busy : GaugeLevel.Fine;

    /// <summary>"%42" in Turkish, "42%" in English, as the Mac's percent style.</summary>
    public static string Percent(double fraction, CultureInfo culture) =>
        Math.Clamp(fraction, 0, 9.99).ToString("P0", culture);

    /// <summary>
    /// "16 GB", "1,2 MB", "512 KB", after the Mac's ByteCountFormatter: memory counts in 1024s, files
    /// in 1000s; more decimals for bigger units, trailing zeros dropped.
    /// </summary>
    public static string Bytes(ulong count, bool memory, CultureInfo culture)
    {
        double unit = memory ? 1024 : 1000;
        string[] names = ["bayt", "KB", "MB", "GB", "TB"];
        if (count < unit) return culture.TwoLetterISOLanguageName == "tr" ? $"{count} bayt" : $"{count} bytes";
        var value = (double)count;
        var index = 0;
        while (value >= unit && index < names.Length - 1)
        {
            value /= unit;
            index++;
        }
        var decimals = index switch { 1 => 0, 2 => 1, _ => 2 };
        return $"{Math.Round(value, decimals).ToString("0." + new string('#', decimals), culture)} {names[index]}";
    }
}

public enum GaugeLevel
{
    Fine,
    Busy,
    High,
}
