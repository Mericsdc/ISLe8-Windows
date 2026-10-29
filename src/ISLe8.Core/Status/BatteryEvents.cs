using ISLe8.Core.Devices;

namespace ISLe8.Core.Status;

public enum BatteryEventKind
{
    PluggedIn,
    Unplugged,
    Low,
    Critical,
}

/// <summary>Plugging in or out, or this computer's battery crossing 20 % or 10 % (SystemModule.swift, BatteryEvent).</summary>
public readonly record struct BatteryEvent(BatteryEventKind Kind, int Percent)
{
    /// <summary>Plugging in or out opens the status ears sideways; low and critical are banners.</summary>
    public bool IsStatus => Kind is BatteryEventKind.PluggedIn or BatteryEventKind.Unplugged;
}

/// <summary>
/// Turns battery readings into events, as SystemModule.swift's batteryChanged does: the first
/// reading only sets the scene, plugging in forgets which warnings were given, and on battery each
/// of 10 % and 20 % is announced once per discharge. There is no "fully charged" event.
/// </summary>
public sealed class BatteryAnnouncer
{
    public const int LowLevel = 20;
    public const int CriticalLevel = 10;

    private readonly HashSet<int> _announced = [];
    private PcBattery? _last;

    /// <summary>The event the new reading brings, if any. With alerts off nothing is announced, but the reading is kept.</summary>
    public BatteryEvent? Changed(PcBattery? state, bool alertsOn)
    {
        var old = _last;
        _last = state;
        if (!alertsOn || old is not { } before || state is not { } now) return null;

        if (now.IsPluggedIn && !before.IsPluggedIn)
        {
            _announced.Clear();
            return new(BatteryEventKind.PluggedIn, now.Percent);
        }
        if (!now.IsPluggedIn && before.IsPluggedIn) return new(BatteryEventKind.Unplugged, now.Percent);
        if (now.IsPluggedIn) return null;
        // 10 first: a drop straight past both says the worse one, once.
        foreach (var threshold in (int[])[CriticalLevel, LowLevel])
        {
            if (now.Percent <= threshold && before.Percent > threshold && _announced.Add(threshold))
            {
                return new(threshold == CriticalLevel ? BatteryEventKind.Critical : BatteryEventKind.Low, now.Percent);
            }
        }
        return null;
    }
}

public enum StatusTint
{
    White,
    Green,
    Orange,
    Red,
}

/// <summary>How the battery is drawn (SystemViews.swift, BatteryChip and BatteryAlertView).</summary>
public static class BatteryLook
{
    /// <summary>The header chip: green on power, red at 10 % or less, orange at 20 % or less.</summary>
    public static StatusTint Chip(PcBattery battery) =>
        battery.IsCharging || battery.IsPluggedIn ? StatusTint.Green
        : battery.Percent <= BatteryAnnouncer.CriticalLevel ? StatusTint.Red
        : battery.Percent <= BatteryAnnouncer.LowLevel ? StatusTint.Orange
        : StatusTint.White;

    /// <summary>The symbol's fill in quarters, 0–4, as battery.0percent … battery.100percent.</summary>
    public static int Quarters(int percent) => percent switch
    {
        >= 88 => 4,
        >= 63 => 3,
        >= 38 => 2,
        >= 13 => 1,
        _ => 0,
    };

    public static StatusTint Event(BatteryEventKind kind) => kind switch
    {
        BatteryEventKind.PluggedIn => StatusTint.Green,
        BatteryEventKind.Unplugged => StatusTint.White,
        BatteryEventKind.Low => StatusTint.Orange,
        _ => StatusTint.Red,
    };
}
