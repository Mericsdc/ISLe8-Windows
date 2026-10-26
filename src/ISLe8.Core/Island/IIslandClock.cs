namespace ISLe8.Core.Island;

/// <summary>
/// One-shot timers for <see cref="IslandModel"/>. The app runs them on its UI thread; tests move
/// time by hand.
/// </summary>
public interface IIslandClock
{
    /// <summary>Runs <paramref name="action"/> once after <paramref name="delay"/>; disposing the result cancels it.</summary>
    IDisposable After(TimeSpan delay, Action action);
}
