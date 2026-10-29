using System.Globalization;
using System.Text.RegularExpressions;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.Island;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Calendar;

/// <summary>One event on a day, ready to show (CalendarModule.swift, DayEvent and UpcomingItem).</summary>
public sealed record CalendarEntry
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset End { get; init; }
    public bool IsAllDay { get; init; }
    public required string ColorHex { get; init; }
    /// <summary>Where it happens: the meeting service ("Google Meet"), else the location, else the calendar.</summary>
    public string? Detail { get; init; }
    public string? MeetingLink { get; init; }
}

/// <summary>
/// The calendars from Settings › Widgets: ICS subscription links, read-only (PORTING.md §3.6). They
/// are fetched at start, when Home is shown and the copy is over 15 minutes old, and every 30 minutes
/// while there are any. Between those the service only waits for the next moment that matters
/// (a heads-up, an event ending, midnight), as the Mac's CalendarModule does.
/// </summary>
public sealed partial class CalendarService(AppSettings settings, HttpClient http, IIslandClock clock)
{
    public static readonly TimeSpan HeadsUp = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(30);

    /// <summary>The Mac's calendar colours, one per feed in turn (ICS has none of its own).</summary>
    private static readonly string[] Colors = ["#1BADF8", "#63DA38", "#FF9500", "#CC73E1", "#FF2968", "#FFCC00", "#A2845E"];

    private static readonly string[] MeetingHosts = ["zoom.us", "meet.google.com", "teams.microsoft.com", "teams.live.com", "webex.com", "facetime.apple.com"];

    private List<(IcsEvent Event, string Color, string? Calendar)> _events = [];
    private readonly HashSet<string> _announced = [];
    private IDisposable? _wake;
    private IDisposable? _refresh;
    private DateTimeOffset? _fetchedAt;
    private int _request;

    /// <summary>Calendars are connected: the Mac's "access granted".</summary>
    public bool HasFeeds => settings.CalendarFeeds.Count > 0;

    /// <summary>The last fetch of at least one feed failed.</summary>
    public bool Failed { get; private set; }

    /// <summary>Goes up on every change, so a view knows to read again.</summary>
    public int Revision { get; private set; }

    public IReadOnlyList<CalendarEntry> Upcoming { get; private set; } = [];

    public event Action? Changed;

    /// <summary>A meeting starts in five minutes (Settings › Widgets › heads-up).</summary>
    public event Action<CalendarEntry>? MeetingSoon;

    public Task Start() => Refresh();

    public Task RefreshIfStale() =>
        HasFeeds && (_fetchedAt is not { } at || clock.Now - at >= MaxAge) ? Refresh() : Task.CompletedTask;

    public async Task Refresh()
    {
        var request = ++_request;
        _refresh?.Dispose();
        _refresh = null;
        var feeds = settings.CalendarFeeds.ToList();
        var events = new List<(IcsEvent, string, string?)>();
        var failed = false;
        for (var i = 0; i < feeds.Count; i++)
        {
            try
            {
                var (name, parsed) = IcsParser.Parse(await http.GetStringAsync(Address(feeds[i])));
                events.AddRange(parsed.Select(e => (e, Colors[i % Colors.Length], name)));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or UriFormatException or InvalidOperationException)
            {
                Log.Error(e, "calendar feed");
                failed = true;
            }
        }
        if (request != _request) return;
        _events = events;
        Failed = failed;
        _fetchedAt = clock.Now;
        if (feeds.Count > 0) _refresh = clock.After(RefreshEvery, () => _ = Refresh());
        Recompute();
    }

    /// <summary>Works out what's next and sleeps until the next moment that matters.</summary>
    public void Recompute()
    {
        var now = clock.Now;
        Upcoming = UpcomingAt(now);
        Revision++;
        ScheduleWake(now);
        Changed?.Invoke();
    }

    // MARK: Reading

