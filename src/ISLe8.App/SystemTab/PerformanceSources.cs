using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.SystemInfo;

namespace ISLe8.SystemTab;

/// <summary>Windows' "GPU Engine(*engtype_3D)\Utilization Percentage", by its English name so it works in every language.</summary>
internal sealed unsafe partial class GpuCounters : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);
    private nint _query;
    private nint _counter;

    public GpuCounters()
    {
        if (PdhOpenQueryW(null, 0, out _query) != 0) return;
        if (PdhAddEnglishCounterW(_query, @"\GPU Engine(*engtype_3D)\Utilization Percentage", 0, out _counter) != 0) _counter = 0;
        else PdhCollectQueryData(_query);
    }

    /// <summary>Since the last read; null on the first or when Windows has no such counter.</summary>
    public GpuUse? Read()
    {
        if (_counter == 0 || PdhCollectQueryData(_query) != 0) return null;
        uint size = 0, count = 0;
        if (PdhGetFormattedCounterArrayW(_counter, PDH_FMT_DOUBLE, ref size, ref count, null) != PDH_MORE_DATA || size == 0) return null;
        var buffer = new byte[size];
        fixed (byte* items = buffer)
        {
            if (PdhGetFormattedCounterArrayW(_counter, PDH_FMT_DOUBLE, ref size, ref count, items) != 0) return null;
            var engines = new List<(string, double)>((int)count);
            for (var i = 0; i < count; i++)
            {
                var item = (CounterItem*)items + i;
                if (item->Status is 0 or 1) engines.Add((new string(item->Name), item->Value));
            }
            return PerformanceMath.Gpu(engines);
        }
    }

    public void Dispose()
    {
        if (_query != 0) PdhCloseQuery(_query);
        _query = 0;
    }

    /// <summary>PDH_FMT_COUNTERVALUE_ITEM_W with a double.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public char* Name;
        public uint Status;
        public double Value;
    }

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PdhOpenQueryW(string? source, nint user, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PdhAddEnglishCounterW(nint query, string path, nint user, out nint counter);

    [LibraryImport("pdh.dll")]
    private static partial int PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    private static partial int PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, ref uint count, byte* items);

    [LibraryImport("pdh.dll")]
    private static partial int PdhCloseQuery(nint query);
}

/// <summary>
/// GPU temperatures as their drivers report them to Windows (D3DKMTQueryAdapterInfo,
/// KMTQAITYPE_ADAPTERPERFDATA, in tenths of a degree): what Task Manager shows, for any maker, WDDM 2.4 on.
/// The GPU's is the busiest adapter's. A laptop's integrated GPU shares the processor's die, so its
/// reading stands in for the CPU's when no sensor program runs.
/// </summary>
internal static unsafe partial class GpuTemperature
{
    private const int KMTQAITYPE_ADAPTERTYPE = 15;
    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;
    private const uint HybridIntegrated = 0x20;

