using ISLe8.Core.Geometry;
using ISLe8.Core.Motion;
using ISLe8.Core.Notifications;
using ISLe8.Core.Settings;
using ISLe8.Core.Status;

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

    /// <summary>At most this many notifications wait behind a banner; older ones give way.</summary>
    public const int NotificationQueueLimit = 5;

    /// <summary>A status banner's ears slide back under the pill this long before the island folds in.</summary>
    public static readonly TimeSpan StatusRetract = TimeSpan.FromMilliseconds(240);

    /// <summary>The fold that follows springs as the ears sprang out, for this long.</summary>
    public static readonly TimeSpan SoftCloseTime = TimeSpan.FromMilliseconds(700);

    private readonly AppSettings _settings;
    private readonly IIslandClock _clock;
    private IDisposable? _hoverTimer;
    private IDisposable? _closeTimer;
    private IDisposable? _shrinkTimer;
    private bool _mediaActive;
    private bool _timerActive;
    private bool _performanceActive;
    private bool _agentActive;
    private IDisposable? _alertTimer;
    private IDisposable? _pendingTimer;
    private readonly List<AppNotification> _pending = [];
    private DateTimeOffset _closedAt = DateTimeOffset.MinValue;
    private bool _softClose;

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

    /// <summary>A text field on the open island is being typed in; the pointer leaving doesn't close it, a click elsewhere does.</summary>
    public bool IsTyping { get; private set; }

    /// <summary>The session is locked: no banners, nothing waits for later (IslandModel.swift, setLocked).</summary>
    public bool IsLocked { get; private set; }

    /// <summary>Notifications waiting for the current banner to finish, shown as "+N" on it.</summary>
    public int PendingNotificationCount => _pending.Count;

    /// <summary>Whether the last change made the island bigger. Growing and shrinking use different springs.</summary>
    public bool IsGrowing { get; private set; } = true;

    /// <summary>A charging or cable banner is folding its ears back under the pill before it closes.</summary>
    public bool IsRetractingStatus { get; private set; }

    /// <summary>
    /// The spring for the change just made (IslandModel.swift, animation): plugging in or out springs
    /// out sideways with a visible bounce and back the same way; otherwise growing and shrinking differ.
    /// </summary>
    public SpringSpec Spring =>
        State == IslandState.Alert && Alert.IsStatus() ? SpringSpec.Status
        : _softClose ? SpringSpec.SoftClose
        : IsGrowing ? SpringSpec.Open
        : SpringSpec.Close;

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

    /// <summary><see cref="IsRetractingStatus"/> changed.</summary>
    public event Action? RetractingChanged;

    /// <summary><see cref="PendingNotificationCount"/> changed.</summary>
    public event Action? PendingNotificationsChanged;

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
            CancelAlert();
            if (!inside) EndAlert(AlertAfterHover);
            Changed?.Invoke();
            return;
        }
        Cancel(ref _hoverTimer);
        Cancel(ref _closeTimer);
        // A pointer that only passed over the pill held notifications back without moving the island.
        if (!inside) ShowPendingLater();

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
        else if (!IsMenuOpen && !((IsPinned || IsTyping) && State == IslandState.Expanded))
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
        CancelAlert();
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
            CancelAlert();
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
        CancelAlert();
        Transition(IslandState.Pill);
    }

    /// <summary>
    /// Shows a banner for <paramref name="duration"/>, then returns to the pill. Only an island at
    /// rest (or showing another banner) takes one, and not under the pointer: a banner never covers
    /// what you are doing on the island.
    /// </summary>
    public void ShowAlert(IslandAlert alert, TimeSpan duration)
    {
        if (!CanShowAlert) return;
        CancelAlert();
        var old = Alert;
        Alert = alert;
        if (State == IslandState.Alert)
        {
            // Swapping one banner for another can change its size without a state change.
            IsGrowing = old is null || IslandSizes.Alert(alert).Height >= IslandSizes.Alert(old).Height;
            AlertChanged?.Invoke();
            Changed?.Invoke();
        }
        else
        {
            Transition(IslandState.Alert);
        }
        EndAlert(duration);
    }

    /// <summary>
    /// Volume or brightness (IslandModel.swift, showHUD). Unlike other banners it also shows while
    /// the pointer rests on the island, since it answers a key just pressed. It never covers an open
    /// island or a notification.
    /// </summary>
    public void ShowHud(HudValue value)
    {
        if (IsLocked || State is IslandState.Expanded or IslandState.DropTarget || IsShowingNotification) return;
        Cancel(ref _hoverTimer);
        CancelAlert();
        var old = Alert;
        Alert = new HudAlert(value);
        if (State == IslandState.Alert)
        {
            IsGrowing = old is null || IslandSizes.Alert(Alert).Width >= IslandSizes.Alert(old).Width;
            AlertChanged?.Invoke();
            Changed?.Invoke();
        }
        else
        {
            Transition(IslandState.Alert);
        }
        EndAlert(AlertDurations.Hud);
    }

    /// <summary>
    /// Notifications queue up behind a banner that is already showing one, and wait while the island
    /// is open or under the pointer, instead of being dropped (IslandModel.swift, showNotification).
    /// </summary>
    public void ShowNotification(AppNotification notification)
    {
        if (IsLocked) return;
        if (CanShowAlert && !IsShowingNotification)
        {
            ShowAlert(new NotificationAlert(notification), NotificationDuration);
            return;
        }
        _pending.Add(notification);
        if (_pending.Count > NotificationQueueLimit) _pending.RemoveRange(0, _pending.Count - NotificationQueueLimit);
        PendingNotificationsChanged?.Invoke();
    }

    /// <summary>
    /// The session was locked or unlocked. Locking puts the island at rest and forgets what was
    /// waiting; the Mac's lock-screen island has no Windows counterpart (PORTING.md §3.18).
    /// </summary>
    public void SetLocked(bool locked)
    {
        if (locked == IsLocked) return;
        IsLocked = locked;
        Cancel(ref _hoverTimer);
        Cancel(ref _closeTimer);
        CancelAlert();
        IsPinned = false;
        if (_pending.Count > 0)
        {
            _pending.Clear();
            PendingNotificationsChanged?.Invoke();
        }
        if (locked) Transition(IslandState.Pill);
        Changed?.Invoke();
    }

    /// <summary>A banner's button did what it offered (Katıl): the banner goes at once.</summary>
    public void DismissAlert()
    {
        if (State != IslandState.Alert) return;
        CancelAlert();
        Transition(IslandState.Pill);
    }

    /// <summary>Only an island at rest (or showing another banner) takes one, and not under the pointer.</summary>
    private bool CanShowAlert => State is IslandState.Pill or IslandState.Alert && !IsHovering && !IsMenuOpen && !IsLocked;

    private bool IsShowingNotification => State == IslandState.Alert && Alert is NotificationAlert;

    private TimeSpan NotificationDuration => TimeSpan.FromSeconds(_settings.NotificationDuration);

    /// <summary>The next waiting notification, once the island is back at rest; after the change that let it, not inside it.</summary>
    private void ShowPendingLater()
    {
        if (_pending.Count == 0) return;
        Cancel(ref _pendingTimer);
        _pendingTimer = _clock.After(TimeSpan.Zero, () =>
        {
            _pendingTimer = null;
            if (_pending.Count == 0 || !CanShowAlert || IsShowingNotification) return;
            var next = _pending[0];
            _pending.RemoveAt(0);
            PendingNotificationsChanged?.Invoke();
            ShowAlert(new NotificationAlert(next), NotificationDuration);
        });
    }

    private void EndAlert(TimeSpan after) =>
        _alertTimer = _clock.After(after, () =>
        {
            _alertTimer = null;
            if (State != IslandState.Alert) return;
            if (!Alert.IsStatus())
            {
                Transition(IslandState.Pill);
                return;
            }
            // The ears slide back under the pill first, then the island folds in as it sprang out.
            SetRetracting(true);
            _alertTimer = _clock.After(StatusRetract, () =>
            {
                SetRetracting(false);
                if (State != IslandState.Alert)
                {
                    _alertTimer = null;
                    return;
                }
                _softClose = true;
                _alertTimer = _clock.After(SoftCloseTime, () =>
                {
                    _alertTimer = null;
                    _softClose = false;
                });
                Transition(IslandState.Pill);
            });
        });

    /// <summary>
    /// Whatever the banner was waiting for is called off; a status banner halfway through folding
    /// stops folding, and the next change springs as usual.
    /// </summary>
    private void CancelAlert()
    {
        Cancel(ref _alertTimer);
        _softClose = false;
        SetRetracting(false);
    }

    private void SetRetracting(bool retracting)
    {
        if (retracting == IsRetractingStatus) return;
        IsRetractingStatus = retracting;
        RetractingChanged?.Invoke();
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
    /// <summary>
    /// Typing started (a text field took the keyboard) or ended. Windows only: on the Mac the island
    /// closes as the pointer leaves even mid-word; here a note was lost that way.
    /// </summary>
    public void SetTyping(bool typing, bool pointerInside)
    {
        if (typing == IsTyping) return;
        IsTyping = typing;
        if (typing) Cancel(ref _closeTimer);
        else if (State == IslandState.Expanded)
        {
            SetHovering(pointerInside);
            return;
        }
        Changed?.Invoke();
    }

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

    /// <summary>
    /// An agent works or waits, or a script runs (<c>isle8 activity</c>): its ear comes before the
    /// music's (IslandModule.swift: agents 55, an approval 100, media 50).
    /// </summary>
    public void SetAgentLive(bool live)
    {
        _agentActive = live;
        UpdateActivity();
    }

    /// <summary>The tab the island should open on, when a module needs the user (an approval: the AI tab).</summary>
    public Func<IslandTab?>? PreferredTab { get; set; }

    /// <summary><c>isle8 open [tab]</c>: opens at once, on <paramref name="tab"/> or the usual one.</summary>
    public void Open(IslandTab? tab)
    {
        Cancel(ref _hoverTimer);
        Cancel(ref _closeTimer);
        CancelAlert();
        if (tab is { } chosen) Select(chosen);
        else if (State != IslandState.Expanded) ChooseTabForOpening();
        Transition(IslandState.Expanded);
    }

    /// <summary>A timer, stopwatch or Pomodoro is set (running or paused): the ear when music isn't.</summary>
    public void SetTimerLive(bool live)
    {
        _timerActive = live;
        UpdateActivity();
    }

    /// <summary>The performance pill is wanted now (Windows only): below an agent, above media and the timer.</summary>
    public void SetPerformanceLive(bool live)
    {
        _performanceActive = live;
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
        CancelAlert();
        if (State != IslandState.Expanded) ChooseTabForOpening();
        Transition(IslandState.Expanded);
    }

    /// <summary>A module that needs the user wins; otherwise the last tab if the island was open a moment ago, else the first.</summary>
    private void ChooseTabForOpening()
    {
        var tabs = VisibleTabs;
        if (PreferredTab?.Invoke() is { } preferred && IndexOf(tabs, preferred) >= 0)
        {
            Select(preferred);
            return;
        }
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

    /// <summary>
    /// The ear goes to the activity with the highest priority (IslandModule.swift): agents 55, media 50, timer 40.
    /// The performance pill, asked for in Settings, comes after agents.
    /// </summary>
    private void UpdateActivity()
    {
        var activity = _agentActive ? PillActivity.Agent : _performanceActive ? PillActivity.Performance : _mediaActive ? PillActivity.Media : _timerActive ? PillActivity.Timer : PillActivity.None;
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
        if (old == IslandState.Expanded)
        {
            _closedAt = _clock.Now;
            IsTyping = false;
        }
        // Pill < peek < open, whichever pill: the bigger state springs open, the smaller one closes.
        IsGrowing = state > old;
        State = state;
        StateChanged?.Invoke(old);
        Changed?.Invoke();
        if (state == IslandState.Pill) ShowPendingLater();
    }

    private static void Cancel(ref IDisposable? timer)
    {
        timer?.Dispose();
        timer = null;
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
