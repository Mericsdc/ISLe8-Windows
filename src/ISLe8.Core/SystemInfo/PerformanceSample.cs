using System.Globalization;

namespace ISLe8.Core.SystemInfo;

/// <summary>
/// The performance pill's figures (Settings › General › Performance; Windows only). Each is null when
/// this computer can't say it: frames need ETW, the CPU's temperature a sensor program.
/// </summary>
public sealed record PerformanceSample(int? Fps, double Cpu, double? Gpu, double? GpuTemperature, double? CpuTemperature);

/// <summary>The busiest GPU: how busy, 0–1, and its adapter LUID.</summary>
public sealed record GpuUse(double Busy, long? Luid);

/// <summary>When the performance pill shows (Windows only).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(Theming.LenientEnumConverter<PerformancePill>))]
public enum PerformancePill
{
    Off,
    /// <summary>While a game is in front; the island then stays over it, still.</summary>
    Gaming,
    Always,
}

public static class PerformanceMath
{
    /// <summary>
    /// One GPU's busy share from Windows' "GPU Engine(*engtype_3D)" counters, as Task Manager shows it:
    /// every process's 3D engine summed per adapter (the instance's <c>luid_0x…_0x…_phys_N</c>), the busiest
    /// adapter, with its LUID so its temperature can be asked for.
    /// </summary>
    public static GpuUse? Gpu(IEnumerable<(string Instance, double Percent)> engines)
    {
        var adapters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (instance, percent) in engines)
        {
            var start = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
            var end = instance.IndexOf("_eng_", StringComparison.OrdinalIgnoreCase);
            if (start < 0 || end <= start) continue;
            var adapter = instance[start..end];
            adapters[adapter] = adapters.GetValueOrDefault(adapter) + Math.Max(percent, 0);
        }
        if (adapters.Count == 0) return null;
        var (busiest, total) = adapters.MaxBy(pair => pair.Value);
        return new GpuUse(Math.Clamp(total / 100, 0, 1), Luid(busiest));
    }

    /// <summary>"luid_0x00000000_0x000112AF_phys_0" → 0x000112AF (high part first in the name).</summary>
    public static long? Luid(string adapter)
    {
        var parts = adapter.Split('_');
        if (parts.Length < 3
            || !uint.TryParse(parts[1].Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var high)
            || !uint.TryParse(parts[2].Replace("0x", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low))
        {
            return null;
        }
        return (long)high << 32 | low;
    }

    /// <summary>Frames counted over <paramref name="seconds"/>, rounded; null with nothing to count.</summary>
    public static int? Fps(int frames, double seconds) => seconds > 0 && frames > 0 ? (int)Math.Round(frames / seconds) : null;

    /// <summary>"64°": whole degrees, as the weather shows them.</summary>
    public static string Degrees(double celsius) => $"{(int)Math.Round(celsius, MidpointRounding.AwayFromZero)}°";

    /// <summary>A sensor's reading worth showing: a real temperature, not a placeholder 0 or a wild value.</summary>
    public static double? Temperature(double? celsius) => celsius is > 1 and < 150 ? celsius : null;

    /// <summary>"%42" in Turkish, "42%" in English.</summary>
    public static string Percent(double fraction, CultureInfo culture) => SystemMath.Percent(fraction, culture);
}
