using ISLe8.Core.Diagnostics;
using Microsoft.UI.Dispatching;

namespace ISLe8.UI;

/// <summary>
/// Work the UI thread runs outside XAML's own events: a queued callback, a timer's tick, the rest of an
/// async method. WinUI's UnhandledException never hears of an exception there; the process just ends
/// (a stowed exception in CoreMessaging). 0.2.6 closed that way whenever an update was out and Settings
/// opened. Here each one is caught and logged instead, as App's UnhandledException does for XAML's events.
/// </summary>
internal static class Guarded
{
    /// <summary><see cref="DispatcherQueue.TryEnqueue(DispatcherQueueHandler)"/>, guarded.</summary>
    public static bool Post(this DispatcherQueue queue, Action action) => queue.TryEnqueue(() => Run(action));

    /// <summary>A timer's tick, guarded.</summary>
    public static void OnTick(this DispatcherQueueTimer timer, Action tick) => timer.Tick += (_, _) => Run(tick);

    public static void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            // An accessory that stays up beats one that vanishes; the log says what happened.
            Log.Error(e, "ui callback");
        }
    }
}

/// <summary>The UI thread's continuations of async methods, <see cref="Guarded"/> (in place of DispatcherQueueSynchronizationContext).</summary>
internal sealed class GuardedSynchronizationContext(DispatcherQueue queue) : SynchronizationContext
{
    public override void Post(SendOrPostCallback callback, object? state) => queue.TryEnqueue(() => Guarded.Run(() => callback(state)));

    /// <summary>Only on the UI thread itself, as DispatcherQueueSynchronizationContext allows.</summary>
    public override void Send(SendOrPostCallback callback, object? state)
    {
        if (!queue.HasThreadAccess) throw new NotSupportedException("Send is only possible on the UI thread.");
        callback(state);
    }

    public override SynchronizationContext CreateCopy() => new GuardedSynchronizationContext(queue);
}
