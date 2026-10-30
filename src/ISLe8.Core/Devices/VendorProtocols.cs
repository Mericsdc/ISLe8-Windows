namespace ISLe8.Core.Devices;

/// <summary>
/// Razer's control protocol, the one Synapse and OpenRazer speak: a 90-byte packet in feature report 0
/// of the device's first interface. Only questions are asked here (the battery, whether it charges);
/// nothing is ever set. Devices that are wired, or a receiver whose mouse is asleep, don't answer them.
/// </summary>
public static class RazerProtocol
{
    public const int Vendor = 0x1532;

    /// <summary>The report with its id: 1 + 90 bytes.</summary>
    public const int ReportLength = 91;

    /// <summary>Command class 0x07 (power): 0x80 the battery level, 0x84 the charging state.</summary>
    public const byte PowerClass = 0x07;
    public const byte BatteryCommand = 0x80;
    public const byte ChargingCommand = 0x84;

    /// <summary>Newer wireless devices answer to 0x1F, some to 0x3F, older ones to 0xFF.</summary>
    public static readonly byte[] TransactionIds = [0x1F, 0x3F, 0xFF];

    public enum Status : byte
    {
        New = 0x00,
        Busy = 0x01,
        Successful = 0x02,
        Failure = 0x03,
        Timeout = 0x04,
        NotSupported = 0x05,
    }

    public static byte[] Request(byte transaction, byte commandClass, byte command)
    {
        var report = new byte[ReportLength];
        // [0] report id, then: status, transaction, remaining packets (2), protocol, data size, class, command, 80 arguments, crc, reserved.
        report[2] = transaction;
        report[6] = 0x02;
        report[7] = commandClass;
        report[8] = command;
        report[89] = Crc(report);
        return report;
    }

    /// <summary>XOR of the packet from "remaining packets" to the last argument.</summary>
    public static byte Crc(ReadOnlySpan<byte> report)
    {
        byte crc = 0;
        for (var i = 3; i < 89; i++) crc ^= report[i];
        return crc;
    }

    public static Status StatusOf(ReadOnlySpan<byte> answer) => answer.Length >= ReportLength ? (Status)answer[1] : Status.Failure;

    /// <summary>The second argument of a successful answer to <paramref name="command"/>; null otherwise.</summary>
    public static byte? Argument(ReadOnlySpan<byte> answer, byte command) =>
        StatusOf(answer) == Status.Successful && answer[7] == PowerClass && answer[8] == command ? answer[10] : null;

    /// <summary>The battery's 0–255 as a percentage.</summary>
    public static int Percent(byte level) => (int)Math.Round(level * 100 / 255.0);
}

/// <summary>
/// SteelSeries' Arctis Nova Pro family: a status question (0xB0) in its vendor collection, and the
/// answer with the headset's battery and the spare one charging in the base station. Read from an
/// Arctis Nova Pro Omni (1038:2290), which gives percentages.
/// </summary>
public static class SteelSeriesProtocol
{
    public const int Vendor = 0x1038;

    /// <summary>The vendor collection that answers (usage page 0xFFC0).</summary>
    public const ushort UsagePage = 0xFFC0;

    public const byte StatusCommand = 0xB0;

    /// <summary>Models read this way, by product id.</summary>
    public static readonly IReadOnlyDictionary<int, string> NovaPro = new Dictionary<int, string>
    {
        [0x2290] = "Arctis Nova Pro Omni",
    };

    public sealed record Reading(int Headset, int? Spare, bool IsCharging);

    /// <summary>The 64-byte question: report id, then the command.</summary>
    public static byte[] Request(byte reportId, int length = 64)
    {
        var report = new byte[length];
        report[0] = reportId;
        report[1] = StatusCommand;
        return report;
    }

    /// <summary>
    /// [1] echoes the command, [6] the headset's battery, [7] the spare's, [15] the headset's state:
    /// 0x01 off, 0x02 charging over the cable, 0x08 connected. Null when the headset is off or the
    /// answer is something else.
    /// </summary>
    public static Reading? Parse(ReadOnlySpan<byte> answer)
    {
        if (answer.Length < 16 || answer[1] != StatusCommand) return null;
        var state = answer[15];
        if (state is not (0x02 or 0x08)) return null;
        return new Reading(Math.Clamp((int)answer[6], 0, 100), answer[7] <= 100 ? answer[7] : null, state == 0x02);
    }
}