    /// <summary>Every event in the range, in order: all-day first, then by start.</summary>
    public IReadOnlyList<CalendarEntry> Between(DateTimeOffset from, DateTimeOffset to)
    {
        var overrides = _events.Where(e => e.Event.RecurrenceId is not null)
            .Select(e => (e.Event.Uid, e.Event.RecurrenceId!.Value.UtcTicks)).ToHashSet();
        var entries = new List<CalendarEntry>();
        foreach (var (e, color, calendar) in _events)
        {
            foreach (var start in Recurrence.Occurrences(e, from, to))
            {
                // A moved or changed occurrence comes as its own VEVENT; the rule's copy gives way.
                if (e.RecurrenceId is null && e.Rule is not null && overrides.Contains((e.Uid, start.UtcTicks))) continue;
                var link = MeetingLink(e);
                entries.Add(new CalendarEntry
                {
                    Id = $"{e.Uid}@{start.UtcTicks}",
                    Title = e.Title,
                    Start = start,
                    End = start + e.Length,
                    IsAllDay = e.IsAllDay,
                    ColorHex = color,
                    Detail = (link is null ? null : ServiceName(link)) ?? e.Location ?? calendar,
                    MeetingLink = link,
                });
            }
        }
        return entries.OrderBy(e => e.IsAllDay ? 0 : 1).ThenBy(e => e.Start).ToList();
    }

    public IReadOnlyList<CalendarEntry> On(DateOnly day)
    {
        var start = LocalMidnight(day);
        return Between(start, LocalMidnight(day.AddDays(1)));
    }

    /// <summary>Each day in the range that has an event, with the first event's colour; a multi-day event marks every day.</summary>
    public IReadOnlyDictionary<DateOnly, string> EventDays(DateOnly first, DateOnly last)
    {
        var from = LocalMidnight(first);
        var to = LocalMidnight(last.AddDays(1));
        var days = new Dictionary<DateOnly, string>();
        foreach (var entry in Between(from, to).OrderBy(e => e.Start))
        {
            var day = DateOnly.FromDateTime((entry.Start < from ? from : entry.Start).LocalDateTime);
            var end = entry.End <= entry.Start ? entry.Start.AddTicks(1) : entry.End;
            while (LocalMidnight(day) < end && day <= last)
            {
                days.TryAdd(day, entry.ColorHex);
                day = day.AddDays(1);
            }
        }
        return days;
    }

    /// <summary>Up next: today and tomorrow, at most four; an all-day event only on its day.</summary>
    public IReadOnlyList<CalendarEntry> UpcomingAt(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        return Between(now, LocalMidnight(today.AddDays(2)))
            .Where(e => e.End > now)
            .Where(e => !e.IsAllDay || DateOnly.FromDateTime(e.Start.LocalDateTime) == today || e.Start < now)
            .Take(4)
            .ToList();
    }

    // MARK: Quick add

