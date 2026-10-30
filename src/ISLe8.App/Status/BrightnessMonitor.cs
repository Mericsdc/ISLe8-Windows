using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.UI;
using Microsoft.UI.Dispatching;

namespace ISLe8.Status;

/// <summary>
/// The built-in display's brightness (BrightnessMonitor.swift; PORTING.md §3.2). WMI's
/// WmiMonitorBrightnessEvent says when it changes, on one thread that waits for it and does nothing
/// else; a computer with no built-in display has no WmiMonitorBrightness and nothing starts. A
/// laptop with its lid closed has none either until the lid opens, so one with a lid waits for the
/// events all the same. Like
/// the Mac's, it reports every change, the automatic ones too, and the caller decides which deserve
/// a HUD: the brightness keys come in through Raw Input, and <see cref="LastKey"/> says when one was
/// last pressed. The keys are listened to only once a built-in display is found. Once started it
/// only goes quiet when turned off, as the Mac's does.
/// </summary>
internal sealed unsafe partial class BrightnessMonitor : IDisposable
{
    private static readonly Guid LocatorClass = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    private static readonly Guid LocatorId = new("DC12A687-737F-11CF-884D-00AA004B2E24");

    /// <summary>Consumer page: Display Brightness Increment and Decrement.</summary>
    private const ushort ConsumerPage = 0x0C;
    private const ushort BrightnessUp = 0x6F;
    private const ushort BrightnessDown = 0x70;

    private readonly DispatcherQueue _queue;
    private readonly Dictionary<nint, byte[]?> _preparsed = [];
    private Thread? _thread;
    private nint _window;
    private bool _enabled;
    private bool _keysRegistered;
    private volatile bool _disposed;

    public BrightnessMonitor(DispatcherQueue queue) => _queue = queue;

    /// <summary>The level, 0–1, after every change, on the UI thread.</summary>
    public event Action<double>? Changed;

    /// <summary>When a brightness key was last pressed, on any keyboard that sends them as HID consumer keys.</summary>
    public DateTimeOffset? LastKey { get; private set; }