    /// <param name="busiest">The busiest adapter's LUID; without one, the hottest adapter counts.</param>
    public static (double? Gpu, double? Integrated) Read(long? busiest)
    {
        var enumerate = new EnumAdapters2();
        if (D3DKMTEnumAdapters2(&enumerate) != 0 || enumerate.Count == 0) return (null, null);
        var adapters = new AdapterInfo[enumerate.Count];
        double? gpu = null, hottest = null, integrated = null;
        fixed (AdapterInfo* list = adapters)
        {
            enumerate.Adapters = list;
            if (D3DKMTEnumAdapters2(&enumerate) != 0) return (null, null);
            for (var i = 0; i < enumerate.Count; i++)
            {
                var data = new PerfData();
                var query = new QueryAdapterInfo { Adapter = list[i].Adapter, Type = KMTQAITYPE_ADAPTERPERFDATA, Data = &data, Size = (uint)sizeof(PerfData) };
                if (D3DKMTQueryAdapterInfo(&query) == 0 && data.Temperature > 0)
                {
                    var celsius = data.Temperature / 10.0;
                    var luid = (long)list[i].LuidHigh << 32 | list[i].LuidLow;
                    if (luid == busiest) gpu = celsius;
                    if (hottest is null || celsius > hottest) hottest = celsius;
                    uint type = 0;
                    var typeQuery = new QueryAdapterInfo { Adapter = list[i].Adapter, Type = KMTQAITYPE_ADAPTERTYPE, Data = &type, Size = sizeof(uint) };
                    if (D3DKMTQueryAdapterInfo(&typeQuery) == 0 && (type & HybridIntegrated) != 0) integrated = celsius;
                }
                var close = list[i].Adapter;
                D3DKMTCloseAdapter(&close);
            }
        }
        return (busiest is null ? hottest : gpu, integrated);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EnumAdapters2
    {
        public uint Count;
        public AdapterInfo* Adapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterInfo
    {
        public uint Adapter;
        public uint LuidLow;
        public int LuidHigh;
        public uint Sources;
        public int PrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public void* Data;
        public uint Size;
    }

    /// <summary>D3DKMT_ADAPTER_PERFDATA.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PerfData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PcieBandwidth;
        public uint FanRpm;
        public uint Power;
        public uint Temperature;
        public byte PowerStateOverride;
    }

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTEnumAdapters2(EnumAdapters2* adapters);

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTQueryAdapterInfo(QueryAdapterInfo* query);

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTCloseAdapter(uint* adapter);
}


/// <summary>
/// Frames a second for one process, the way PresentMon counts them: an ETW session of ISLe8's own hears
/// every Present the program makes through DXGI (Direct3D 10–12, and Vulkan or OpenGL presenting
/// through it) or Direct3D 9. Only those events, nothing they carry. Windows lets a session start only
/// for an administrator or a member of "Performance Log Users"; otherwise there are no frames to show.
/// </summary>
internal sealed unsafe partial class FrameCounter : IDisposable
{
    private const string SessionName = "ISLe8 frames";
    private static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const ushort DxgiPresentStart = 42;
    private const ushort D3D9PresentStart = 1;

    private const int PropertiesSize = 120;
    private const int NameBytes = 128;
    private const ulong InvalidTrace = ulong.MaxValue;

    private static FrameCounter? s_current;
    private readonly nint _properties;
    private ulong _session;
    private ulong _trace = InvalidTrace;
    private Thread? _reader;
    private int _frames;
    private long _countingSince = Environment.TickCount64;

    /// <summary>Whose frames count; 0 for none.</summary>
    public uint Process { get; set; }

    public FrameCounter()
    {
        _properties = Marshal.AllocHGlobal(PropertiesSize + NameBytes);
        if (!Start()) return;
        s_current = this;
        var logfile = stackalloc byte[LogfileSize];
        new Span<byte>(logfile, LogfileSize).Clear();
        fixed (char* name = SessionName)
        {
            *(char**)(logfile + 8) = name;
            *(uint*)(logfile + 28) = 0x00000100 | 0x10000000; // PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD
            *(delegate* unmanaged<EventRecord*, void>*)(logfile + 424) = &OnEvent;
            _trace = OpenTraceW(logfile);
        }
        if (_trace == InvalidTrace)
        {
            Log.Info($"frames: no trace ({Marshal.GetLastPInvokeError()})");
            Stop();
            return;
        }
        _reader = new Thread(Read) { IsBackground = true, Name = "ISLe8 frames" };
        _reader.Start();
    }

    /// <summary>EVENT_TRACE_LOGFILEW on 64-bit Windows.</summary>
    private const int LogfileSize = 448;

    /// <summary>Frames a second since the last call.</summary>
    public int? TakeFps()
    {
        var now = Environment.TickCount64;
        var frames = Interlocked.Exchange(ref _frames, 0);
        var seconds = (now - Interlocked.Exchange(ref _countingSince, now)) / 1000.0;
        return _trace == InvalidTrace || Process == 0 ? null : PerformanceMath.Fps(frames, seconds);
    }

