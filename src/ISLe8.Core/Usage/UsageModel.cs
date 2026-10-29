namespace ISLe8.Core.Usage;

// Ported from the Mac's Usage module, itself from LimitBar LRX (based on dancik/limitbar, MIT License;
// the license ships as LimitBar-LICENSE.txt). The vendor marks come from simple-icons (CC0).

/// <summary>A service whose usage limits are shown, asked only through its own official CLI (UsageModel.swift, UsageVendor).</summary>
public enum UsageVendor
{
    Codex,
    Claude,
    Gemini,
}

public static class UsageVendors
{
    public static IReadOnlyList<UsageVendor> All { get; } = [UsageVendor.Codex, UsageVendor.Claude, UsageVendor.Gemini];

    public static string Id(UsageVendor vendor) => vendor switch
    {
        UsageVendor.Codex => "codex",
        UsageVendor.Claude => "claude",
        _ => "gemini",
    };

    public static UsageVendor? Parse(string? id) => All.Cast<UsageVendor?>().FirstOrDefault(vendor => Id(vendor!.Value) == id);

    public static string DisplayName(UsageVendor vendor) => vendor switch
    {
        UsageVendor.Codex => "Codex",
        UsageVendor.Claude => "Claude",
        _ => "Gemini",
    };

    /// <summary>The CLI that is asked, and the one the sign-in hint names.</summary>
    public static string Cli(UsageVendor vendor) => vendor switch
    {
        UsageVendor.Codex => "codex",
        UsageVendor.Claude => "claude",
        _ => "agy",
    };

    /// <summary>The product the limits belong to.</summary>
    public static string Product(UsageVendor vendor) => vendor switch
    {
        UsageVendor.Codex => "ChatGPT",
        UsageVendor.Claude => "Claude Code",
        _ => "Antigravity",
    };
}

/// <summary>Why a vendor shows no numbers; <see cref="Ok"/> = the CLI answered.</summary>
public enum UsageState
{
    Ok,
    /// <summary>CLI not found.</summary>
    Missing,
    /// <summary>Found, but in a folder others can write to: never run.</summary>
    Untrusted,
    /// <summary>The CLI says nobody is signed in.</summary>
    SignedOut,
    /// <summary>It ran and gave no limits (timeout, error, changed format). Previous numbers are kept.</summary>
    Failed,
    /// <summary>Signed in, but the account has no usage limits (paid by use): Windows-only, the Mac reads it as a failure.</summary>
    NoLimits,
}

/// <summary>Why a read failed: one of our own fixed texts, chosen by the app in the user's language. Never CLI output.</summary>
public enum UsageError
{
    None,
    /// <summary>"Kullanım verisi bekleniyor."</summary>
    Waiting,
    Timeout,
    /// <summary>The CLI answered something else; <see cref="UsageSnapshot.ErrorCode"/> is its exit code.</summary>
    NoData,
    /// <summary>Claude answered without the quota lines (its version may have changed).</summary>
    NoQuotaLines,
    /// <summary>Antigravity answered without a Gemini group.</summary>
    NoGemini,
    /// <summary>Codex's app-server failed; the last local reading is shown.</summary>
    ServerFallback,
    Untrusted,
}

/// <summary>Where a snapshot's numbers came from.</summary>
public enum UsageSource
{
    Live,
    /// <summary>Codex wrote them into its own session log.</summary>
    LocalSession,
}

public enum UsageWindowKind
{
    FiveHour,
    Week,
    /// <summary>Claude's weekly limit for one model family ("Opus").</summary>
    ModelWeek,
    /// <summary>A Codex window of another length.</summary>
    Minutes,
    /// <summary>A Codex window that gives no length.</summary>
    Quota,
    /// <summary>An Antigravity model group, named by Google.</summary>
    Group,
}

/// <summary>One limit window as the vendor reports it (UsageModel.swift, UsageWindow).</summary>
public sealed record UsageWindow
{
    public UsageWindowKind Kind { get; init; }

    /// <summary>The model for <see cref="UsageWindowKind.ModelWeek"/>, the group's name for <see cref="UsageWindowKind.Group"/>.</summary>
    public string Name { get; init; } = "";

    /// <summary>The length for <see cref="UsageWindowKind.Minutes"/>.</summary>
    public int Minutes { get; init; }

    /// <summary>Weekly rather than 5-hour, for the small tag.</summary>
    public bool IsWeekly { get; init; }

    /// <summary>Percent used, 0–100; null = the vendor gave none, which is not 0 %.</summary>
    public double? Used { get; init; }

    public DateTimeOffset? ResetsAt { get; init; }

    public string Id => $"{Kind}:{Name}:{Minutes}|{(IsWeekly ? "w" : "5h")}";

    public double? Remaining => Used is { } used ? Math.Max(0, 100 - used) : null;

    /// <summary>The reset time has passed since it was read: the old percent is obsolete, not "0 %".</summary>
    public bool ResetPassed(DateTimeOffset now) => ResetsAt is { } reset && reset < now;

