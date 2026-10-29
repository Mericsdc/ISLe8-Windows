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
