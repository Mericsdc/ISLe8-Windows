using System.Globalization;
using System.Text;

namespace ISLe8.Core.Calendar;

/// <summary>An event as read from a calendar feed: one VEVENT, perhaps repeating.</summary>
public sealed record IcsEvent
{
    public required string Uid { get; init; }
    public required string Title { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required TimeSpan Length { get; init; }
    public bool IsAllDay { get; init; }
    public string? Location { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
    public RecurrenceRule? Rule { get; init; }
    public IReadOnlyList<DateTimeOffset> Exceptions { get; init; } = [];
    /// <summary>Set on an instance that replaces one occurrence of a repeating event (RECURRENCE-ID).</summary>
    public DateTimeOffset? RecurrenceId { get; init; }
}

public enum Frequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>The part of RFC 5545's RRULE that calendars send for meetings: frequency, interval, an end, weekdays, a day of the month.</summary>
public sealed record RecurrenceRule(Frequency Frequency, int Interval, int? Count, DateTimeOffset? Until, IReadOnlyList<DayOfWeek> ByDay, int? ByMonthDay);

/// <summary>
/// Reads iCalendar feeds (RFC 5545): the subscription links Outlook, Google and iCloud give out. On
/// Windows they stand in for the Mac's EventKit (PORTING.md §3.6). Times with a TZID are read in
/// that zone, Windows or IANA names alike; dates alone are all-day events in local time.
/// </summary>
public static class IcsParser
{
    public static (string? Name, IReadOnlyList<IcsEvent> Events) Parse(string text)
    {
        var events = new List<IcsEvent>();
        string? name = null;
        Dictionary<string, (string Value, Dictionary<string, string> Parameters)>? current = null;
        List<string>? exceptions = null;
        foreach (var line in Unfold(text))
        {
            var (key, parameters, value) = Split(line);
            if (key == "BEGIN" && value == "VEVENT")
            {
                current = [];
                exceptions = [];
            }
            else if (key == "END" && value == "VEVENT" && current is not null)
            {
                if (Build(current, exceptions!) is { } built) events.Add(built);
                current = null;
            }
            else if (current is not null)
            {
                if (key == "EXDATE") exceptions!.AddRange(value.Split(',').Select(part => Join(parameters, part)));
                else current.TryAdd(key, (value, parameters));
            }
            else if (key == "X-WR-CALNAME")
            {
                name = Text(value);
            }
        }
        return (name, events);
    }

    /// <summary>A single event as an .ics file, for the default calendar app to add (quick add).</summary>
    public static string Write(string title, DateTimeOffset start, TimeSpan length, DateTimeOffset stamp)
    {
        static string Utc(DateTimeOffset time) => time.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var escaped = title.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
        return string.Join("\r\n",
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//LRXe8//ISLe8//EN",
            "BEGIN:VEVENT",
            $"UID:{Guid.NewGuid()}@isle8",
            $"DTSTAMP:{Utc(stamp)}",
            $"DTSTART:{Utc(start)}",
            $"DTEND:{Utc(start + length)}",
            $"SUMMARY:{escaped}",
            "END:VEVENT",
            "END:VCALENDAR",
            "");
    }

    private static IcsEvent? Build(Dictionary<string, (string Value, Dictionary<string, string> Parameters)> fields, List<string> exceptions)
    {
        if (!fields.TryGetValue("DTSTART", out var start)) return null;
        if (fields.TryGetValue("STATUS", out var status) && status.Value.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase)) return null;
        var allDay = IsDate(start.Value, start.Parameters);
        if (ParseTime(start.Value, start.Parameters) is not { } begin) return null;

        TimeSpan length;
        if (fields.TryGetValue("DTEND", out var end) && ParseTime(end.Value, end.Parameters) is { } finish) length = finish - begin;
        else if (fields.TryGetValue("DURATION", out var duration) && ParseDuration(duration.Value) is { } span) length = span;
        else length = allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
        if (length < TimeSpan.Zero) length = TimeSpan.Zero;

        DateTimeOffset? recurrenceId = fields.TryGetValue("RECURRENCE-ID", out var id) ? ParseTime(id.Value, id.Parameters) : null;
        return new IcsEvent
        {
            Uid = fields.TryGetValue("UID", out var uid) ? uid.Value : $"{begin:O}",
            Title = fields.TryGetValue("SUMMARY", out var summary) ? Text(summary.Value) : "",
            Start = begin,
            Length = length,
            IsAllDay = allDay,
            Location = fields.TryGetValue("LOCATION", out var location) && location.Value.Length > 0 ? Text(location.Value) : null,
            Description = fields.TryGetValue("DESCRIPTION", out var description) ? Text(description.Value) : null,
            Url = fields.TryGetValue("URL", out var url) ? url.Value : null,
            Rule = fields.TryGetValue("RRULE", out var rule) ? ParseRule(rule.Value, start.Parameters) : null,
            Exceptions = exceptions.Select(part => Split("EXDATE" + part)).Select(e => ParseTime(e.Value, e.Parameters)).OfType<DateTimeOffset>().ToList(),
            RecurrenceId = recurrenceId,
        };
    }

    /// <summary>Lines folded with a leading space or tab are joined back (RFC 5545 §3.1).</summary>
    private static IEnumerable<string> Unfold(string text)
    {
        var line = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                line.Append(raw, 1, raw.Length - 1);
                continue;
            }
            if (line.Length > 0) yield return line.ToString();
            line.Clear().Append(raw);
        }
        if (line.Length > 0) yield return line.ToString();
    }

    /// <summary>"DTSTART;TZID=Europe/Istanbul:20261029T140000" → key, parameters, value.</summary>
    private static (string Key, Dictionary<string, string> Parameters, string Value) Split(string line)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // The value starts after the first colon that is not inside a quoted parameter.
        var quoted = false;
        var colon = -1;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ':' && !quoted)
            {
                colon = i;
                break;
            }
        }
        if (colon < 0) return (line.ToUpperInvariant(), parameters, "");
        var head = line[..colon].Split(';');
        foreach (var part in head.Skip(1))
        {
            var equals = part.IndexOf('=');
            if (equals > 0) parameters[part[..equals]] = part[(equals + 1)..].Trim('"');
        }
        return (head[0].ToUpperInvariant(), parameters, line[(colon + 1)..]);
    }

    /// <summary>An EXDATE value back into "key;params:value" form, so it is read like DTSTART.</summary>
    private static string Join(Dictionary<string, string> parameters, string value) =>
        string.Concat(parameters.Select(p => $";{p.Key}={p.Value}")) + ":" + value;

    private static bool IsDate(string value, Dictionary<string, string> parameters) =>
        parameters.TryGetValue("VALUE", out var kind) ? kind.Equals("DATE", StringComparison.OrdinalIgnoreCase) : value.Length == 8;

    public static DateTimeOffset? ParseTime(string value, IReadOnlyDictionary<string, string> parameters)
    {
        value = value.Trim();
        if (IsDate(value, new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase)) && value.Length >= 8)
        {
            if (!DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
            return new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
        }
        var utc = value.EndsWith('Z');
        var digits = utc ? value[..^1] : value;
        if (!DateTime.TryParseExact(digits, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) return null;
        if (utc) return new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
        var zone = parameters.TryGetValue("TZID", out var id) ? Zone(id) : TimeZoneInfo.Local;
        return new DateTimeOffset(time, zone.GetUtcOffset(time));
    }

    /// <summary>A zone by Windows or IANA name; Outlook writes "Turkey Standard Time", Google "Europe/Istanbul".</summary>
    private static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim('"', '/'));
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    /// <summary>"PT1H30M", "P1D", "-PT15M".</summary>
    public static TimeSpan? ParseDuration(string value)
    {
        value = value.Trim();
        var negative = value.StartsWith('-');
        value = value.TrimStart('+', '-');
        if (!value.StartsWith('P')) return null;
        var total = TimeSpan.Zero;
        var number = 0;
        var inTime = false;
        foreach (var c in value[1..])
        {
            if (char.IsDigit(c)) number = number * 10 + (c - '0');
            else
            {
                switch (c)
                {
                    case 'T': inTime = true; break;
                    case 'W': total += TimeSpan.FromDays(7 * number); break;
                    case 'D': total += TimeSpan.FromDays(number); break;
                    case 'H' when inTime: total += TimeSpan.FromHours(number); break;
                    case 'M' when inTime: total += TimeSpan.FromMinutes(number); break;
                    case 'S' when inTime: total += TimeSpan.FromSeconds(number); break;
                    default: return null;
                }
                number = 0;
            }
        }
        return negative ? -total : total;
    }

    private static RecurrenceRule? ParseRule(string value, Dictionary<string, string> startParameters)
    {
        var parts = value.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1], StringComparer.OrdinalIgnoreCase);
        if (!parts.TryGetValue("FREQ", out var freq)) return null;
        Frequency? frequency = freq.ToUpperInvariant() switch
        {
            "DAILY" => Frequency.Daily,
            "WEEKLY" => Frequency.Weekly,
            "MONTHLY" => Frequency.Monthly,
            "YEARLY" => Frequency.Yearly,
            _ => null,
        };
        if (frequency is null) return null;
        var interval = parts.TryGetValue("INTERVAL", out var i) && int.TryParse(i, out var n) && n > 0 ? n : 1;
        int? count = parts.TryGetValue("COUNT", out var c) && int.TryParse(c, out var cn) ? cn : null;
        var until = parts.TryGetValue("UNTIL", out var u) ? ParseTime(u, startParameters) : null;
        var days = parts.TryGetValue("BYDAY", out var by)
            ? by.Split(',').Select(d => d.Length >= 2 ? Weekday(d[^2..]) : null).OfType<DayOfWeek>().ToList()
            : [];
        int? monthDay = parts.TryGetValue("BYMONTHDAY", out var md) && int.TryParse(md.Split(',')[0], out var mdn) ? mdn : null;
        return new RecurrenceRule(frequency.Value, interval, count, until, days, monthDay);
    }

    private static DayOfWeek? Weekday(string code) => code.ToUpperInvariant() switch
    {
        "MO" => DayOfWeek.Monday,
        "TU" => DayOfWeek.Tuesday,
        "WE" => DayOfWeek.Wednesday,
        "TH" => DayOfWeek.Thursday,
        "FR" => DayOfWeek.Friday,
        "SA" => DayOfWeek.Saturday,
        "SU" => DayOfWeek.Sunday,
        _ => null,
    };

    /// <summary>Unescapes TEXT values: "\," "\;" "\n" "\\".</summary>
    private static string Text(string value)
    {
        if (!value.Contains('\\')) return value;
        var text = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                text.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                text.Append(value[i]);
            }
        }
        return text.ToString();
    }
}