    /// <summary>At or above 90 % and still current.</summary>
    public bool IsHot(DateTimeOffset now) => !ResetPassed(now) && (Used ?? 0) >= 90;

    /// <summary>Rounded, but 99.6 % must never read as 100 %.</summary>
    public static string PercentText(double? value)
    {
        if (value is not { } percent) return "—";
        var rounded = Math.Round(percent, MidpointRounding.AwayFromZero);
        return ((int)(rounded >= 100 && percent < 100 ? 99 : rounded)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed record UsageSnapshot
{
    public UsageVendor Vendor { get; init; }
    public UsageState State { get; init; } = UsageState.Ok;

    /// <summary>From the CLI's own answer: "Max", "ChatGPT Plus". Nothing else of the account is kept.</summary>
    public string Plan { get; init; } = "";

    /// <summary>The window to show when there is room for one.</summary>
    public UsageWindow? Bar { get; init; }

    /// <summary>Every window, <see cref="Bar"/> first.</summary>
    public List<UsageWindow> Windows { get; init; } = [];

    /// <summary>Codex's free limit resets still available, and when the first of them expires.</summary>
    public int ResetCredits { get; init; }
    public DateTimeOffset? ResetCreditsExpire { get; init; }

    /// <summary>When the vendor's numbers were taken, not when they were looked at.</summary>
    public DateTimeOffset? MeasuredAt { get; init; }

    public UsageError Error { get; init; }
    public int ErrorCode { get; init; }
    public UsageSource? Source { get; init; }

    /// <summary>A window other than <see cref="Bar"/> is at 90 % or more.</summary>
    public bool OtherHot(DateTimeOffset now) => Windows.Any(window => window != Bar && window.IsHot(now));

    public static UsageSnapshot Status(UsageVendor vendor, UsageState state, UsageError error = UsageError.None, int code = 0) =>
        new() { Vendor = vendor, State = state, Error = error, ErrorCode = code };

    /// <summary>Structural equality for the lists too, so an unchanged reading is not published again.</summary>
    public bool SameAs(UsageSnapshot? other) =>
        other is not null && this with { Windows = NoWindows } == other with { Windows = NoWindows } && Windows.SequenceEqual(other.Windows);

    /// <summary>One shared list: a record compares its lists by reference.</summary>
    private static readonly List<UsageWindow> NoWindows = [];
}

/// <summary>How often each vendor is asked, and how failures back off (UsagePolling.swift).</summary>
public static class UsagePolicy
{
    /// <summary>
    /// With the island closed. Open, Codex and Antigravity are asked every minute instead (outside
    /// saver mode); Claude never more often than every 20 minutes.
    /// </summary>
    public static TimeSpan RemoteInterval(UsageVendor vendor, bool economy, bool visible) => vendor switch
    {
        UsageVendor.Claude => TimeSpan.FromMinutes(20),
        _ when visible && !economy => TimeSpan.FromMinutes(1),
        _ => TimeSpan.FromMinutes(economy ? 15 : 5),
    };

    /// <summary>1, 2, 4 … up to 30 minutes after consecutive failures.</summary>
    public static TimeSpan RetryDelay(int failures) =>
        TimeSpan.FromSeconds(Math.Min(30 * 60, 60 * Math.Pow(2, Math.Max(0, Math.Min(5, failures - 1)))));

    /// <summary>A CLI that isn't there, can't be trusted or has nobody signed in is checked again this often.</summary>
    public static readonly TimeSpan RecheckMissing = TimeSpan.FromMinutes(5);

    /// <summary>A manual refresh asks live at most this often.</summary>
    public static readonly TimeSpan ManualGap = TimeSpan.FromSeconds(30);
}

/// <summary>
/// The 90 % warning (UsageModule.swift, warnAboutHotWindows): once per window each time it crosses,
/// not again until it drops below and crosses again; a reading from the cache counts as warned.
/// </summary>
public sealed class UsageWarnings
{
    private readonly HashSet<string> _hot = [];

    public IReadOnlyList<UsageWindow> Read(UsageSnapshot snapshot, DateTimeOffset now, bool fromCache = false)
    {
        var prefix = $"{UsageVendors.Id(snapshot.Vendor)}|";
        if (snapshot.State == UsageState.Ok)
        {
            // A window that is gone can warn again once it comes back.
            var current = snapshot.Windows.Select(window => prefix + window.Id).ToHashSet();
            _hot.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal) && !current.Contains(key));
        }
        var warn = new List<UsageWindow>();
        foreach (var window in snapshot.Windows)
        {
            var key = prefix + window.Id;
            if (window.IsHot(now))
            {
                if (_hot.Add(key) && !fromCache) warn.Add(window);
            }
            else
            {
                _hot.Remove(key);
            }
        }
        return warn;
    }

    public void Clear() => _hot.Clear();
}
