namespace ISLe8.Core.Devices;

/// <summary>What a device is, for its icon (DevicesModule.swift, WirelessDevice.Kind).</summary>
public enum DeviceKind
{
    AirPods,
    AirPodsPro,
    AirPodsMax,
    Beats,
    Headphones,
    Speaker,
    Keyboard,
    Mouse,
    Trackpad,
    Gamepad,
    Phone,
    Other,
}

/// <summary>
/// A connected wireless device that reports its battery: AirPods (each bud and the case), Beats,
/// headphones, keyboards, mice, game controllers (DevicesModule.swift, WirelessDevice). Levels are
/// 0–100; a missing one is a part the device doesn't report.
/// </summary>
public sealed record WirelessDevice(
    string Id, string Name, DeviceKind Kind, int? Main, int? Left = null, int? Right = null, int? Case = null, bool IsCharging = false)
{
    /// <summary>"AirPods Pro 3" out of "Deniz (AirPods Pro 3)", "AirPods Pro" out of "Deniz's AirPods Pro".</summary>
    public string ShortName
    {
        get
        {
            var open = Name.LastIndexOf('(');
            var close = Name.LastIndexOf(')');
            if (open >= 0 && close > open) return Name[(open + 1)..close];
            foreach (var marker in new[] { "’s ", "'s " })
            {
                var at = Name.IndexOf(marker, StringComparison.Ordinal);
                if (at >= 0) return Name[(at + marker.Length)..];
            }
            return Name;
        }
    }

    /// <summary>The level that runs out first; the case doesn't count.</summary>
    public int? Lowest => new[] { Main, Left, Right }.Where(level => level is not null).Min();

    public bool HasBuds => Left is not null || Right is not null;
}

/// <summary>A ring's colour: green, orange from 30 %, red from 15 % (DeviceViews.swift, IconRing).</summary>
public enum BatteryTint
{
    Green,
    Orange,
    Red,
}

/// <summary>What Windows knows about a Bluetooth device's type: the classic Class of Device, or the LE appearance.</summary>
public readonly record struct DeviceClass(int? CodMajor = null, int? CodMinor = null, int? AppearanceCategory = null, int? AppearanceSubcategory = null);

/// <summary>The Devices tab's rules, from DevicesModule.swift and DeviceViews.swift.</summary>
public static class DeviceRules
{
    /// <summary>At or below this a device is low: a red ring and one warning.</summary>
    public const int LowLevel = 15;

    /// <summary>A device warned about is warned again only after it charged above this.</summary>
    public const int RecoveredLevel = 30;

    /// <summary>At most this many rings share a row of cards.</summary>
    public const int RingsPerRow = 5;

    /// <summary>Apple's vendor id in Bluetooth hardware ids (<c>_VID&amp;0001004c</c>) and its company id in advertisements.</summary>
    public const int AppleVendor = 0x004C;

    private static readonly int[] AirPodsMaxModels = [0x200A, 0x201F];
    private static readonly int[] AirPodsProModels = [0x200E, 0x2014, 0x2024, 0x2027];
    private static readonly int[] AirPodsModels = [0x2002, 0x200F, 0x2013, 0x2019, 0x201B];

    public static BatteryTint Tint(int level) => level <= LowLevel ? BatteryTint.Red : level <= RecoveredLevel ? BatteryTint.Orange : BatteryTint.Green;

