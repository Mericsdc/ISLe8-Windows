using System.Runtime.InteropServices;
using ISLe8.Core.Devices;
using ISLe8.Core.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace ISLe8.Devices;

/// <summary>
/// Batteries of devices on their own USB receivers, which Windows knows nothing about: Razer mice and
/// keyboards, SteelSeries Arctis headsets, ASUS ROG keyboards and mice on the ROG Omni Receiver. Each
/// is asked in its maker's protocol (<see cref="RazerProtocol"/>, <see cref="SteelSeriesProtocol"/>,
/// <see cref="AsusProtocol"/>), the way Synapse, GG and Armoury Crate ask; only questions, never a setting.
/// Runs off the UI thread, a few milliseconds per device.
/// </summary>
internal static unsafe partial class HidBatteries
{
    /// <summary>A battery read from a receiver.</summary>
    public sealed record Reading(string Id, string Name, DeviceKind Kind, int Level, bool IsCharging, int? Spare);

    /// <summary>The HID interfaces worth watching for arrivals: these makers' receivers.</summary>
    public const string WatchSelector =
        "System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True"
        + " AND (System.DeviceInterface.Hid.VendorId:=5426 OR System.DeviceInterface.Hid.VendorId:=4152"
        + " OR (System.DeviceInterface.Hid.VendorId:=2821 AND System.DeviceInterface.Hid.ProductId:=6862))";

