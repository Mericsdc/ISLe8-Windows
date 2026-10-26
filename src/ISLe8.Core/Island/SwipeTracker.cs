namespace ISLe8.Core.Island;

/// <summary>
/// Two fingers sideways on the open island switch tabs, once per swipe (IslandController.swift,
/// trackSwipe). A precision touchpad sends horizontal wheel deltas with no start or end to the
/// gesture, so a short pause ends one swipe. A tilting mouse wheel works the same way.
/// </summary>
public sealed class SwipeTracker
{
    /// <summary>One wheel notch (WHEEL_DELTA). The Mac waits for 60 pt of finger travel.</summary>
    public const int Threshold = 120;

    /// <summary>This long without a delta and the next one starts a new swipe.</summary>
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(250);

    private int _distance;
    private bool _handled;
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    /// <summary>
    /// Adds a horizontal wheel delta (positive: content scrolls right, fingers move left). Returns 1 for
    /// the next tab, -1 for the previous one, 0 while the swipe is still too short or already used.
    /// </summary>
    public int Add(int delta, DateTimeOffset now)
    {
        if (now - _last > Pause)
        {
            _distance = 0;
            _handled = false;
        }
        _last = now;
        if (_handled) return 0;
        _distance += delta;
        if (Math.Abs(_distance) < Threshold) return 0;
        _handled = true;
        return _distance > 0 ? 1 : -1;
    }
}
