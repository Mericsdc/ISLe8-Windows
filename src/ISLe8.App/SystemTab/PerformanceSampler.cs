using System.Runtime.InteropServices;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.SystemInfo;
using ISLe8.UI;
using Microsoft.UI.Dispatching;

namespace ISLe8.SystemTab;

/// <summary>
/// The performance pill's figures (Windows only), once a second on a worker thread while the pill is the
/// island's activity (opening the island or a banner doesn't stop it, so it isn't set up again each time):
/// CPU from GetSystemTimes; GPU from Windows' "GPU Engine" counters, as Task Manager; the GPU's
/// temperature from its driver (D3DKMT, also Task Manager's); the CPU's from LibreHardwareMonitor or
/// OpenHardwareMonitor when one runs, or else a laptop's integrated GPU on the same die (Windows has no
/// CPU sensor of its own); frames from ETW (<see cref="FrameCounter"/>), counted for the program in front.
/// </summary>
internal sealed partial class PerformanceSampler(DispatcherQueue queue) : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(1);

    private CancellationTokenSource? _running;
    private Thread? _worker;

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
        _worker = new Thread(() => Loop(running.Token)) { IsBackground = true, Name = "ISLe8 performance", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    /// <summary>Stops and waits a moment for the worker, so its ETW session doesn't outlive ISLe8.</summary>
    public void Dispose()
    {
        Run(false);
        _worker?.Join(TimeSpan.FromSeconds(3));
    }

    private void Loop(CancellationToken stop)
    {
        GpuCounters? gpu = null;
        FrameCounter? frames = null;
        SensorTemperature? cpuTemperature = null;
        try
        {
            gpu = new GpuCounters();
            frames = new FrameCounter();
            cpuTemperature = new SensorTemperature();
            (ulong Idle, ulong Kernel, ulong User)? lastCpu = null;
            // The first round only takes the starting points: CPU and GPU are shares of the time since then.
            var primed = false;
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
                    var fps = frames.TakeFps();
                    if (primed)
                    {
                        var (gpuTemperature, integrated) = GpuTemperature.Read(use?.Luid);
                        var cpuCelsius = PerformanceMath.Temperature(cpuTemperature.Read()) ?? PerformanceMath.Temperature(integrated);
                        var sample = new PerformanceSample(fps, cpu, use?.Busy, PerformanceMath.Temperature(gpuTemperature), cpuCelsius);
                        queue.Post(() =>
                        {
                            if (stop.IsCancellationRequested) return;
                            Sample = sample;
                            Changed?.Invoke();
                        });
                    }
                    primed = true;
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    Log.Error(e, "performance");
                }
                stop.WaitHandle.WaitOne(Every);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A worker thread's exception would end the process.
            Log.Error(e, "performance");
        }
        finally
        {
            frames?.Dispose();
            gpu?.Dispose();
            cpuTemperature?.Dispose();
        }
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