    /// <summary>
    /// "14:30 Toplantı" or just "Toplantı": without a time, the next full hour today or 09:00 on
    /// another day; an hour long. Null when there is no title.
    /// </summary>
    public static (string Title, DateTimeOffset Start)? ParseQuickAdd(string text, DateOnly day, DateTimeOffset now)
    {
        var title = text.Trim();
        DateTime? start = null;
        if (QuickTime().Match(title) is { Success: true } match
            && int.TryParse(match.Groups[1].Value, out var hour) && int.TryParse(match.Groups[2].Value, out var minute) && hour < 24 && minute < 60)
        {
            start = day.ToDateTime(new TimeOnly(hour, minute));
            title = title[match.Length..].Trim();
        }
        if (title.Length == 0) return null;
        var local = now.LocalDateTime;
        start ??= day == DateOnly.FromDateTime(local)
            ? new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0).AddHours(1)
            : day.ToDateTime(new TimeOnly(9, 0));
        return (title, new DateTimeOffset(start.Value, TimeZoneInfo.Local.GetUtcOffset(start.Value)));
    }

    [GeneratedRegex(@"^(\d{1,2})[:.](\d{2})\s+")]
    private static partial Regex QuickTime();

    // MARK: Meetings

    public static string? MeetingLink(IcsEvent e)
    {
        if (e.Url is { } url && IsMeeting(url)) return url;
        var text = string.Join("\n", new[] { e.Location, e.Description }.Where(t => !string.IsNullOrEmpty(t)));
        return Links().Matches(text).Select(m => m.Value.TrimEnd('.', ',', ')', '>', ']')).FirstOrDefault(IsMeeting);
    }

    public static string? ServiceName(string link)
    {
        var host = Host(link);
        if (host is null) return null;
        if (host.Contains("zoom")) return "Zoom";
        if (host.Contains("meet.google")) return "Google Meet";
        if (host.Contains("teams")) return "Microsoft Teams";
        if (host.Contains("webex")) return "Webex";
        if (host.Contains("facetime")) return "FaceTime";
        return null;
    }

    private static bool IsMeeting(string link) => Host(link) is { } host && MeetingHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));

    private static string? Host(string link) => Uri.TryCreate(link, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : null;

    [GeneratedRegex(@"https?://[^\s""<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Links();

    /// <summary>Sleeps until the next heads-up, the end of the current event, or midnight.</summary>
    private void ScheduleWake(DateTimeOffset now)
    {
        _wake?.Dispose();
        if (!HasFeeds)
        {
            _wake = null;
            return;
        }
        var moments = new List<DateTimeOffset> { LocalMidnight(DateOnly.FromDateTime(now.LocalDateTime).AddDays(1)) };
        foreach (var e in Upcoming.Where(e => !e.IsAllDay))
        {
            moments.Add(e.End);
            if (settings.MeetingHeadsUp && !_announced.Contains(e.Id)) moments.Add(e.Start - HeadsUp);
        }
        var next = moments.Select(m => m < now ? now : m).Min();
        _wake = clock.After(next - now + TimeSpan.FromSeconds(0.5), () =>
        {
            _wake = null;
            AnnounceDueMeetings();
            Recompute();
        });
    }

    private void AnnounceDueMeetings()
    {
        if (!settings.MeetingHeadsUp) return;
        var now = clock.Now;
        foreach (var e in Upcoming.Where(e => !e.IsAllDay && !_announced.Contains(e.Id)))
        {
            var until = e.Start - now;
            if (until <= HeadsUp && until > TimeSpan.FromMinutes(-1))
            {
                _announced.Add(e.Id);
                MeetingSoon?.Invoke(e);
            }
        }
    }

    private static Uri Address(string feed) =>
        new(feed.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase) ? "https://" + feed["webcal://".Length..] : feed);

    public static DateTimeOffset LocalMidnight(DateOnly day)
    {
        var midnight = day.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, TimeZoneInfo.Local.GetUtcOffset(midnight));
    }
}

/// <summary>The month grid's and week strip's days, starting on the culture's first weekday.</summary>
public static class CalendarGrid
{
    /// <summary>Whole weeks covering the month: five or six rows.</summary>
    public static IReadOnlyList<DateOnly> MonthDays(DateOnly month, DayOfWeek firstDay)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var start = WeekStart(first, firstDay);
        var last = WeekStart(first.AddMonths(1).AddDays(-1), firstDay).AddDays(6);
        var days = new List<DateOnly>();
        for (var day = start; day <= last; day = day.AddDays(1)) days.Add(day);
        return days;
    }

    public static DateOnly WeekStart(DateOnly day, DayOfWeek firstDay) => day.AddDays(-(((int)day.DayOfWeek - (int)firstDay + 7) % 7));

    /// <summary>The weekday initials in order from the first day of the week.</summary>
    public static IReadOnlyList<string> WeekdaySymbols(CultureInfo culture)
    {
        var names = culture.DateTimeFormat.AbbreviatedDayNames;
        var first = (int)culture.DateTimeFormat.FirstDayOfWeek;
        return Enumerable.Range(0, 7).Select(i => names[(first + i) % 7]).ToList();
    }
}
