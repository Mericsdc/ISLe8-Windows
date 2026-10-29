using ISLe8.Core.Settings;
using ISLe8.Core.Timer;

namespace ISLe8.Core.Tests;

/// <summary>The timer (PORTING.md §3.7): countdown, Pomodoro and stopwatch, worked out from dates.</summary>
public class TimerTests
{
    private readonly ManualClock _clock = new();
    private readonly AppSettings _settings = new();
    private readonly IslandTimer _timer;
    private readonly List<TimerEvent> _events = [];

    public TimerTests()
    {
        _timer = new IslandTimer(_settings, _clock);
        _timer.PhaseEnded += _events.Add;
    }

    private void Wait(double seconds) => _clock.Advance(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void ACountdownEndsOnTimeAndSaysSo()
    {
        _timer.StartCountdown(5);
        Wait(100);
        Assert.Equal(TimeSpan.FromSeconds(200), _timer.Value(_clock.Now));
        Assert.Equal(1 / 3.0, _timer.Progress(_clock.Now), precision: 6);

        Wait(199.9);
        Assert.True(_timer.IsRunning);
        Wait(0.1);
        Assert.Equal([new TimerEvent.Finished()], _events);
        Assert.Equal(TimerMode.Idle, _timer.Mode);
    }

    [Fact]
    public void APauseHoldsTheTimeLeft()
    {
        _timer.StartCountdown(5);
        Wait(60);
        _timer.Pause();
        Wait(3600);
        Assert.Empty(_events);
        Assert.Equal(TimeSpan.FromSeconds(240), _timer.Value(_clock.Now));
        Assert.False(_timer.IsRunning);

        _timer.Resume();
        Wait(239);
        Assert.Empty(_events);
        Wait(1);
        Assert.Single(_events);
    }

    [Fact]
    public void APomodoroAlternatesFocusAndBreakRoundByRound()
    {
        _settings.PomodoroWorkMinutes = 25;
        _settings.PomodoroBreakMinutes = 5;
        _timer.StartPomodoro();
        Assert.Equal((TimerMode.Pomodoro, false, 1), (_timer.Mode, _timer.IsBreak, _timer.Round));

        Wait(25 * 60);
        Assert.Equal([new TimerEvent.BreakStarts(5)], _events);
        Assert.Equal((true, 1), (_timer.IsBreak, _timer.Round));
        Assert.Equal(TimeSpan.FromMinutes(5), _timer.Duration);

        Wait(5 * 60);
        Assert.Equal(new TimerEvent.WorkStarts(25), _events[^1]);
        Assert.Equal((false, 2), (_timer.IsBreak, _timer.Round));
        Assert.True(_timer.IsRunning);
    }

    [Fact]
    public void AStopwatchCountsUpAndPausesWhereItIs()
    {
        _timer.StartStopwatch();
        Wait(90);
        Assert.Equal(TimeSpan.FromSeconds(90), _timer.Value(_clock.Now));
        Assert.Equal(0, _timer.Progress(_clock.Now));

        _timer.Pause();
        Wait(30);
        _timer.Resume();
        Wait(10);
        Assert.Equal(TimeSpan.FromSeconds(100), _timer.Value(_clock.Now));
        Assert.Empty(_events);
    }

    [Fact]
    public void StoppingCancelsTheEnd()
    {
        _timer.StartCountdown(15);
        _timer.Reset();
        Wait(20 * 60);
        Assert.Empty(_events);
        Assert.Equal(TimerMode.Idle, _timer.Mode);
    }

    [Theory]
    [InlineData(245.2, true, "4:06")]
    [InlineData(245.2, false, "4:05")]
    [InlineData(1499, true, "24:59")]
    [InlineData(3900, true, "1:05:00")]
    [InlineData(0, true, "0:00")]
    public void TimesReadAsOnTheMac(double seconds, bool roundingUp, string text)
    {
        Assert.Equal(text, TimerText.Format(TimeSpan.FromSeconds(seconds), roundingUp));
    }

    [Fact]
    public void ATimerWidensThePillAndShowsInItsEars()
    {
        var model = new Island.IslandModel(_settings, _clock);
        model.SetTimerLive(true);
        Assert.Equal((Geometry.PillKind.Compact, Geometry.PillActivity.Timer), (model.Pill, model.Activity));

        model.SetTimerLive(false);
        Assert.Equal(Geometry.PillKind.Idle, model.Pill);
    }

    [Fact]
    public void MusicTakesTheEarsAndHandsThemBackEightSecondsAfterStopping()
    {
        var model = new Island.IslandModel(_settings, _clock);
        model.SetTimerLive(true);
        model.SetLive(true);
        Assert.Equal(Geometry.PillActivity.Media, model.Activity);

        model.SetLive(false);
        Wait(7.9);
        Assert.Equal(Geometry.PillActivity.Media, model.Activity);
        Wait(0.1);
        Assert.Equal((Geometry.PillKind.Compact, Geometry.PillActivity.Timer), (model.Pill, model.Activity));
    }

    [Fact]
    public void PomodoroLengthsStayInTheMacsRanges()
    {
        var settings = SettingsStore.Import("""{ "pomodoroWorkMinutes": 93, "pomodoroBreakMinutes": 0 }""");

        Assert.Equal(90, settings.PomodoroWorkMinutes);
        Assert.Equal(1, settings.PomodoroBreakMinutes);
    }
}