/// <summary>
/// ASUS ROG keyboards and mice on the ROG Omni Receiver (0B05:1ACE), asked as Armoury Crate asks
/// (the protocol as G-Helper documents it, github.com/seerge/g-helper, GPL-3.0; only the facts are
/// used here). The receiver has three vendor collections on its interface 2, each with 64-byte
/// reports: col01 (report 1) lists what is paired, col02 (report 2) talks to a keyboard, col03
/// (report 3) to a mouse. Read from a ROG Azoth: the pair list names 0x1A85, the keyboard answers
/// 46 % and charging.
/// </summary>
public static class AsusProtocol
{
    public const int Vendor = 0x0B05;
    public const int OmniReceiver = 0x1ACE;
    public const int ReportLength = 64;

    public const byte PairListReport = 0x01;
    public const byte KeyboardReport = 0x02;
    public const byte MouseReport = 0x03;

    /// <summary>Keyboards by the product id the receiver lists them under.</summary>
    public static readonly IReadOnlyDictionary<int, string> Keyboards = new Dictionary<int, string>
    {
        [0x1A85] = "ROG Azoth",
        [0x1B42] = "ROG Azoth Extreme",
        [0x1CF1] = "ROG Azoth Extreme SE",
        [0x1AB0] = "ROG Strix Scope II 96 Wireless",
        [0x1B7A] = "ROG Strix Scope II 96 RX Wireless",
        [0x1B06] = "ROG Falchion RX Low Profile",
    };

    public static readonly IReadOnlyDictionary<int, string> Mice = new Dictionary<int, string>
    {
        [0x1B65] = "ROG Harpe Ace Mini",
        [0x1C0E] = "ROG Keris II Origin",
        [0x1D4E] = "ROG Keris II Origin",
        [0x1A94] = "ROG Harpe Ace Aim Lab Edition",
        [0x1AD7] = "ROG Strix Impact III Wireless",
        [0x1A72] = "ROG Gladius III Aimpoint",
        [0x1A68] = "ROG Keris Wireless Aimpoint",
        [0x1A6A] = "ROG Keris Wireless Aimpoint",
        [0x1B1A] = "ROG Keris II Ace",
        [0x1B18] = "ROG Keris II Ace",
        [0x1B68] = "ROG Harpe Ace Extreme",
        [0x1B69] = "ROG Harpe Ace Extreme",
    };

    public sealed record Reading(int Level, bool IsCharging);

    /// <summary>A 64-byte report: its id, then the command's bytes, the rest zero.</summary>
    public static byte[] Request(byte reportId, params ReadOnlySpan<byte> command)
    {
        var report = new byte[ReportLength];
        report[0] = reportId;
        command.CopyTo(report.AsSpan(1));
        return report;
    }

    /// <summary>What is paired: 0xA0.</summary>
    public static byte[] PairListRequest() => Request(PairListReport, 0xA0, 0x00, 0x00);

    /// <summary>A keyboard's power state: 0x12 0x01.</summary>
    public static byte[] KeyboardBatteryRequest() => Request(KeyboardReport, 0x12, 0x01);

    /// <summary>A mouse's: 0x12 0x07.</summary>
    public static byte[] MouseBatteryRequest() => Request(MouseReport, 0x12, 0x07);

    public static bool IsPairList(ReadOnlySpan<byte> answer) => answer.Length >= 9 && answer[0] == PairListReport && answer[1] == 0xA0;

    /// <summary>The paired product ids: little-endian, in four-byte slots from byte 5, until an empty one.</summary>
    public static List<int> Paired(ReadOnlySpan<byte> answer)
    {
        var pids = new List<int>();
        if (!IsPairList(answer)) return pids;
        for (var offset = 5; offset + 1 < answer.Length; offset += 4)
        {
            var pid = answer[offset] | answer[offset + 1] << 8;
            if (pid == 0) break;
            pids.Add(pid);
        }
        return pids;
    }

    public static bool IsKeyboardBattery(ReadOnlySpan<byte> answer) => answer.Length >= 10 && answer[0] == KeyboardReport && answer[1] == 0x12 && answer[2] == 0x01;

    public static bool IsMouseBattery(ReadOnlySpan<byte> answer) => answer.Length >= 11 && answer[0] == MouseReport && answer[1] == 0x12 && answer[2] == 0x07;

    /// <summary>
    /// A keyboard's answer: [6] the battery, [9] 1 while charging. A device that has gone to sleep
    /// answers 0 % and not charging: no reading, as Armoury Crate shows none.
    /// </summary>
    public static Reading? KeyboardBattery(ReadOnlySpan<byte> answer) =>
        IsKeyboardBattery(answer) ? Make(answer[6], answer[9] == 1) : null;

    /// <summary>A mouse's: [5] the battery, [10] non-zero while charging.</summary>
    public static Reading? MouseBattery(ReadOnlySpan<byte> answer) =>
        IsMouseBattery(answer) ? Make(answer[5], answer[10] > 0) : null;

    private static Reading? Make(byte level, bool charging) =>
        level > 100 || level == 0 && !charging ? null : new Reading(level, charging);
}