/// <summary>The occurrences of an event in a range: once, or as its rule repeats it.</summary>
public static class Recurrence
{
    /// <summary>Safety limit on how far a rule is walked.</summary>
    private const int MaxSteps = 5000;

    public static IEnumerable<DateTimeOffset> Occurrences(IcsEvent e, DateTimeOffset from, DateTimeOffset to)
    {
        if (e.Rule is not { } rule)
        {
            if (e.Start < to && e.Start + e.Length > from || e.Start >= from && e.Start < to) yield return e.Start;
            yield break;
        }
        var excluded = e.Exceptions.Select(x => x.UtcTicks).ToHashSet();
        var produced = 0;
        var local = e.Start.DateTime;
        var zoneOffset = e.Start.Offset;
        for (var step = 0; step < MaxSteps; step++)
        {
            foreach (var candidate in Period(rule, local, step))
            {
                if (candidate < local) continue;
                // Wall-clock time kept across daylight saving changes.
                var occurrence = new DateTimeOffset(candidate, OffsetFor(candidate, zoneOffset, e.Start));
                if (rule.Until is { } until && occurrence > until) yield break;
                if (rule.Count is { } count && produced >= count) yield break;
                produced++;
                if (occurrence >= to) yield break;
                if (excluded.Contains(occurrence.UtcTicks)) continue;
                if (occurrence + e.Length > from || occurrence >= from) yield return occurrence;
            }
        }
    }

