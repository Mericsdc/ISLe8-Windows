using ISLe8.Core.Island;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Timer;

public enum TimerMode
{
    Idle,
    Countdown,
    Pomodoro,
    Stopwatch,
}

/// <summary>What the banner says when a phase ends (TimerModule.swift, TimerEvent).</summary>
public abstract record TimerEvent
{
    public sealed record Finished : TimerEvent;
    public sealed record BreakStarts(int Minutes) : TimerEvent;
    public sealed record WorkStarts(int Minutes) : TimerEvent;
}

/// <summary>
/// A countdown, a Pomodoro cycle or a stopwatch (TimerModule.swift). Times are worked out from the
/// clock's dates, so nothing drifts and a sleep doesn't stop them; the only thing that waits is one
/// timer until the countdown or phase ends. Whatever shows the time ticks on its own, only while seen.
/// </summary>
public sealed class IslandTimer(AppSettings settings, IIslandClock clock)
{
    private IDisposable? _end;
    /// <summary>While running: when a countdown ends, or when a stopwatch (re)started.</summary>
    private DateTimeOffset? _anchor;

    public TimerMode Mode { get; private set; }

    /// <summary>In a Pomodoro: a break rather than focus.</summary>
    public bool IsBreak { get; private set; }

    /// <summary>In a Pomodoro: which round, from 1.</summary>
    public int Round { get; private set; }

    /// <summary>Total length of the current countdown or Pomodoro phase.</summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>While paused: time left (countdown) or time elapsed (stopwatch).</summary>
    public TimeSpan? PausedValue { get; private set; }

    public bool IsRunning => Mode != TimerMode.Idle && PausedValue is null;

    /// <summary>Anything changed: started, paused, resumed, a new phase, stopped.</summary>
    public event Action? Changed;

    /// <summary>A phase ended; the banner and the sound.</summary>
    public event Action<TimerEvent>? PhaseEnded;

    public void StartCountdown(int minutes) => Begin(TimerMode.Countdown, false, 0, TimeSpan.FromMinutes(minutes));

    public void StartPomodoro() => Begin(TimerMode.Pomodoro, false, 1, TimeSpan.FromMinutes(settings.PomodoroWorkMinutes));

    public void StartStopwatch()
    {
        Cancel();
        Mode = TimerMode.Stopwatch;
        IsBreak = false;
        Round = 0;
        Duration = TimeSpan.Zero;
        _anchor = clock.Now;
        PausedValue = null;
        Changed?.Invoke();
    }

    public void Pause()
    {
        if (!IsRunning || _anchor is not { } anchor) return;
        Cancel();
        var now = clock.Now;
        PausedValue = Mode == TimerMode.Stopwatch ? now - anchor : Max(anchor - now, TimeSpan.Zero);
        Changed?.Invoke();
    }

    public void Resume()
    {
        if (PausedValue is not { } paused) return;
        PausedValue = null;
        if (Mode == TimerMode.Stopwatch)
        {
            _anchor = clock.Now - paused;
        }
        else
        {
            _anchor = clock.Now + paused;
            ScheduleEnd();
        }
        Changed?.Invoke();
    }

    public void Reset()
    {
        Cancel();
        Mode = TimerMode.Idle;
        IsBreak = false;
        Round = 0;
        _anchor = null;
        PausedValue = null;
        Duration = TimeSpan.Zero;
        Changed?.Invoke();
    }

    /// <summary>Time left for a countdown, time elapsed for a stopwatch.</summary>
    public TimeSpan Value(DateTimeOffset at)
    {
        if (PausedValue is { } paused) return paused;
        if (_anchor is not { } anchor) return TimeSpan.Zero;
        return Mode == TimerMode.Stopwatch ? at - anchor : Max(anchor - at, TimeSpan.Zero);
    }

    /// <summary>0…1 of the countdown done; 0 for a stopwatch.</summary>
    public double Progress(DateTimeOffset at) =>
        Mode == TimerMode.Stopwatch || Duration <= TimeSpan.Zero ? 0 : 1 - Value(at) / Duration;

    private void Begin(TimerMode mode, bool isBreak, int round, TimeSpan length)
    {
        Mode = mode;
        IsBreak = isBreak;
        Round = round;
        Duration = length;
        _anchor = clock.Now + length;
        PausedValue = null;
        ScheduleEnd();
        Changed?.Invoke();
    }

    private void ScheduleEnd()
    {
        Cancel();
        if (_anchor is not { } anchor) return;
        _end = clock.After(Max(anchor - clock.Now, TimeSpan.Zero), FinishPhase);
    }

    private void FinishPhase()
    {
        _end = null;
        if (Mode == TimerMode.Pomodoro)
        {
            if (IsBreak)
            {
                var minutes = settings.PomodoroWorkMinutes;
                PhaseEnded?.Invoke(new TimerEvent.WorkStarts(minutes));
                Begin(TimerMode.Pomodoro, false, Round + 1, TimeSpan.FromMinutes(minutes));
            }
            else
            {
                var minutes = settings.PomodoroBreakMinutes;
                PhaseEnded?.Invoke(new TimerEvent.BreakStarts(minutes));
                Begin(TimerMode.Pomodoro, true, Round, TimeSpan.FromMinutes(minutes));
            }
            return;
        }
        PhaseEnded?.Invoke(new TimerEvent.Finished());
        Reset();
    }

    private void Cancel()
    {
        _end?.Dispose();
        _end = null;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

public static class TimerText
{
    /// <summary>"4:05", "24:59", "1:05:00". A countdown rounds up, so it reads 0:00 only at the end.</summary>
    public static string Format(TimeSpan time, bool roundingUp = true)
    {
        var seconds = time.TotalSeconds;
        var total = (long)(roundingUp ? Math.Ceiling(seconds - 1e-9) : Math.Floor(seconds));
        if (total < 0) total = 0;
        return total >= 3600
            ? $"{total / 3600}:{total / 60 % 60:00}:{total % 60:00}"
            : $"{total / 60}:{total % 60:00}";
    }
}
