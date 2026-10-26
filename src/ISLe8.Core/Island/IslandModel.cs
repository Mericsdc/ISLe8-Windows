using ISLe8.Core.Geometry;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Island;

public enum IslandState
{
    /// <summary>At rest: the idle pill, or the wider one while something is live.</summary>
    Pill,
    /// <summary>Click mode only: the pointer rests on the pill, which swells a little to say it can be clicked.</summary>
    Peek,
    /// <summary>The full island.</summary>
    Expanded,
}

/// <summary>
/// The island's state machine, after IslandModel.swift: when it opens, closes and swells. The app
/// feeds it pointer, menu and media events and draws whatever state it is in. The hover rules
/// are the ones the Mac found by trial and error (PORTING.md §2).
/// </summary>
public sealed class IslandModel
{
    /// <summary>Even with no open delay the pointer must rest this long, so one passing over the pill doesn't open it.</summary>
    public static readonly TimeSpan MinimumOpenDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>Click mode: how long the pointer rests before the pill swells.</summary>
    public static readonly TimeSpan PeekDelay = TimeSpan.FromMilliseconds(60);

    /// <summary>Leaving a pill or a peek settles it back this quickly.</summary>
    public static readonly TimeSpan LeaveDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>A finger lifting is not leaving: an island opened by touch stays this long.</summary>
    public static readonly TimeSpan TouchCloseDelay = TimeSpan.FromSeconds(4);

    /// <summary>Players report "paused" for a moment between tracks; the pill waits this long before narrowing.</summary>
    public static readonly TimeSpan PillShrinkDelay = TimeSpan.FromSeconds(1.5);

    /// <summary>Reopening within this time returns to the tab you left; later it starts at the first tab.</summary>
    public static readonly TimeSpan TabMemory = TimeSpan.FromSeconds(15);

    private readonly AppSettings _settings;
    private readonly IIslandClock _clock;
    private IDisposable? _hoverTimer;
    private IDisposable? _closeTimer;
    private IDisposable? _shrinkTimer;
    private DateTimeOffset _closedAt = DateTimeOffset.MinValue;

