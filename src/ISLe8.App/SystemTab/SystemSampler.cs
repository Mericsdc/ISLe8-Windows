using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.SystemInfo;
using ISLe8.UI;
using Microsoft.UI.Dispatching;

namespace ISLe8.SystemTab;

/// <summary>
/// Task Manager in brief, for the System tab (SystemStats.swift): CPU, memory, the system drive,
/// Ethernet and Wi-Fi traffic, and the user's own processes using the most CPU. Sampled every two
/// seconds on a worker thread, only while the tab is on screen; nothing runs otherwise.
/// </summary>
internal sealed partial class SystemSampler(DispatcherQueue queue)
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(2);

    private CancellationTokenSource? _running;
    private (ulong Idle, ulong Kernel, ulong User)? _lastCpu;
    private (ulong Received, ulong Sent, DateTimeOffset At)? _lastNetwork;
    private Dictionary<int, long> _lastProcessTimes = [];
    private DateTimeOffset _lastProcessRead;
    private (long Total, long Free) _disk;
    private DateTimeOffset _diskReadAt;

    public SystemSample? Sample { get; private set; }
    public NetworkLink Link { get; private set; } = NetworkLink.Other;

    /// <summary>A new sample, on the UI thread.</summary>
    public event Action? Changed;

    /// <summary>Starts sampling while <paramref name="run"/>; stops at once otherwise.</summary>
    public void Run(bool run)
    {
        if (run == _running is not null) return;
        if (!run)
        {
            _running!.Cancel();
            _running = null;
            return;
        }
        var running = _running = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!running.IsCancellationRequested)
            {
                try
                {
                    var sample = Next();
                    var link = ReadLink();
                    queue.Post(() =>
                    {
                        if (running.IsCancellationRequested) return;
                        Sample = sample;
                        Link = link;
                        Changed?.Invoke();
                    });
                    await Task.Delay(Every, running.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Log.Error(e, "system sample");
                    return;
                }
            }
        });
    }

    private SystemSample Next()
    {
        var now = DateTimeOffset.Now;
        if (now - _diskReadAt > TimeSpan.FromSeconds(10))
        {
            _disk = ReadDisk();
            _diskReadAt = now;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        GlobalMemoryStatusEx(ref memory);
        var traffic = ReadNetwork(now);
        return new SystemSample
        {
            Cpu = ReadCpu(),
            Cores = Environment.ProcessorCount,
            MemoryUsed = memory.TotalPhysical - memory.AvailablePhysical,
            MemoryTotal = memory.TotalPhysical,
            DiskTotal = _disk.Total,
            DiskFree = _disk.Free,
            Download = traffic.Download,
            Upload = traffic.Upload,
            Top = ReadTopProcesses(now),
        };
    }

    private double ReadCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var now = (idle, kernel, user);
        var last = _lastCpu;
        _lastCpu = now;
        return last is { } before ? SystemMath.Cpu(before, now) : 0;
    }

    private static (long Total, long Free) ReadDisk()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
            return (drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (IOException)
        {
            return (0, 0);
        }
    }

    /// <summary>Bytes in and out of the Ethernet and Wi-Fi adapters, not virtual ones, since the last sample.</summary>
    private (double Download, double Upload) ReadNetwork(DateTimeOffset now)
    {
        ulong received = 0, sent = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up) continue;
            if (adapter.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT)) continue;
            var description = adapter.Description;
            if (description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) || description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var statistics = adapter.GetIPStatistics();
                received += (ulong)statistics.BytesReceived;
                sent += (ulong)statistics.BytesSent;
            }
            catch (NetworkInformationException)
            {
                // An adapter going away mid-read.
            }
        }
        var last = _lastNetwork;
        _lastNetwork = (received, sent, now);
        if (last is not { } before) return (0, 0);
        var seconds = (now - before.At).TotalSeconds;
        return (SystemMath.Rate(before.Received, received, seconds), SystemMath.Rate(before.Sent, sent, seconds));
    }

    /// <summary>The user's own processes by CPU over the last sample; others can't be read without elevation, and Task Manager is the place for those.</summary>
    private List<ProcessUse> ReadTopProcesses(DateTimeOffset now)
    {
        var session = Process.GetCurrentProcess().SessionId;
        var seconds = (now - _lastProcessRead).TotalSeconds;
        var times = new Dictionary<int, long>();
        var busy = new List<ProcessUse>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.SessionId != session || process.Id == 0) continue;
                    var ticks = process.TotalProcessorTime.Ticks;
                    times[process.Id] = ticks;
                    if (_lastProcessTimes.TryGetValue(process.Id, out var before) && seconds is > 0 and < 5)
                    {
                        busy.Add(new ProcessUse(process.Id, process.ProcessName, SystemMath.ProcessCpu(before, ticks, seconds), (ulong)process.WorkingSet64));
                    }
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
                {
                    // Gone, or not the user's to read.
                }
            }
        }
        _lastProcessTimes = times;
        _lastProcessRead = now;
        return busy.OrderByDescending(p => p.Cpu).Take(5).ToList();
    }

    /// <summary>How the computer is online: Ethernet, Wi-Fi, mobile data, something else, or not.</summary>
    private static NetworkLink ReadLink()
    {
        try
        {
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (profile is null) return NetworkLink.Offline;
            return profile.NetworkAdapter?.IanaInterfaceType switch
            {
                6 => NetworkLink.Wired,
                71 => NetworkLink.WiFi,
                243 or 244 => NetworkLink.Cellular,
                _ => profile.IsWlanConnectionProfile ? NetworkLink.WiFi : profile.IsWwanConnectionProfile ? NetworkLink.Cellular : NetworkLink.Other,
            };
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            return NetworkLink.Other;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