    /// <summary>
    /// Renamed AirPods ("Deniz's AirPods") still carry their model in the name or in Apple's product id;
    /// other devices say what they are in their Class of Device or LE appearance, else in their name.
    /// </summary>
    public static DeviceKind Kind(string name, int? appleModel, DeviceClass type)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("airpods max") || appleModel is { } max && AirPodsMaxModels.Contains(max)) return DeviceKind.AirPodsMax;
        if (lower.Contains("airpods pro") || appleModel is { } pro && AirPodsProModels.Contains(pro)) return DeviceKind.AirPodsPro;
        if (lower.Contains("airpods") || appleModel is { } pods && AirPodsModels.Contains(pods)) return DeviceKind.AirPods;
        if (lower.Contains("beats")) return DeviceKind.Beats;
        if (FromClass(type) is { } kind) return kind;
        if (lower.Contains("keyboard") || lower.Contains("klavye")) return DeviceKind.Keyboard;
        if (lower.Contains("mouse") || lower.Contains("fare")) return DeviceKind.Mouse;
        if (lower.Contains("trackpad")) return DeviceKind.Trackpad;
        return DeviceKind.Other;
    }

    /// <summary>Bluetooth's assigned numbers: major 2 phone, 4 audio/video, 5 peripheral; LE categories 15 HID, 33 audio sink, 37 wearable audio.</summary>
    private static DeviceKind? FromClass(DeviceClass type)
    {
        switch (type.CodMajor)
        {
            case 2:
                return DeviceKind.Phone;
            case 4:
                return type.CodMinor switch
                {
                    1 or 2 or 6 => DeviceKind.Headphones,
                    5 or 7 or 8 or 10 or 15 => DeviceKind.Speaker,
                    _ => null,
                };
            case 5 when type.CodMinor is { } minor:
                if ((minor & 0x0F) is 1 or 2) return DeviceKind.Gamepad;
                return (minor & 0x30) switch
                {
                    0x20 => DeviceKind.Mouse,
                    0x10 or 0x30 => DeviceKind.Keyboard,
                    _ => null,
                };
        }
        return type.AppearanceCategory switch
        {
            1 => DeviceKind.Phone,
            15 => type.AppearanceSubcategory switch
            {
                1 => DeviceKind.Keyboard,
                2 => DeviceKind.Mouse,
                3 or 4 => DeviceKind.Gamepad,
                _ => null,
            },
            33 => DeviceKind.Speaker,
            37 => DeviceKind.Headphones,
            _ => null,
        };
    }

    /// <summary>Apple's product id out of a Bluetooth hardware id such as <c>BTHENUM\{…}_VID&amp;0001004c_PID&amp;2014</c>.</summary>
    public static int? AppleModel(IEnumerable<string> hardwareIds)
    {
        foreach (var id in hardwareIds)
        {
            var upper = id.ToUpperInvariant();
            var vendor = upper.IndexOf("_VID&", StringComparison.Ordinal);
            var product = upper.IndexOf("_PID&", StringComparison.Ordinal);
            if (vendor < 0 || product < 0 || upper.Length < product + 9) continue;
            var vendorText = upper[(vendor + 5)..product];
            if (!int.TryParse(vendorText[^4..], System.Globalization.NumberStyles.HexNumber, null, out var vendorId) || vendorId != AppleVendor) continue;
            if (int.TryParse(upper.AsSpan(product + 5, 4), System.Globalization.NumberStyles.HexNumber, null, out var model)) return model;
        }
        return null;
    }

    /// <summary>
    /// One entry per name, every field filled, sorted by name: a device can show up twice, over
    /// Bluetooth LE and classic Bluetooth (DevicesModule.swift, merged).
    /// </summary>
    public static IReadOnlyList<WirelessDevice> Merge(IEnumerable<WirelessDevice> devices)
    {
        var result = new List<WirelessDevice>();
        foreach (var device in devices)
        {
            var index = result.FindIndex(kept => kept.Name == device.Name);
            if (index < 0)
            {
                result.Add(device);
                continue;
            }
            var kept = result[index];
            result[index] = kept with
            {
                Kind = kept.Kind == DeviceKind.Other ? device.Kind : kept.Kind,
                Main = kept.Main ?? device.Main,
                Left = kept.Left ?? device.Left,
                Right = kept.Right ?? device.Right,
                Case = kept.Case ?? device.Case,
            };
        }
        return result.OrderBy(device => device.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>The header's chip shows the device whose battery runs out first.</summary>
    public static WirelessDevice? ChipDevice(IReadOnlyList<WirelessDevice> devices) =>
        devices.Count == 0 ? null : devices.MinBy(device => device.Lowest ?? 101);

    /// <summary>Cards in rows of at most <see cref="RingsPerRow"/> rings, in order (DeviceViews.swift, rows).</summary>
    public static List<List<T>> Rows<T>(IEnumerable<T> cards, Func<T, int> rings)
    {
        var rows = new List<List<T>>();
        var row = new List<T>();
        var count = 0;
        foreach (var card in cards)
        {
            if (row.Count > 0 && count + rings(card) > RingsPerRow)
            {
                rows.Add(row);
                row = [];
                count = 0;
            }
            row.Add(card);
            count += rings(card);
        }
        if (row.Count > 0) rows.Add(row);
        return rows;
    }

    /// <summary>A card's share of its row by ring count, but a lone card doesn't stretch across.</summary>
    public static double CardWidth(double rowWidth, int cardsInRow, int rowRings, int cardRings, double spacing = 8) =>
        Math.Min((rowWidth - (cardsInRow - 1) * spacing) * cardRings / rowRings, cardRings * 120 + 60);

    /// <summary>The rings are bigger when every card fits in one row.</summary>
    public static double RingSize(int rows) => rows == 1 ? 58 : 46;

    public static double RowHeight(int rows) => rows == 1 ? 140 : 112;

    /// <summary>Under a card's ring: the percentage, the name and a second line, with their gaps.</summary>
    private const double CardText = 64;

    /// <summary>
    /// The rows' height and the rings' size in <paramref name="height"/>. The Mac's sizes, unless the
    /// rows would not fit (two rows of 112 overflow the compact layouts' 196, on the Mac too): then
    /// the rows share the height and the rings shrink, never below 28.
    /// </summary>
    public static (double RowHeight, double RingSize) Fit(int rows, double height)
    {
        var row = Math.Min(RowHeight(rows), (height - (rows - 1) * 8) / Math.Max(rows, 1));
        return (row, Math.Max(28, Math.Min(RingSize(rows), row - CardText)));
    }
}

/// <summary>
/// When a read says something worth a banner (DevicesModule.swift, apply): a device that wasn't
/// there at the last read has connected; a low one is said once, until it charges above 30 %.
/// The devices there at launch get no "connected" banner, and a low one only when very low.
/// </summary>
public sealed class DeviceAlerts
{
    private readonly HashSet<string> _warned = [];
    private HashSet<string> _present = [];
    private bool _hasReadOnce;

    public IReadOnlyList<(WirelessDevice Device, bool IsLow)> Read(IReadOnlyList<WirelessDevice> found)
    {
        var alerts = new List<(WirelessDevice, bool)>();
        foreach (var device in found)
        {
            var level = device.Lowest ?? 100;
            var isNew = _hasReadOnce && !_present.Contains(device.Id);
            // One banner per device: a low one says so instead of just "connected".
            if (level <= DeviceRules.LowLevel && !_warned.Contains(device.Id) && (_hasReadOnce || level <= 10))
            {
                _warned.Add(device.Id);
                alerts.Add((device, true));
            }
            else if (isNew)
            {
                alerts.Add((device, false));
            }
            if (level > DeviceRules.RecoveredLevel) _warned.Remove(device.Id);
        }
        _present = found.Select(device => device.Id).ToHashSet();
        _hasReadOnce = true;
        return alerts;
    }
}

/// <summary>
/// AirPods and Beats batteries from Apple's proximity-pairing advertisement, the only place Windows
/// can read each bud and the case: manufacturer data of company 0x004C, message type 0x07. Levels
/// come in tens; 15 means the part isn't there. Which nibble is the left bud depends on which bud
/// is the primary one.
/// </summary>
public sealed record AirPodsReading(int Model, int? Left, int? Right, int? Case, bool LeftCharging, bool RightCharging, bool CaseCharging)
{
    /// <summary><paramref name="data"/> is the manufacturer data after the two bytes of company id.</summary>
    public static AirPodsReading? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 9 || data[0] != 0x07 || data[1] < 7) return null;
        var model = data[3] | data[4] << 8;
        var flipped = (data[5] & 0x20) == 0;
        int high = data[6] >> 4, low = data[6] & 0x0F;
        var charging = data[7] >> 4;
        return new AirPodsReading(
            model,
            Level(flipped ? high : low),
            Level(flipped ? low : high),
            Level(data[7] & 0x0F),
            (charging & (flipped ? 0b010 : 0b001)) != 0,
            (charging & (flipped ? 0b001 : 0b010)) != 0,
            (charging & 0b100) != 0);
    }

    private static int? Level(int value) => value <= 10 ? value * 10 : null;
}

/// <summary>This computer's own battery, for the Devices tab's first card ("Bu Mac" on the Mac).</summary>
public readonly record struct PcBattery(int Percent, bool IsCharging, bool IsPluggedIn);

/// <summary>How long each device banner stays, from the Mac (AppDelegate.swift).</summary>
public static class DeviceAlertDurations
{
    public static readonly TimeSpan Connected = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan Low = TimeSpan.FromSeconds(5);
}