    public IslandModel(AppSettings settings, IIslandClock clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public IslandState State { get; private set; } = IslandState.Pill;

    /// <summary>The pill the island shows, or will show, at rest.</summary>
    public PillKind Pill { get; private set; } = PillKind.Idle;

    /// <summary>The open island's tab, or the one it will open on.</summary>
    public IslandTab Tab { get; private set; } = IslandTab.Home;

    /// <summary>The tabs in the dock, in the user's order.</summary>
    public IReadOnlyList<IslandTab> VisibleTabs => IslandTabs.Visible(_settings);

    public bool IsPinned { get; private set; }
    public bool IsHovering { get; private set; }
    public bool IsMenuOpen { get; private set; }

    /// <summary>Whether the last change made the island bigger. Growing and shrinking use different springs.</summary>
    public bool IsGrowing { get; private set; } = true;

    /// <summary>
    /// A click anywhere else would close the island (<see cref="ClickedOutside"/>). Only then does the
    /// app need to watch the mouse outside its window.
    /// </summary>
    public bool WantsOutsideClicks => State == IslandState.Expanded && !IsPinned && !IsHovering && !IsMenuOpen;

    /// <summary><see cref="State"/> changed; the argument is the state it left.</summary>
    public event Action<IslandState>? StateChanged;

    /// <summary><see cref="Pill"/> changed.</summary>
    public event Action? PillChanged;

    /// <summary><see cref="Tab"/> changed; the argument is the tab it left.</summary>
    public event Action<IslandTab>? TabChanged;

    /// <summary>Anything changed: state, pill, pointer, pin or menu.</summary>
    public event Action? Changed;

    /// <summary>
    /// The pointer came onto the island (true) or left it (false). The app checks the real
    /// cursor position first; the system can report an exit that didn't happen.
    /// </summary>
    public void SetHovering(bool inside, bool touch = false)
    {
        IsHovering = inside;
        Cancel(ref _hoverTimer);
        Cancel(ref _closeTimer);

        if (inside)
        {
            if (State != IslandState.Expanded && !IsMenuOpen)
            {
                _hoverTimer = _settings.OpenMode == OpenMode.Hover
                    // Straight open; the short wait only skips a pointer passing over the pill.
                    ? _clock.After(Max(TimeSpan.FromSeconds(_settings.OpenDelay), MinimumOpenDelay), Open)
                    // Click mode: a slight swell says the island can be clicked.
                    : _clock.After(PeekDelay, () => Transition(IslandState.Peek));
            }
        }
        else if (!IsMenuOpen && !(IsPinned && State == IslandState.Expanded))
        {
            var delay = State != IslandState.Expanded ? LeaveDelay
                : touch ? TouchCloseDelay
                : TimeSpan.FromSeconds(_settings.CloseDelay);
            _closeTimer = _clock.After(delay, () => Transition(IslandState.Pill));
        }
        Changed?.Invoke();
    }

    /// <summary>A click or tap on the island opens it at once, in either mode.</summary>
    public void Click()
    {
        Cancel(ref _hoverTimer);
        Cancel(ref _closeTimer);
        Open();
    }

    /// <summary>A click somewhere outside ISLe8 closes an island the pointer has left (unless pinned).</summary>
    public void ClickedOutside()
    {
        if (WantsOutsideClicks) Close();
    }

    public void Close()
    {
        Cancel(ref _hoverTimer);
        Cancel(ref _closeTimer);
        Transition(IslandState.Pill);
    }

    /// <summary>Pinning keeps the island open; pinning it from the pill (the menu) opens it.</summary>
    public void SetPinned(bool pinned)
    {
        if (pinned == IsPinned) return;
        IsPinned = pinned;
        if (pinned) Click();
        else if (!IsHovering) SetHovering(false);
        else Changed?.Invoke();
    }

    /// <summary>
    /// The island's menu opened or closed. While it is open the island neither opens nor closes;
    /// once it closes with the pointer elsewhere, the island closes as if the pointer had just left.
    /// </summary>
    public void SetMenuOpen(bool open, bool pointerInside)
    {
        if (open == IsMenuOpen) return;
        IsMenuOpen = open;
        if (open)
        {
            Cancel(ref _hoverTimer);
            Cancel(ref _closeTimer);
            Changed?.Invoke();
        }
        else
        {
            SetHovering(pointerInside);
        }
    }

    /// <summary>
    /// Something is live (music playing): the pill widens at once. When it stops, the pill
    /// narrows only after <see cref="PillShrinkDelay"/>, so a song change doesn't make it twitch.
    /// </summary>
    public void SetLive(bool live)
    {
        if (live)
        {
            Cancel(ref _shrinkTimer);
            SetPill(PillKind.Compact);
        }
        else if (Pill == PillKind.Compact && _shrinkTimer is null)
        {
            _shrinkTimer = _clock.After(PillShrinkDelay, () =>
            {
                _shrinkTimer = null;
                SetPill(PillKind.Idle);
            });
        }
    }

    /// <summary>A tab from the dock. The island stays open and, in the wide and compact layouts, the same size.</summary>
    public void Select(IslandTab tab)
    {
        if (tab == Tab) return;
        var old = Tab;
        Tab = tab;
        TabChanged?.Invoke(old);
        Changed?.Invoke();
    }

    /// <summary>A two-finger swipe on the open island: the next (1) or previous (-1) tab, stopping at the ends.</summary>
    public void SelectAdjacent(int step)
    {
        var tabs = VisibleTabs;
        var index = IndexOf(tabs, Tab);
        if (State != IslandState.Expanded || index < 0 || index + step < 0 || index + step >= tabs.Count) return;
        Select(tabs[index + step]);
    }

    /// <summary>The dock's tabs were reordered or hidden in Settings; a hidden current tab gives way to the first.</summary>
    public void TabsChanged()
    {
        var tabs = VisibleTabs;
        if (IndexOf(tabs, Tab) < 0) Select(tabs[0]);
        else Changed?.Invoke();
    }

    private void Open()
    {
        if (State != IslandState.Expanded) ChooseTabForOpening();
        Transition(IslandState.Expanded);
    }

    /// <summary>The last tab if the island was open a moment ago, otherwise the first one.</summary>
    private void ChooseTabForOpening()
    {
        var tabs = VisibleTabs;
        if (_clock.Now - _closedAt > TabMemory || IndexOf(tabs, Tab) < 0) Select(tabs[0]);
    }

    private static int IndexOf(IReadOnlyList<IslandTab> tabs, IslandTab tab)
    {
        for (var i = 0; i < tabs.Count; i++)
        {
            if (tabs[i] == tab) return i;
        }
        return -1;
    }

    private void SetPill(PillKind pill)
    {
        if (pill == Pill) return;
        IsGrowing = pill == PillKind.Compact;
        Pill = pill;
        PillChanged?.Invoke();
        Changed?.Invoke();
    }

    private void Transition(IslandState state)
    {
        if (state == State) return;
        var old = State;
        if (old == IslandState.Expanded) _closedAt = _clock.Now;
        // Pill < peek < open, whichever pill: the bigger state springs open, the smaller one closes.
        IsGrowing = state > old;
        State = state;
        StateChanged?.Invoke(old);
        Changed?.Invoke();
    }

    private static void Cancel(ref IDisposable? timer)
    {
        timer?.Dispose();
        timer = null;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
