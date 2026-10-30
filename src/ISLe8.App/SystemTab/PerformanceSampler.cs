using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.SystemInfo;
using Microsoft.UI.Dispatching;

namespace ISLe8.SystemTab;

/// <summary>
/// The performance pill's figures (Windows only), once a second on a worker thread and only while the
/// pill shows: CPU from GetSystemTimes; GPU from Windows' "GPU Engine" counters, as Task Manager; the GPU's
/// temperature from its driver (D3DKMT, also Task Manager's); the CPU's from LibreHardwareMonitor or
/// OpenHardwareMonitor when one runs, or else a laptop's integrated GPU on the same die (Windows has no
/// CPU sensor of its own); frames from ETW
/// (<see cref="FrameCounter"/>), counted for the program in front.
/// </summary>
internal sealed partial class PerformanceSampler(DispatcherQueue queue)
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(1);

    private CancellationTokenSource? _running;

    public PerformanceSample? Sample { get; private set; }

    /// <summary>A new sample, on the UI thread.</summary>
    public event Action? Changed;

    /// <summary>The process whose frames count: the game in front, or 0.</summary>
    public uint FrameProcess { get; set; }

    public void Run(bool run)
    {
        if (run == _running is not null) return;
        if (!run)
        {
            _running!.Cancel();
            _running = null;
            Sample = null;
            return;
        }
        var running = _running = new CancellationTokenSource();
        var thread = new Thread(() => Loop(running.Token)) { IsBackground = true, Name = "ISLe8 performance", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    private void Loop(CancellationToken stop)
    {
        using var gpu = new GpuCounters();
        using var frames = new FrameCounter();
        using var cpuTemperature = new SensorTemperature();
        (ulong Idle, ulong Kernel, ulong User)? lastCpu = null;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                frames.Process = FrameProcess;
                var cpu = 0.0;
                if (GetSystemTimes(out var idle, out var kernel, out var user))
                {
                    if (lastCpu is { } last) cpu = SystemMath.Cpu(last, (idle, kernel, user));
                    lastCpu = (idle, kernel, user);
                }
                var use = gpu.Read();
                var (gpuTemperature, integrated) = GpuTemperature.Read(use?.Luid);
                var cpuCelsius = PerformanceMath.Temperature(cpuTemperature.Read()) ?? PerformanceMath.Temperature(integrated);
                var sample = new PerformanceSample(frames.TakeFps(), cpu, use?.Busy, PerformanceMath.Temperature(gpuTemperature), cpuCelsius);
                queue.TryEnqueue(() =>
                {
                    if (stop.IsCancellationRequested) return;
                    Sample = sample;
                    Changed?.Invoke();
                });
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Log.Error(e, "performance");
            }
            stop.WaitHandle.WaitOne(Every);
        }
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
