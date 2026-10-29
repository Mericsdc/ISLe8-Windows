using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Tests;

public class IslandModelTests
{
    private readonly ManualClock _clock = new();
    private readonly AppSettings _settings = new();
    private readonly IslandModel _model;

    public IslandModelTests()
    {
        _model = new IslandModel(_settings, _clock);
    }

    private void Wait(double milliseconds) => _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));

    private void OpenByHover()
    {
        _model.SetHovering(true);
        Wait(50);
        Assert.Equal(IslandState.Expanded, _model.State);
    }

    // MARK: Hover mode

    [Fact]
    public void WithNoDelayItStillWaitsFiftyMilliseconds()
    {
        _model.SetHovering(true);
        Wait(49);
        Assert.Equal(IslandState.Pill, _model.State);
        Wait(1);
        Assert.Equal(IslandState.Expanded, _model.State);
    }

    [Fact]
    public void APointerPassingOverDoesNotOpenIt()
    {
        _model.SetHovering(true);
        Wait(30);
        _model.SetHovering(false);
        Wait(1000);

        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void TheOpenDelaySettingIsUsed()
    {
        _settings.OpenDelay = 0.3;
        _model.SetHovering(true);
        Wait(299);
        Assert.Equal(IslandState.Pill, _model.State);
        Wait(1);
        Assert.Equal(IslandState.Expanded, _model.State);
    }

    [Fact]
    public void LeavingClosesAfterTheCloseDelay()
    {
        OpenByHover();
        _model.SetHovering(false);
        Wait(349);
        Assert.Equal(IslandState.Expanded, _model.State);
        Wait(1);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void ComingBackBeforeItClosesKeepsItOpen()
    {
        OpenByHover();
        _model.SetHovering(false);
        Wait(200);
        _model.SetHovering(true);
        Wait(1000);

        Assert.Equal(IslandState.Expanded, _model.State);
    }

    [Fact]
    public void HoverModeNeverPeeks()
    {
        _settings.OpenDelay = 1;
        _model.SetHovering(true);
        Wait(500);

        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void AClickOpensAtOnce()
    {
        _settings.OpenDelay = 1;
        _model.SetHovering(true);
        _model.Click();

        Assert.Equal(IslandState.Expanded, _model.State);
    }

    // MARK: Click mode

    [Fact]
    public void ClickModeSwellsThenOpensOnlyOnAClick()
    {
        _settings.OpenMode = OpenMode.Click;
        _model.SetHovering(true);
        Wait(59);
        Assert.Equal(IslandState.Pill, _model.State);
        Wait(1);
        Assert.Equal(IslandState.Peek, _model.State);
        Wait(5000);
        Assert.Equal(IslandState.Peek, _model.State);

        _model.Click();
        Assert.Equal(IslandState.Expanded, _model.State);
    }

    [Fact]
    public void LeavingAPeekSettlesBackQuickly()
    {
        _settings.OpenMode = OpenMode.Click;
        _model.SetHovering(true);
        Wait(60);
        _model.SetHovering(false);
        Wait(49);
        Assert.Equal(IslandState.Peek, _model.State);
        Wait(1);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void ClickModeStillClosesWhenThePointerLeaves()
    {
        _settings.OpenMode = OpenMode.Click;
        _model.SetHovering(true);
        _model.Click();
        _model.SetHovering(false);
        Wait(350);

        Assert.Equal(IslandState.Pill, _model.State);
    }

    // MARK: Pin, menu, outside clicks

    [Fact]
    public void APinnedIslandStaysWhenThePointerLeaves()
    {
        OpenByHover();
        _model.SetPinned(true);
        _model.SetHovering(false);
        Wait(5000);

        Assert.Equal(IslandState.Expanded, _model.State);
    }

    [Fact]
    public void UnpinningWithThePointerAwayClosesAfterTheDelay()
    {
        OpenByHover();
        _model.SetPinned(true);
        _model.SetHovering(false);
        _model.SetPinned(false);
        Wait(349);
        Assert.Equal(IslandState.Expanded, _model.State);
        Wait(1);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void PinningFromThePillOpensIt()
    {
        _model.SetPinned(true);

        Assert.Equal(IslandState.Expanded, _model.State);
    }

    [Fact]
    public void AMenuOpenedOnThePillKeepsItAPill()
    {
        _model.SetHovering(true);
        _model.SetMenuOpen(true, pointerInside: true);
        Wait(1000);

        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void AMenuOpenedOnTheIslandKeepsItOpen()
    {
        OpenByHover();
        _model.SetMenuOpen(true, pointerInside: true);
        _model.SetHovering(false);
        Wait(5000);

        Assert.Equal(IslandState.Expanded, _model.State);
    }

    [Fact]
    public void ClosingTheMenuOnThePillLetsItOpen()
    {
        _model.SetHovering(true);
        _model.SetMenuOpen(true, pointerInside: true);
        _model.SetMenuOpen(false, pointerInside: true);
        Wait(50);

        Assert.Equal(IslandState.Expanded, _model.State);
    }

    [Fact]
    public void ClosingTheMenuWithThePointerAwayClosesTheIsland()
    {
        OpenByHover();
        _model.SetMenuOpen(true, pointerInside: true);
        _model.SetMenuOpen(false, pointerInside: false);
        Wait(350);

        Assert.Equal(IslandState.Pill, _model.State);
    }

    [Fact]
    public void AClickOutsideClosesOnlyAnIslandThePointerHasLeft()
    {
        _settings.CloseDelay = 1.5;
        OpenByHover();
        _model.ClickedOutside();
        Assert.Equal(IslandState.Expanded, _model.State);

        _model.SetHovering(false);
        Assert.True(_model.WantsOutsideClicks);
        _model.ClickedOutside();
        Assert.Equal(IslandState.Pill, _model.State);
        Assert.False(_model.WantsOutsideClicks);
    }

    [Fact]
    public void APinnedIslandIgnoresOutsideClicks()
    {
        OpenByHover();
        _model.SetPinned(true);
        _model.SetHovering(false);

        Assert.False(_model.WantsOutsideClicks);
        _model.ClickedOutside();
        Assert.Equal(IslandState.Expanded, _model.State);
    }

    [Fact]
    public void ALiftedFingerKeepsItOpenFourSeconds()
    {
        _model.SetHovering(true, touch: true);
        _model.Click();
        _model.SetHovering(false, touch: true);
        Wait(3999);
        Assert.Equal(IslandState.Expanded, _model.State);
        Wait(1);
        Assert.Equal(IslandState.Pill, _model.State);
    }

    // MARK: Pill and springs

    [Fact]
    public void ThePillWidensAtOnceAndNarrowsEightSecondsLaterAsOnTheMac()
    {
        _model.SetLive(true);
        Assert.Equal(PillKind.Compact, _model.Pill);

        _model.SetLive(false);
        Wait(7999);
        Assert.Equal(PillKind.Compact, _model.Pill);
        Wait(1);
        Assert.Equal(PillKind.Idle, _model.Pill);
    }

    [Fact]
    public void ASongChangeDoesNotNarrowThePill()
    {
        _model.SetLive(true);
        _model.SetLive(false);
        Wait(400);
        _model.SetLive(true);
        Wait(10_000);

        Assert.Equal(PillKind.Compact, _model.Pill);
    }

    [Fact]
    public void GrowingOpensAndShrinkingCloses()
    {
        var changes = new List<(IslandState From, IslandState To, bool Growing)>();
        _model.StateChanged += from => changes.Add((from, _model.State, _model.IsGrowing));

        OpenByHover();
        _model.Close();

        Assert.Equal([(IslandState.Pill, IslandState.Expanded, true), (IslandState.Expanded, IslandState.Pill, false)], changes);
    }

    // MARK: Settings

    [Fact]
    public void TheDefaultsAreTheMacOnes()
    {
        var settings = new AppSettings();

        Assert.Equal(OpenMode.Hover, settings.OpenMode);
        Assert.Equal(0, settings.OpenDelay);
        Assert.Equal(0.35, settings.CloseDelay);
        Assert.Equal(1, settings.AnimationSpeed);
    }
}

/// <summary>Time that only moves when a test says so; due timers run in order.</summary>
internal sealed class ManualClock(DateTimeOffset start) : IIslandClock
{
    private readonly List<Timer> _timers = [];
    private TimeSpan _now;

    public ManualClock() : this(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero)) { }

    public DateTimeOffset Now => start + _now;

    public IDisposable After(TimeSpan delay, Action action)
    {
        var timer = new Timer(_now + delay, action, _timers);
        _timers.Add(timer);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        var end = _now + by;
        while (_timers.Where(timer => timer.Due <= end).OrderBy(timer => timer.Due).FirstOrDefault() is { } next)
        {
            _now = next.Due;
            _timers.Remove(next);
            next.Action();
        }
        _now = end;
    }

    /// <summary>A class, not a record: two timers due at the same moment must stay two.</summary>
    private sealed class Timer(TimeSpan due, Action action, List<Timer> owner) : IDisposable
    {
        public TimeSpan Due { get; } = due;
        public Action Action { get; } = action;

        public void Dispose() => owner.Remove(this);
    }
}
