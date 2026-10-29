using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLe8.Core.Usage;

/// <summary>
/// The vendors' answers, as numbers (UsageFetchers.swift). A CLI's own text is only matched against,
/// never stored or shown: what goes into a snapshot is percentages, times, a plan name and our own codes.
/// </summary>
public static partial class UsageParse
{
    private static readonly string[] SignedOutPhrases =
    [
        "not logged in", "not signed in", "please log in", "please sign in", "login required",
        "unauthenticated", "unauthorized", "no credentials", "not authenticated", "authentication required",
    ];

    public static bool SignedOutText(string text)
    {
        var lower = text.ToLowerInvariant();
        return SignedOutPhrases.Any(lower.Contains);
    }

    /// <summary>Seconds (or milliseconds) since 1970, or an ISO 8601 string.</summary>
    public static DateTimeOffset? Date(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number))
        {
            var seconds = number > 1e12 ? number / 1000 : number;
            return seconds is >= 0 and < 32_503_680_000 ? DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)) : null;
        }
        if (value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            return date;
        }
        return null;
    }

    private static double? Finite(JsonElement? value) =>
        value is { ValueKind: JsonValueKind.Number } number && number.TryGetDouble(out var result) && double.IsFinite(result) ? result : null;

    private static double? Percent(JsonElement? value) => Finite(value) is { } percent and >= 0 and <= 100 ? percent : null;

    private static JsonElement? Get(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    private static string? Text(JsonElement element, string name) => Get(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // MARK: Claude: `claude -p /usage`

    /// <summary>
    /// The quota lines of /usage's text ("Current session: 12% used · resets Oct 21 at 9:10pm
    /// (Europe/Istanbul)"), each weekly line per model included. An account paid by use gets a cost
    /// summary instead ("Total cost: …"): it has no limits to show.
    /// </summary>
    public static UsageSnapshot ClaudeUsage(string text, DateTimeOffset now)
    {
        if (SignedOutText(text)) return UsageSnapshot.Status(UsageVendor.Claude, UsageState.SignedOut);
        UsageWindow? Line(string title, UsageWindowKind kind, bool weekly, string name = "")
        {
            var match = Regex.Match(text, Regex.Escape(title) + @":\s*(\d+(?:\.\d+)?)%\s*used(?:\s*·\s*resets\s+([^(\n]+?)\s*\(([^)]+)\))?");
            if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var used) || used is < 0 or > 100) return null;
            var reset = match.Groups[2].Success ? ClaudeReset(match.Groups[2].Value, match.Groups[3].Value, now) : null;
            return new UsageWindow { Kind = kind, Name = name, IsWeekly = weekly, Used = used, ResetsAt = reset };
        }
        var five = Line("Current session", UsageWindowKind.FiveHour, weekly: false);
        var week = Line("Current week (all models)", UsageWindowKind.Week, weekly: true);
        var extra = new List<UsageWindow>();
        foreach (Match match in ModelWeekPattern().Matches(text))
        {
            var model = match.Groups[1].Value;
            if (model != "all models" && Line($"Current week ({model})", UsageWindowKind.ModelWeek, weekly: true, model) is { } window) extra.Add(window);
        }
        if (five is null && week is null)
        {
            return text.Contains("Total cost:", StringComparison.Ordinal)
                ? UsageSnapshot.Status(UsageVendor.Claude, UsageState.NoLimits)
                : UsageSnapshot.Status(UsageVendor.Claude, UsageState.Failed, UsageError.NoQuotaLines);
        }
        return new UsageSnapshot
        {
            Vendor = UsageVendor.Claude,
            Bar = five ?? week,
            Windows = [.. new[] { five, week }.OfType<UsageWindow>(), .. extra],
            MeasuredAt = now,
            Source = UsageSource.Live,
        };
    }

    [GeneratedRegex(@"Current week \(([^)]+)\)")]
    private static partial Regex ModelWeekPattern();

    /// <summary>"Oct 21 at 9:10pm" / "Oct 24 at 11pm" in the given zone; the year is the next occurrence.</summary>
    public static DateTimeOffset? ClaudeReset(string when, string zone, DateTimeOffset now)
    {
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(zone.Trim());
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Local;
        }
        var clean = when.Replace(" at ", " ", StringComparison.Ordinal).Trim().ToUpperInvariant();
        foreach (var year in new[] { now.Year, now.Year + 1 })
        {
            foreach (var format in new[] { "MMM d h:mmtt yyyy", "MMM d htt yyyy", "MMM d H:mm yyyy" })
            {
                if (!DateTime.TryParseExact($"{clean} {year}", format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)) continue;
                var date = new DateTimeOffset(local, timeZone.GetUtcOffset(local));
                if (date > now.AddDays(-1)) return date;
            }
        }
        return null;
    }

    /// <summary><c>claude auth status --json</c>: whether someone is signed in, and the plan. The email and organisation are never kept.</summary>
    public static (bool SignedIn, string Plan)? ClaudeAccount(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return (Get(root, "loggedIn") is { ValueKind: JsonValueKind.True }, Capitalized(Text(root, "subscriptionType") ?? ""));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // MARK: Gemini: `agy -p /quota`

    /// <summary>
    /// The most-used bucket across the Gemini groups is the bar (the one that stops you first);
    /// every Gemini group is listed, other groups (Claude/GPT inside Antigravity) once used.
    /// </summary>
    public static UsageSnapshot Gemini(string json, int exitCode, DateTimeOffset now)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return SignedOutText(json) ? UsageSnapshot.Status(UsageVendor.Gemini, UsageState.SignedOut) : UsageSnapshot.Status(UsageVendor.Gemini, UsageState.Failed, UsageError.NoData, exitCode);
        }
        using (document)
        {
            if (Get(document.RootElement, "command") is not { } command || Get(command, "data") is not { } data || Get(data, "groups") is not { ValueKind: JsonValueKind.Array } groups)
            {
                return SignedOutText(json) ? UsageSnapshot.Status(UsageVendor.Gemini, UsageState.SignedOut) : UsageSnapshot.Status(UsageVendor.Gemini, UsageState.Failed, UsageError.NoData, exitCode);
            }
            var gemini = new List<UsageWindow>();
            var other = new List<UsageWindow>();
            foreach (var group in groups.EnumerateArray())
            {
                var name = Text(group, "name") ?? "Models";
                if (Get(group, "buckets") is not { ValueKind: JsonValueKind.Array } buckets) continue;
                foreach (var bucket in buckets.EnumerateArray())
                {
                    if (Finite(Get(bucket, "remaining_fraction")) is not { } fraction || fraction is < 0 or > 1) continue;
                    var window = new UsageWindow
                    {
                        Kind = UsageWindowKind.Group,
                        Name = name,
                        IsWeekly = Text(bucket, "window") != "5h",
                        Used = (1 - fraction) * 100,
                        ResetsAt = Get(bucket, "reset_time") is { } reset ? Date(reset) : null,
                    };
                    if (name.Contains("Gemini", StringComparison.OrdinalIgnoreCase)) gemini.Add(window);
                    else if (fraction < 1) other.Add(window);
                }
            }
            if (gemini.Count == 0) return UsageSnapshot.Status(UsageVendor.Gemini, UsageState.Failed, UsageError.NoGemini);
            gemini = [.. gemini.OrderByDescending(window => window.Used ?? 0)];
            return new UsageSnapshot { Vendor = UsageVendor.Gemini, Bar = gemini[0], Windows = [.. gemini, .. other], MeasuredAt = now, Source = UsageSource.Live };
        }
    }

    // MARK: Codex

    /// <summary>
    /// Both the session-log shape (<c>used_percent</c>, <c>window_minutes</c>) and the app-server's
    /// (<c>usedPercent</c>, <c>windowDurationMins</c>). A missing number stays missing, never 0 %.
    /// </summary>
    public static UsageSnapshot? CodexLimits(JsonElement limits, string usedKey, string resetKey, DateTimeOffset? measured)
    {
        if ((Text(limits, "limit_id") ?? Text(limits, "limitId")) is { } id && id != "codex") return null;
        UsageWindow? Window(string key)
        {
            if (Get(limits, key) is not { } entry || Percent(Get(entry, usedKey)) is not { } used) return null;
            var duration = Finite(Get(entry, "window_minutes") ?? Get(entry, "windowDurationMins")) ?? 0;
            var minutes = duration is >= 0 and <= 525_600 && Math.Round(duration) == duration ? (int)duration : 0;
            var kind = minutes switch { 300 => UsageWindowKind.FiveHour, 10080 => UsageWindowKind.Week, > 0 => UsageWindowKind.Minutes, _ => UsageWindowKind.Quota };
            return new UsageWindow
            {
                Kind = kind,
                Minutes = kind == UsageWindowKind.Minutes ? minutes : 0,
                IsWeekly = minutes >= 10080,
                Used = used,
                ResetsAt = Get(entry, resetKey) is { } reset ? Date(reset) : null,
            };
        }
        var five = Window("primary");
        var week = Window("secondary");
        if (five is null && week is null) return null;
        var plan = Text(limits, "plan_type") ?? Text(limits, "planType");
        return new UsageSnapshot
        {
            Vendor = UsageVendor.Codex,
            Plan = plan is null ? "" : "ChatGPT " + Capitalized(plan),
            Bar = five ?? week,
            Windows = [.. new[] { five, week }.OfType<UsageWindow>()],
            MeasuredAt = measured,
        };
    }

    /// <summary>The app-server's <c>account/rateLimits/read</c> result, with the free resets still available.</summary>
    public static UsageSnapshot CodexServer(JsonElement result, DateTimeOffset now)
    {
        var limits = Get(result, "rateLimitsByLimitId") is { } byId && Get(byId, "codex") is { } codex ? codex : Get(result, "rateLimits");
        if (limits is not { } found || CodexLimits(found, "usedPercent", "resetsAt", now) is not { } snapshot)
        {
            return UsageSnapshot.Status(UsageVendor.Codex, UsageState.Failed, UsageError.NoData);
        }
        var credits = Get(result, "rateLimitResetCredits") is { } resets && Get(resets, "credits") is { ValueKind: JsonValueKind.Array } list
            ? list.EnumerateArray().Where(credit => Text(credit, "status") == "available").Select(credit => Get(credit, "expiresAt") is { } at ? Date(at) : null).OfType<DateTimeOffset>().Order().ToList()
            : [];
        return snapshot with { Source = UsageSource.Live, ResetCredits = credits.Count, ResetCreditsExpire = credits.Count > 0 ? credits[0] : null };
    }

    /// <summary>
    /// One line of a Codex session log: a <c>token_count</c> event with <c>rate_limits</c>, not in the
    /// future. Conversation lines are skipped without being kept.
    /// </summary>
    public static UsageSnapshot? CodexSessionLine(string line, DateTimeOffset now)
    {
        if (!line.Contains("\"rate_limits\"", StringComparison.Ordinal)) return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (Text(root, "type") != "event_msg" || Get(root, "payload") is not { } payload || Text(payload, "type") != "token_count"
                || Get(payload, "rate_limits") is not { } limits || Get(root, "timestamp") is not { } stamp || Date(stamp) is not { } time || time > now.AddMinutes(1))
            {
                return null;
            }
            return CodexLimits(limits, "used_percent", "resets_at", time) is { } snapshot ? snapshot with { Source = UsageSource.LocalSession } : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The newest genuine measurement in the tail of a session log, read backwards (UsageCodexLogs).
    /// Only the last 256 KiB of a log is read.
    /// </summary>
    public static UsageSnapshot? CodexSessionTail(string tail, DateTimeOffset now)
    {
        foreach (var line in tail.Split('\n').Reverse())
        {
            if (CodexSessionLine(line.TrimEnd('\r'), now) is { } snapshot) return snapshot;
        }
        return null;
    }
}