    private bool Start()
    {
        var properties = (byte*)_properties;
        // A session a crashed ISLe8 left behind is stopped first.
        Prepare(properties);
        ControlTraceW(0, SessionName, properties, 1 /* EVENT_TRACE_CONTROL_STOP */);
        Prepare(properties);
        ulong session;
        var result = StartTraceW(&session, SessionName, properties);
        if (result != 0)
        {
            Log.Info($"frames: no session ({result}; 5 means ISLe8 isn't allowed to trace)");
            return false;
        }
        _session = session;
        foreach (var provider in new[] { Dxgi, D3D9 })
        {
            var guid = provider;
            EnableTraceEx2(_session, &guid, 1 /* EVENT_CONTROL_CODE_ENABLE_PROVIDER */, 4 /* TRACE_LEVEL_INFORMATION */, 0, 0, 0, null);
        }
        return true;
    }

    private static void Prepare(byte* properties)
    {
        new Span<byte>(properties, PropertiesSize + NameBytes).Clear();
        *(uint*)properties = PropertiesSize + NameBytes;   // Wnode.BufferSize
        *(uint*)(properties + 40) = 1;                     // Wnode.ClientContext: QueryPerformanceCounter
        *(uint*)(properties + 44) = 0x00020000;            // Wnode.Flags: WNODE_FLAG_TRACED_GUID
        *(uint*)(properties + 64) = 0x00000100;            // LogFileMode: EVENT_TRACE_REAL_TIME_MODE
        *(uint*)(properties + 68) = 1;                     // FlushTimer: a second
        *(uint*)(properties + 116) = PropertiesSize;       // LoggerNameOffset
    }

    private void Read()
    {
        var trace = _trace;
        var result = ProcessTrace(&trace, 1, null, null);
        if (result != 0) Log.Info($"frames: trace ended ({result})");
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(EventRecord* record)
    {
        if (s_current is not { } counter || counter.Process == 0 || record->ProcessId != counter.Process) return;
        var id = record->Id;
        if (id == DxgiPresentStart && record->Provider == Dxgi || id == D3D9PresentStart && record->Provider == D3D9)
        {
            Interlocked.Increment(ref counter._frames);
        }
    }

    private void Stop()
    {
        if (_session == 0) return;
        Prepare((byte*)_properties);
        ControlTraceW(_session, null, (byte*)_properties, 1 /* EVENT_TRACE_CONTROL_STOP */);
        _session = 0;
    }

    public void Dispose()
    {
        if (s_current == this) s_current = null;
        Stop();
        if (_trace != InvalidTrace) CloseTrace(_trace);
        _trace = InvalidTrace;
        _reader?.Join(TimeSpan.FromSeconds(2));
        Marshal.FreeHGlobal(_properties);
    }

    /// <summary>The start of EVENT_RECORD: its EVENT_HEADER, up to the descriptor's id.</summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct EventRecord
    {
        [FieldOffset(12)] public uint ProcessId;
        [FieldOffset(24)] public Guid Provider;
        [FieldOffset(40)] public ushort Id;
    }

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint StartTraceW(ulong* session, string name, byte* properties);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ControlTraceW(ulong session, string? name, byte* properties, uint control);

    [LibraryImport("advapi32.dll")]
    private static partial uint EnableTraceEx2(ulong session, Guid* provider, uint control, byte level, ulong matchAny, ulong matchAll, uint timeout, void* parameters);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial ulong OpenTraceW(byte* logfile);

    [LibraryImport("advapi32.dll")]
    private static partial uint ProcessTrace(ulong* traces, uint count, void* start, void* end);

    [LibraryImport("advapi32.dll")]
    private static partial uint CloseTrace(ulong trace);
}

/// <summary>
/// The CPU's temperature from LibreHardwareMonitor or OpenHardwareMonitor, which publish their sensors in
/// WMI while they run: the package's, or failing that the hottest core's. Null without either.
/// </summary>
internal sealed unsafe partial class SensorTemperature : IDisposable
{
    private static readonly Guid LocatorClass = new("4590F811-1D3A-11D0-891F-00AA004B2E24");
    private static readonly Guid LocatorId = new("DC12A687-737F-11CF-884D-00AA004B2E24");
    private readonly bool _com;
    private nint _services;
    private long _lookedAt = long.MinValue;

    public SensorTemperature() => _com = CoInitializeEx(0, 0 /* COINIT_MULTITHREADED */) >= 0;