    /// <summary>The candidates in the <paramref name="step"/>-th period after the start, in order.</summary>
    private static IEnumerable<DateTime> Period(RecurrenceRule rule, DateTime start, int step)
    {
        var n = step * rule.Interval;
        switch (rule.Frequency)
        {
            case Frequency.Daily:
                yield return start.AddDays(n);
                break;
            case Frequency.Weekly:
                if (rule.ByDay.Count == 0)
                {
                    yield return start.AddDays(7 * n);
                    break;
                }
                // The week (Monday first, RFC 5545's default WKST) the period falls in.
                var monday = start.AddDays(-(((int)start.DayOfWeek + 6) % 7)).AddDays(7 * n);
                foreach (var day in rule.ByDay.OrderBy(d => ((int)d + 6) % 7))
                {
                    yield return monday.AddDays(((int)day + 6) % 7);
                }
                break;
            case Frequency.Monthly:
                var month = new DateTime(start.Year, start.Month, 1, start.Hour, start.Minute, start.Second).AddMonths(n);
                var dayOfMonth = rule.ByMonthDay ?? start.Day;
                if (dayOfMonth <= DateTime.DaysInMonth(month.Year, month.Month)) yield return month.AddDays(dayOfMonth - 1);
                break;
            case Frequency.Yearly:
                var year = start.Year + n;
                if (start.Month != 2 || start.Day != 29 || DateTime.IsLeapYear(year)) yield return new DateTime(year, start.Month, start.Day, start.Hour, start.Minute, start.Second);
                break;
        }
    }

    private static TimeSpan OffsetFor(DateTime wallClock, TimeSpan startOffset, DateTimeOffset start) =>
        start.Offset == TimeZoneInfo.Local.GetUtcOffset(start.DateTime) ? TimeZoneInfo.Local.GetUtcOffset(wallClock) : startOffset;
}