    public static List<Reading> Read()
    {
        var readings = new List<Reading>();
        var paths = InterfacePaths();
        try
        {
            ReadAsus(paths, readings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Log.Error(e, "hid battery");
        }
        foreach (var path in paths)
        {
            var lower = path.ToLowerInvariant();
            try
            {
                if (lower.Contains("vid_1532")) ReadRazer(path, readings);
                else if (lower.Contains("vid_1038")) ReadSteelSeries(path, lower, readings);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                Log.Error(e, "hid battery");
            }
        }
        // A receiver can answer on more than one of its collections; one card per device.
        return readings.DistinctBy(reading => reading.Name).ToList();
    }

    // MARK: Razer

    private static void ReadRazer(string path, List<Reading> readings)
    {
        // Mice and keyboards can't be opened for reading or writing, but feature reports pass with no access.
        using var handle = Open(path, access: 0, overlapped: false);
        if (handle.IsInvalid || Caps(handle) is not { } caps || caps.FeatureReportByteLength != RazerProtocol.ReportLength) return;
        foreach (var transaction in RazerProtocol.TransactionIds)
        {
            if (Ask(handle, transaction, RazerProtocol.BatteryCommand) is not { } level) continue;
            var charging = Ask(handle, transaction, RazerProtocol.ChargingCommand) == 1;
            var name = Product(handle) ?? "Razer";
            var kind = caps.UsagePage == 1 && caps.Usage == 2 ? DeviceKind.Mouse
                : caps.UsagePage == 1 && caps.Usage == 6 ? DeviceKind.Keyboard
                : DeviceRules.Kind(name, null, default);
            readings.Add(new Reading(Id(path), name, kind, RazerProtocol.Percent(level), charging, null));
            return;
        }
    }

    /// <summary>One question; the device may be busy with Synapse for a moment, so it is asked again.</summary>
    private static byte? Ask(SafeFileHandle handle, byte transaction, byte command)
    {
        var request = RazerProtocol.Request(transaction, RazerProtocol.PowerClass, command);
        fixed (byte* buffer = request)
        {
            if (!HidD_SetFeature(handle, buffer, request.Length)) return null;
        }
        var answer = new byte[RazerProtocol.ReportLength];
        for (var attempt = 0; attempt < 6; attempt++)
        {
            Thread.Sleep(25);
            fixed (byte* buffer = answer)
            {
                if (!HidD_GetFeature(handle, buffer, answer.Length)) return null;
            }
            if (RazerProtocol.StatusOf(answer) != RazerProtocol.Status.Busy) break;
        }
        return RazerProtocol.Argument(answer, command);
    }

    // MARK: SteelSeries

    private static void ReadSteelSeries(string path, string lower, List<Reading> readings)
    {
        var product = SteelSeriesProtocol.NovaPro.Keys.FirstOrDefault(pid => lower.Contains($"pid_{pid:x4}"));
        if (product == 0) return;
        using var handle = Open(path, access: 0xC0000000, overlapped: true);
        if (handle.IsInvalid || Caps(handle) is not { } caps || caps.UsagePage != SteelSeriesProtocol.UsagePage || OutputReportId(handle) is not { } reportId) return;
        using var stream = new FileStream(handle, FileAccess.ReadWrite, 0, isAsync: true);
        stream.Write(SteelSeriesProtocol.Request(reportId, caps.OutputReportByteLength));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        var answer = new byte[Math.Max((int)caps.InputReportByteLength, 16)];
        while (!timeout.IsCancellationRequested)
        {
            int count;
            try
            {
                count = stream.ReadAsync(answer, timeout.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (count == 0) return;
            if (answer[1] != SteelSeriesProtocol.StatusCommand) continue;
            if (SteelSeriesProtocol.Parse(answer) is { } reading)
            {
                readings.Add(new Reading(Id(path), Product(handle) ?? SteelSeriesProtocol.NovaPro[product], DeviceKind.Headphones, reading.Headset, reading.IsCharging, reading.Spare));
            }
            return;
        }
    }

    // MARK: ASUS ROG Omni Receiver

    /// <summary>
    /// Each receiver says what is paired with it (col01); a keyboard it lists is asked on col02, a
    /// mouse on col03. The three collections of one receiver share its instance in their paths.
    /// </summary>
    private static void ReadAsus(List<string> paths, List<Reading> readings)
    {
        var receivers = paths
            .Where(path => path.Contains("vid_0b05&pid_1ace&mi_02&col0", StringComparison.OrdinalIgnoreCase))
            .GroupBy(OmniInstance, StringComparer.OrdinalIgnoreCase);
        foreach (var receiver in receivers)
        {
            string? Collection(int number) => receiver.FirstOrDefault(path => path.Contains($"&col0{number}", StringComparison.OrdinalIgnoreCase));
            if (Collection(1) is not { } list
                || Exchange(list, AsusProtocol.PairListRequest(), answer => AsusProtocol.IsPairList(answer)) is not { } pairs)
            {
                continue;
            }
            foreach (var pid in AsusProtocol.Paired(pairs))
            {
                var keyboard = AsusProtocol.Keyboards.TryGetValue(pid, out var keyboardName);
                var mouse = AsusProtocol.Mice.TryGetValue(pid, out var mouseName);
                if (!keyboard && !mouse || Collection(keyboard ? 2 : 3) is not { } channel) continue;
                var answer = keyboard
                    ? Exchange(channel, AsusProtocol.KeyboardBatteryRequest(), answer => AsusProtocol.IsKeyboardBattery(answer))
                    : Exchange(channel, AsusProtocol.MouseBatteryRequest(), answer => AsusProtocol.IsMouseBattery(answer));
                if (answer is null || (keyboard ? AsusProtocol.KeyboardBattery(answer) : AsusProtocol.MouseBattery(answer)) is not { } reading) continue;
                readings.Add(new Reading($"{Id(channel)}#{pid:x4}", keyboard ? keyboardName! : mouseName!,
                    keyboard ? DeviceKind.Keyboard : DeviceKind.Mouse, reading.Level, reading.IsCharging, null));
            }
        }
    }

    /// <summary>The receiver's instance, the part of the path after the collection: "a&amp;2c91b242&amp;0".</summary>
    private static string OmniInstance(string path)
    {
        var parts = path.Split('#');
        if (parts.Length < 3) return path;
        var cut = parts[2].LastIndexOf('&');
        return cut > 0 ? parts[2][..cut] : parts[2];
    }

    /// <summary>
    /// Writes one output report and reads input reports until one is the answer, for at most 600 ms;
    /// Armoury Crate may be talking to the same collection, so other reports are skipped, and a
    /// question lost among its traffic is asked once more.
    /// </summary>
    private static byte[]? Exchange(string path, byte[] request, Func<byte[], bool> isAnswer) =>
        ExchangeOnce(path, request, isAnswer) ?? ExchangeOnce(path, request, isAnswer);

    private static byte[]? ExchangeOnce(string path, byte[] request, Func<byte[], bool> isAnswer)
    {
        using var handle = Open(path, access: 0xC0000000, overlapped: true);
        if (handle.IsInvalid || Caps(handle) is not { } caps || caps.OutputReportByteLength != request.Length) return null;
        using var stream = new FileStream(handle, FileAccess.ReadWrite, 0, isAsync: true);
        stream.Write(request);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        var answer = new byte[Math.Max((int)caps.InputReportByteLength, request.Length)];
        while (!timeout.IsCancellationRequested)
        {
            int count;
            try
            {
                count = stream.ReadAsync(answer, timeout.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            if (count == 0) return null;
            if (isAnswer(answer)) return answer;
        }
        return null;
    }

    // MARK: HID

    /// <summary>The device-specific middle of the interface path: the same for a device across starts.</summary>
    private static string Id(string path)
    {
        var parts = path.Split('#');
        return parts.Length >= 3 ? $"{parts[1]}#{parts[2]}".ToLowerInvariant() : path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        public fixed ushort Reserved[17];
        public fixed ushort Counts[10];
    }

    [StructLayout(LayoutKind.Explicit, Size = 72)]
    private struct HIDP_VALUE_CAPS
    {
        [FieldOffset(2)] public byte ReportID;
    }

    private static HIDP_CAPS? Caps(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var data)) return null;
        try
        {
            return HidP_GetCaps(data, out var caps) == HidpStatusSuccess ? caps : null;
        }
        finally
        {
            HidD_FreePreparsedData(data);
        }
    }

    private static byte? OutputReportId(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var data)) return null;
        try
        {
            ushort count = 8;
            var caps = stackalloc HIDP_VALUE_CAPS[8];
            return HidP_GetValueCaps(1 /* HidP_Output */, caps, ref count, data) == HidpStatusSuccess && count > 0 ? caps[0].ReportID : null;
        }
        finally
        {
            HidD_FreePreparsedData(data);
        }
    }

    private static string? Product(SafeFileHandle handle)
    {
        var buffer = stackalloc char[128];
        return HidD_GetProductString(handle, buffer, 256) && new string(buffer) is { Length: > 0 } name ? name.Trim() : null;
    }

    private static SafeFileHandle Open(string path, uint access, bool overlapped) =>
        CreateFileW(path, access, 3 /* read | write sharing */, 0, 3 /* OPEN_EXISTING */, overlapped ? 0x40000000u : 0, 0);

    /// <summary>Every present HID interface of these makers' receivers.</summary>
    private static List<string> InterfacePaths()
    {
        var paths = new List<string>();
        HidD_GetHidGuid(out var hid);
        var set = SetupDiGetClassDevsW(&hid, null, 0, 0x12 /* DIGCF_PRESENT | DIGCF_DEVICEINTERFACE */);
        if (set == -1) return paths;
        try
        {
            var data = new SP_DEVICE_INTERFACE_DATA { cbSize = sizeof(SP_DEVICE_INTERFACE_DATA) };
            var buffer = stackalloc byte[2048];
            for (uint index = 0; SetupDiEnumDeviceInterfaces(set, 0, &hid, index, ref data); index++)
            {
                *(int*)buffer = sizeof(nint) == 8 ? 8 : 6;
                if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, 2048, out _, 0)) continue;
                var path = new string((char*)(buffer + 4));
                var lower = path.ToLowerInvariant();
                if (lower.Contains("vid_1532") || lower.Contains("vid_1038") || lower.Contains("vid_0b05&pid_1ace")) paths.Add(path);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return paths;
    }

    private const int HidpStatusSuccess = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [LibraryImport("hid.dll")]
    private static partial void HidD_GetHidGuid(out Guid guid);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetPreparsedData(SafeFileHandle handle, out nint data);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_FreePreparsedData(nint data);

    [LibraryImport("hid.dll")]
    private static partial int HidP_GetCaps(nint data, out HIDP_CAPS caps);

    [LibraryImport("hid.dll")]
    private static partial int HidP_GetValueCaps(int type, HIDP_VALUE_CAPS* caps, ref ushort length, nint data);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_SetFeature(SafeFileHandle handle, byte* buffer, int length);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetFeature(SafeFileHandle handle, byte* buffer, int length);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetProductString(SafeFileHandle handle, char* buffer, int length);

    [LibraryImport("setupapi.dll")]
    private static partial nint SetupDiGetClassDevsW(Guid* guid, char* enumerator, nint parent, uint flags);

    [LibraryImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiEnumDeviceInterfaces(nint set, nint info, Guid* guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);

    [LibraryImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiGetDeviceInterfaceDetailW(nint set, ref SP_DEVICE_INTERFACE_DATA data, byte* detail, int size, out int required, nint info);

    [LibraryImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetupDiDestroyDeviceInfoList(nint set);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
}
