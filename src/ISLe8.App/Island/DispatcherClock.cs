using ISLe8.Core.Island;
using ISLe8.UI;
using Microsoft.UI.Dispatching;

namespace ISLe8.Island;

/// <summary><see cref="IslandModel"/>'s timers, run on the UI thread. Nothing ticks between them.</summary>
internal sealed class DispatcherClock(DispatcherQueue queue) : IIslandClock
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public IDisposable After(TimeSpan delay, Action action)
    {
        var timer = queue.CreateTimer();
        timer.IsRepeating = false;
        timer.Interval = delay;
        var pending = new Pending(timer);
        timer.OnTick(() =>
        {
            timer.Stop();
            if (!pending.IsCancelled) action();
        });
        timer.Start();
        return pending;
    }

    private sealed class Pending(DispatcherQueueTimer timer) : IDisposable
    {
        public bool IsCancelled { get; private set; }

        public void Dispose()
        {
            IsCancelled = true;
            timer.Stop();
        }
    }
}