    public double? Read()
    {
        if (!_com) return null;
        // Without a sensor program, it is looked for again every half minute, not every second.
        if (_services == 0)
        {
            if (Environment.TickCount64 - _lookedAt < 30_000) return null;
            _lookedAt = Environment.TickCount64;
            _services = Connect(@"ROOT\LibreHardwareMonitor");
            if (_services == 0) _services = Connect(@"ROOT\OpenHardwareMonitor");
            if (_services == 0) return null;
        }
        var results = Query("SELECT Name, Value FROM Sensor WHERE SensorType = 'Temperature'");
        if (results == 0)
        {
            Marshal.Release(_services);
            _services = 0;
            return null;
        }
        try
        {
            double? package = null, hottest = null;
            while (Next(results) is { } sensor)
            {
                if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase) || sensor.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)) package ??= sensor.Value;
                if (sensor.Name.StartsWith("CPU", StringComparison.OrdinalIgnoreCase) || sensor.Name.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
                {
                    hottest = Math.Max(hottest ?? 0, sensor.Value);
                }
            }
            return package ?? hottest;
        }
        finally
        {
            Marshal.Release(results);
        }
    }

    public void Dispose()
    {
        if (_services != 0) Marshal.Release(_services);
        _services = 0;
        if (_com) CoUninitialize();
    }

    private static nint Connect(string path)
    {
        var clsid = LocatorClass;
        var iid = LocatorId;
        nint locator;
        if (CoCreateInstance(&clsid, 0, 1 /* CLSCTX_INPROC_SERVER */, &iid, &locator) < 0) return 0;
        var name = SysAllocString(path);
        try
        {
            nint services;
            var connect = (delegate* unmanaged[Stdcall]<nint, nint, nint, nint, nint, int, nint, nint, nint*, int>)Slot(locator, 3);
            if (connect(locator, name, 0, 0, 0, 0, 0, 0, &services) < 0) return 0;
            CoSetProxyBlanket(services, 10, 0, 0, 3, 3, 0, 0);
            return services;
        }
        finally
        {
            SysFreeString(name);
            Marshal.Release(locator);
        }
    }

    private nint Query(string wql)
    {
        var language = SysAllocString("WQL");
        var query = SysAllocString(wql);
        try
        {
            nint results;
            var exec = (delegate* unmanaged[Stdcall]<nint, nint, nint, int, nint, nint*, int>)Slot(_services, 20 /* ExecQuery */);
            if (exec(_services, language, query, 0x30, 0, &results) < 0) return 0;
            CoSetProxyBlanket(results, 10, 0, 0, 3, 3, 0, 0);
            return results;
        }
        finally
        {
            SysFreeString(query);
            SysFreeString(language);
        }
    }

    private static (string Name, double Value)? Next(nint enumerator)
    {
        nint item;
        uint returned;
        var next = (delegate* unmanaged[Stdcall]<nint, int, uint, nint*, uint*, int>)Slot(enumerator, 4);
        if (next(enumerator, 2000, 1, &item, &returned) < 0 || returned == 0) return null;
        try
        {
            var get = (delegate* unmanaged[Stdcall]<nint, char*, int, Variant*, int*, int*, int>)Slot(item, 4);
            Variant name = default, value = default;
            try
            {
                fixed (char* n = "Name")
                fixed (char* v = "Value")
                {
                    if (get(item, n, 0, &name, null, null) < 0 || get(item, v, 0, &value, null, null) < 0) return ("", 0);
                }
                var text = name.Type == 8 /* VT_BSTR */ && name.Pointer != 0 ? Marshal.PtrToStringBSTR(name.Pointer) : "";
                double reading = value.Type switch
                {
                    4 /* VT_R4 */ => value.Single,
                    5 /* VT_R8 */ => value.Double,
                    _ => 0,
                };
                return (text, reading);
            }
            finally
            {
                VariantClear(&name);
                VariantClear(&value);
            }
        }
        finally
        {
            Marshal.Release(item);
        }
    }

    private static nint Slot(nint instance, int index) => (*(nint**)instance)[index];

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public float Single;
        [FieldOffset(8)] public double Double;
        [FieldOffset(8)] public nint Pointer;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint model);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

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