    /// <summary>The window that takes the keys' Raw Input. Set once, before <see cref="SetEnabled"/>.</summary>
    public void AttachWindow(nint window) => _window = window;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled || _thread is not null) return;
        _thread = new Thread(Watch) { IsBackground = true, Name = "ISLe8 brightness", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _enabled = false;
    }

    // MARK: Keys

    private bool RegisterKeys()
    {
        var device = new RawInputDevice { UsagePage = ConsumerPage, Usage = 0x01, Flags = 0x100 /* RIDEV_INPUTSINK */, Target = _window };
        if (RegisterRawInputDevices(&device, 1, (uint)sizeof(RawInputDevice))) return true;
        Log.Info($"brightness keys: RegisterRawInputDevices failed ({Marshal.GetLastPInvokeError()})");
        return false;
    }

    /// <summary>WM_INPUT from a consumer-control collection: notes the time if a brightness key is down.</summary>
    public nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        if (message != 0x00FF /* WM_INPUT */ || !_keysRegistered) return null;
        try
        {
            if (IsBrightnessKey(lParam)) LastKey = DateTimeOffset.Now;
        }
        catch (Exception e)
        {
            Log.Error(e, "brightness key");
        }
        // DefWindowProc still has to see WM_INPUT.
        return null;
    }

    private bool IsBrightnessKey(nint input)
    {
        uint size = 0;
        var header = (uint)sizeof(RawInputHeader);
        if (GetRawInputData(input, 0x10000003 /* RID_INPUT */, null, &size, header) != 0 || size < header + 8 || size > 1024) return false;
        var buffer = stackalloc byte[(int)size];
        if (GetRawInputData(input, 0x10000003, buffer, &size, header) != size) return false;
        var raw = (RawInputHeader*)buffer;
        if (raw->Type != 2 /* RIM_TYPEHID */) return false;
        var reportSize = *(uint*)(buffer + header);
        var count = *(uint*)(buffer + header + 4);
        var reports = buffer + header + 8;
        if (reportSize == 0 || header + 8 + reportSize * count > size) return false;
        if (Preparsed(raw->Device) is not { } preparsed) return false;

        var usages = stackalloc ushort[16];
        fixed (byte* data = preparsed)
        {
            for (var i = 0u; i < count; i++)
            {
                uint found = 16;
                if (HidP_GetUsages(0 /* HidP_Input */, ConsumerPage, 0, usages, &found, data, reports + i * reportSize, reportSize) != 0x00110000) continue;
                for (var j = 0; j < found; j++)
                {
                    if (usages[j] is BrightnessUp or BrightnessDown) return true;
                }
            }
        }
        return false;
    }

    /// <summary>Each device's report layout, asked once.</summary>
    private byte[]? Preparsed(nint device)
    {
        if (_preparsed.TryGetValue(device, out var known)) return known;
        uint size = 0;
        byte[]? data = null;
        if (GetRawInputDeviceInfoW(device, 0x20000005 /* RIDI_PREPARSEDDATA */, null, &size) == 0 && size > 0)
        {
            data = new byte[size];
            fixed (byte* pointer = data)
            {
                if (GetRawInputDeviceInfoW(device, 0x20000005, pointer, &size) != size) data = null;
            }
        }
        _preparsed[device] = data;
        return data;
    }

    // MARK: WMI, on its own thread

    private void Watch()
    {
        if (CoInitializeEx(0, 0 /* COINIT_MULTITHREADED */) < 0) return;
        nint services = 0;
        try
        {
            services = Connect();
            if (services == 0) return;
            var level = Current(services);
            if (level is null && !HasLid()) return;
            Log.Info(level is null ? "brightness: waiting for the built-in display (lid closed)" : "brightness: following the built-in display");
            _queue.Post(() =>
            {
                if (!_keysRegistered && _window != 0 && !_disposed) _keysRegistered = RegisterKeys();
            });
            if (level is { } now) Post(now);
            Follow(services);
        }
        catch (Exception e)
        {
            Log.Error(e, "brightness");
        }
        finally
        {
            if (services != 0) Marshal.Release(services);
        }
    }

    /// <summary>ROOT\WMI, with the identity WMI needs to answer.</summary>
    private static nint Connect()
    {
        var clsid = LocatorClass;
        var iid = LocatorId;
        nint locator;
        if (CoCreateInstance(&clsid, 0, 1 /* CLSCTX_INPROC_SERVER */, &iid, &locator) < 0) return 0;
        var path = SysAllocString("ROOT\\WMI");
        try
        {
            nint services;
            var connect = (delegate* unmanaged[Stdcall]<nint, nint, nint, nint, nint, int, nint, nint, nint*, int>)Slot(locator, 3);
            if (connect(locator, path, 0, 0, 0, 0, 0, 0, &services) < 0) return 0;
            CoSetProxyBlanket(services, 10 /* RPC_C_AUTHN_WINNT */, 0, 0, 3 /* RPC_C_AUTHN_LEVEL_CALL */, 3 /* RPC_C_IMP_LEVEL_IMPERSONATE */, 0, 0);
            return services;
        }
        finally
        {
            SysFreeString(path);
            Marshal.Release(locator);
        }
    }

    /// <summary>The built-in display's level now, 0–1; null without one (a desktop: "not supported").</summary>
    private static double? Current(nint services)
    {
        var results = Query(services, 20 /* ExecQuery */, "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active = TRUE");
        if (results == 0) return null;
        try
        {
            return Next(results, 5000, "CurrentBrightness");
        }
        finally
        {
            Marshal.Release(results);
        }
    }

    /// <summary>Waits for each change, for as long as ISLe8 runs. No timeout: nothing wakes this thread but a change.</summary>
    private void Follow(nint services)
    {
        var events = Query(services, 22 /* ExecNotificationQuery */, "SELECT Brightness FROM WmiMonitorBrightnessEvent");
        if (events == 0) return;
        try
        {
            while (!_disposed && Next(events, -1 /* WBEM_INFINITE */, "Brightness") is { } level)
            {
                if (_enabled) Post(level);
            }
        }
        finally
        {
            Marshal.Release(events);
        }
    }

    private void Post(double level) => _queue.Post(() =>
    {
        if (_enabled) Changed?.Invoke(level);
    });

    /// <summary>A WQL query, forward-only and semisynchronous: no sink of ISLe8's for WMI to call into.</summary>
    private static nint Query(nint services, int slot, string wql)
    {
        var language = SysAllocString("WQL");
        var query = SysAllocString(wql);
        try
        {
            nint results;
            var exec = (delegate* unmanaged[Stdcall]<nint, nint, nint, int, nint, nint*, int>)Slot(services, slot);
            if (exec(services, language, query, 0x30 /* WBEM_FLAG_RETURN_IMMEDIATELY | WBEM_FLAG_FORWARD_ONLY */, 0, &results) < 0) return 0;
            // The enumerator is a proxy of its own; without the same identity Next can be refused.
            CoSetProxyBlanket(results, 10, 0, 0, 3, 3, 0, 0);
            return results;
        }
        finally
        {
            SysFreeString(query);
            SysFreeString(language);
        }
    }

    /// <summary>The next object's byte-sized percentage, 0–1; null when there are no more or it can't be read.</summary>
    private static double? Next(nint enumerator, int timeout, string property)
    {
        nint item;
        uint returned;
        var next = (delegate* unmanaged[Stdcall]<nint, int, uint, nint*, uint*, int>)Slot(enumerator, 4);
        if (next(enumerator, timeout, 1, &item, &returned) < 0 || returned == 0) return null;
        try
        {
            var value = default(Variant);
            var get = (delegate* unmanaged[Stdcall]<nint, char*, int, Variant*, int*, int*, int>)Slot(item, 4);
            fixed (char* name = property)
            {
                if (get(item, name, 0, &value, null, null) < 0) return null;
            }
            try
            {
                // uint8 comes as VT_UI1; some providers hand it over as VT_I4.
                double? percent = value.Type switch
                {
                    17 /* VT_UI1 */ => value.Byte,
                    3 /* VT_I4 */ => value.Int,
                    _ => null,
                };
                return percent is { } p ? Math.Clamp(p / 100, 0, 1) : null;
            }
            finally
            {
                VariantClear(&value);
            }
        }
        finally
        {
            Marshal.Release(item);
        }
    }

    private static nint Slot(nint instance, int index) => (*(nint**)instance)[index];

    /// <summary>A laptop or tablet: SYSTEM_POWER_CAPABILITIES.LidPresent, the third byte.</summary>
    private static bool HasLid()
    {
        var capabilities = stackalloc byte[128];
        return GetPwrCapabilities(capabilities) && capabilities[2] != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint WParam;
    }

    /// <summary>VARIANT: the type, three reserved words, then the value (24 bytes on 64-bit Windows).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public byte Byte;
        [FieldOffset(8)] public int Int;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterRawInputDevices(RawInputDevice* devices, uint count, uint size);

    [LibraryImport("user32.dll")]
    private static partial uint GetRawInputData(nint input, uint command, void* data, uint* size, uint headerSize);

    [LibraryImport("user32.dll")]
    private static partial uint GetRawInputDeviceInfoW(nint device, uint command, void* data, uint* size);

    [LibraryImport("hid.dll")]
    private static partial int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, ushort* usages, uint* usageLength, void* preparsed, byte* report, uint reportLength);

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool GetPwrCapabilities(byte* capabilities);

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint model);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* result);

    [LibraryImport("ole32.dll")]
    private static partial int CoSetProxyBlanket(nint proxy, uint authentication, uint authorization, nint principal, uint level, uint impersonation, nint identity, uint capabilities);

    [LibraryImport("oleaut32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SysAllocString(string text);

    [LibraryImport("oleaut32.dll")]
    private static partial void SysFreeString(nint text);

    [LibraryImport("oleaut32.dll")]
    private static partial int VariantClear(Variant* value);
}
