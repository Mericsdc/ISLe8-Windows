using ISLe8.Core.Geometry;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Island;

public enum IslandState
{
    /// <summary>At rest: the idle pill, or the wider one while something is live.</summary>
    Pill,
    /// <summary>Click mode only: the pointer rests on the pill, which swells a little to say it can be clicked.</summary>
    Peek,
    /// <summary>A short-lived banner (<see cref="IslandModel.Alert"/>), for example a new song.</summary>
    Alert,
    /// <summary>The full island.</summary>
    Expanded,
    /// <summary>Files are being dragged onto the island: two zones, the shelf and sharing.</summary>
    DropTarget,
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

    /// <summary>
    /// After a pause the wide pill stays this long, so a quick resume (or a player reporting "paused"
    /// between tracks) doesn't make it flicker (MediaModule.swift).
    /// </summary>
    public static readonly TimeSpan PillShrinkDelay = TimeSpan.FromSeconds(8);

    /// <summary>A banner the pointer rested on goes this long after the pointer leaves.</summary>
    public static readonly TimeSpan AlertAfterHover = TimeSpan.FromSeconds(1.2);

    /// <summary>Reopening within this time returns to the tab you left; later it starts at the first tab.</summary>
    public static readonly TimeSpan TabMemory = TimeSpan.FromSeconds(15);

    private readonly AppSettings _settings;
    private readonly IIslandClock _clock;
    private IDisposable? _hoverTimer;
    private IDisposable? _closeTimer;
    private IDisposable? _shrinkTimer;
    private bool _mediaActive;
    private bool _timerActive;
    private IDisposable? _alertTimer;
    private DateTimeOffset _closedAt = DateTimeOffset.MinValue;

    public IslandModel(AppSettings settings, IIslandClock clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public IslandState State { get; private set; } = IslandState.Pill;

    /// <summary>The pill the island shows, or will show, at rest.</summary>
    public PillKind Pill { get; private set; } = PillKind.Idle;

    /// <summary>What the wide pill's ears show; the last one while it narrows back.</summary>
    public PillActivity Activity { get; private set; } = PillActivity.None;

    /// <summary>The open island's tab, or the one it will open on.</summary>
    public IslandTab Tab { get; private set; } = IslandTab.Home;

    /// <summary>The banner shown in <see cref="IslandState.Alert"/>, or last shown.</summary>
    public IslandAlert? Alert { get; private set; }

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

    /// <summary>One banner took another's place without the state changing.</summary>
    public event Action? AlertChanged;

    /// <summary>Anything changed: state, pill, pointer, pin or menu.</summary>
    public event Action? Changed;

    /// <summary>
    /// The pointer came onto the island (true) or left it (false). The app checks the real
    /// cursor position first; the system can report an exit that didn't happen.
    /// </summary>
    public void SetHovering(bool inside, bool touch = false)
    {
        IsHovering = inside;
        // A file drag decides on its own when the drop target closes.
        if (State == IslandState.DropTarget) return;
        // A banner stays while the pointer rests on it, so there is time to read it; a click opens the island.
        if (State == IslandState.Alert)
        {
            Cancel(ref _alertTimer);
            if (!inside) EndAlert(AlertAfterHover);
            Changed?.Invoke();
            return;
        }
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

    /// <summary>
    /// A shortcut for one tab (Ctrl+Alt+V for the clipboard): opens the island on it at once, or
    /// closes it when it is already open there.
    /// </summary>
    public void Toggle(IslandTab tab)
    {
        if (State == IslandState.Expanded && Tab == tab)
        {
            Close();
            return;
        }
        Cancel(ref _hoverTimer);
        Cancel(ref _closeTimer);
        Cancel(ref _alertTimer);
        Select(tab);
        Transition(IslandState.Expanded);
    }

    /// <summary>
    /// Files are being dragged onto the island (true) or away again (false). On, the drop target opens
    /// from any state; off, it returns to the pill (IslandModel.swift, setFileDragNearby).
    /// </summary>
    public void SetFileDragNearby(bool near)
    {
        if (near)
        {
            if (State == IslandState.DropTarget) return;
            Cancel(ref _hoverTimer);
            Cancel(ref _closeTimer);
            Cancel(ref _alertTimer);
            Transition(IslandState.DropTarget);
        }
        else if (State == IslandState.DropTarget)
        {
            Transition(IslandState.Pill);
        }
    }

    /// <summary>Files landed on the shelf zone: show them.</summary>
    public void ShowShelfAfterDrop()
    {
        Select(IslandTab.Shelf);
        Transition(IslandState.Expanded);
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
        Cancel(ref _alertTimer);
        Transition(IslandState.Pill);
    }

    /// <summary>
    /// Shows a banner for <paramref name="duration"/>, then returns to the pill. Only an island at
    /// rest (or showing another banner) takes one, and not under the pointer: a banner never covers
    /// what you are doing on the island.
    /// </summary>
    public void ShowAlert(IslandAlert alert, TimeSpan duration)
    {
        if (State is not (IslandState.Pill or IslandState.Alert) || IsHovering || IsMenuOpen) return;
        Cancel(ref _alertTimer);
        Alert = alert;
        if (State == IslandState.Alert)
        {
            AlertChanged?.Invoke();
            Changed?.Invoke();
        }
        else
        {
            Transition(IslandState.Alert);
        }
        EndAlert(duration);
    }

    /// <summary>A banner's button did what it offered (Katıl): the banner goes at once.</summary>
    public void DismissAlert()
    {
        if (State != IslandState.Alert) return;
        Cancel(ref _alertTimer);
        Transition(IslandState.Pill);
    }

    private void EndAlert(TimeSpan after) =>
        _alertTimer = _clock.After(after, () =>
        {
            _alertTimer = null;
            if (State == IslandState.Alert) Transition(IslandState.Pill);
        });

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
    /// Music playing: media takes the ear and the pill widens at once. When it stops, media keeps the
    /// ear for <see cref="PillShrinkDelay"/> (MediaModule.swift), so a song change doesn't make the
    /// pill twitch and a timer underneath doesn't flash in.
    /// </summary>
    public void SetLive(bool playing)
    {
        if (playing)
        {
            Cancel(ref _shrinkTimer);
            _mediaActive = true;
            UpdateActivity();
        }
        else if (_mediaActive && _shrinkTimer is null)
        {
            _shrinkTimer = _clock.After(PillShrinkDelay, () =>
            {
                _shrinkTimer = null;
                _mediaActive = false;
                UpdateActivity();
            });
        }
    }

    /// <summary>A timer, stopwatch or Pomodoro is set (running or paused): the ear when music isn't.</summary>
    public void SetTimerLive(bool live)
    {
        _timerActive = live;
        UpdateActivity();
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
        Cancel(ref _alertTimer);
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

    /// <summary>The ear goes to the activity with the highest priority (IslandModule.swift): media 50, timer 40.</summary>
    private void UpdateActivity()
    {
        var activity = _mediaActive ? PillActivity.Media : _timerActive ? PillActivity.Timer : PillActivity.None;
        var pill = activity == PillActivity.None ? PillKind.Idle : PillKind.Compact;
        if (activity == Activity && pill == Pill) return;
        if (pill != Pill) IsGrowing = pill == PillKind.Compact;
        // Narrowing keeps the last ear's content while it fades out.
        if (activity != PillActivity.None) Activity = activity;
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
