using System.Text.Json;
using ISLe8.Core.Usage;

namespace ISLe8.Core.Tests;

/// <summary>Usage limits: the CLIs' answers, the cadence and the 90 % warning (PORTING.md §3.13).</summary>
public class UsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 20, 0, 0, TimeSpan.Zero);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ClaudesUsageLinesBecomeWindows()
    {
        const string text = "Current session: 12% used · resets Oct 30 at 1:10am (Europe/Istanbul)\n"
            + "Current week (all models): 40.5% used · resets Nov 2 at 11pm (Europe/Istanbul)\n"
            + "Current week (Opus): 55% used";
        var snapshot = UsageParse.ClaudeUsage(text, Now);
        Assert.Equal(UsageState.Ok, snapshot.State);
        Assert.Equal([UsageWindowKind.FiveHour, UsageWindowKind.Week, UsageWindowKind.ModelWeek], snapshot.Windows.Select(window => window.Kind));
        Assert.Equal((12, 40.5, 55d), (snapshot.Windows[0].Used, snapshot.Windows[1].Used, snapshot.Windows[2].Used!.Value));
        Assert.Equal("Opus", snapshot.Windows[2].Name);
        Assert.Same(snapshot.Windows[0], snapshot.Bar);
        // 1:10 in Istanbul (UTC+3) is 22:10 UTC the day before.
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 22, 10, 0, TimeSpan.Zero), snapshot.Windows[0].ResetsAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 20, 0, 0, TimeSpan.Zero), snapshot.Windows[1].ResetsAt);
    }

    [Fact]
    public void AnAccountPaidByUseHasNoLimits()
    {
        // What /usage answered on the test PC: a cost summary, no quota lines.
        const string text = "Total cost:            $0.0000\nTotal duration (API):  0s\nTotal duration (wall): 0s\n"
            + "Total code changes:    0 lines added, 0 lines removed\nUsage:                 0 input, 0 output, 0 cache read, 0 cache write";
        Assert.Equal(UsageState.NoLimits, UsageParse.ClaudeUsage(text, Now).State);
        Assert.Equal(UsageError.NoQuotaLines, UsageParse.ClaudeUsage("Something new", Now).Error);
        Assert.Equal(UsageState.SignedOut, UsageParse.ClaudeUsage("Not logged in · Please run /login", Now).State);
    }

    [Fact]
    public void ClaudesAccountGivesOnlyThePlan()
    {
        Assert.Equal((true, "Max"), UsageParse.ClaudeAccount("""{"loggedIn":true,"email":"x@y","subscriptionType":"max"}"""));
        Assert.Equal((false, ""), UsageParse.ClaudeAccount("""{"loggedIn":false}"""));
        Assert.Null(UsageParse.ClaudeAccount("not json"));
    }

    [Fact]
    public void AntigravitysGroupsPutTheTightestGeminiFirst()
    {
        // The numbers agy gave on the test PC.
        const string json = """
            {"command":{"data":{"groups":[
              {"name":"Gemini Models","buckets":[{"window":"weekly","remaining_fraction":0.9548428058624268,"reset_time":"2026-10-30T20:38:03Z"},{"window":"5h","remaining_fraction":1,"reset_time":"2026-10-30T00:34:20Z"}]},
              {"name":"Claude and GPT models","buckets":[{"window":"weekly","remaining_fraction":0,"reset_time":"2026-10-30T20:07:57Z"},{"window":"5h","remaining_fraction":1}]},
              {"name":"Gemini 4 Argon","buckets":[{"window":"weekly","remaining_fraction":0.0009269333095289767,"reset_time":"2026-10-30T11:18:56Z"}]}]}}}
            """;
        var snapshot = UsageParse.Gemini(json, 0, Now);
        Assert.Equal("Gemini 4 Argon", snapshot.Bar!.Name);
        Assert.Equal("99", UsageWindow.PercentText(snapshot.Bar.Used));
        Assert.True(snapshot.Bar.IsWeekly);
        // Three Gemini buckets, then the used-up Claude/GPT week; its untouched 5 h is left out.
        Assert.Equal(4, snapshot.Windows.Count);
        Assert.Equal("Claude and GPT models", snapshot.Windows[3].Name);
        Assert.Equal(UsageState.Failed, UsageParse.Gemini("{}", 3, Now).State);
        Assert.Equal(3, UsageParse.Gemini("{}", 3, Now).ErrorCode);
    }

    [Fact]
    public void CodexsServerGivesItsWindowsAndPlan()
    {
        // account/rateLimits/read on the test PC.
        var result = Json("""
            {"rateLimitsByLimitId":{"codex":{"limitId":"codex","planType":"team",
              "primary":{"usedPercent":10,"windowDurationMins":300,"resetsAt":1793306209},
              "secondary":{"usedPercent":68,"windowDurationMins":10080,"resetsAt":1793781774}}},
             "rateLimitResetCredits":{"credits":[{"status":"available","expiresAt":"2026-11-01T09:00:00Z"},{"status":"used","expiresAt":"2026-10-30T09:00:00Z"}]}}
            """);
        var snapshot = UsageParse.CodexServer(result, Now);
        Assert.Equal(("ChatGPT Team", UsageSource.Live), (snapshot.Plan, snapshot.Source));
        Assert.Equal((UsageWindowKind.FiveHour, 10d), (snapshot.Windows[0].Kind, snapshot.Windows[0].Used!.Value));
        Assert.Equal((UsageWindowKind.Week, true, 68d), (snapshot.Windows[1].Kind, snapshot.Windows[1].IsWeekly, snapshot.Windows[1].Used!.Value));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793306209), snapshot.Windows[0].ResetsAt);
        Assert.Equal(1, snapshot.ResetCredits);
        Assert.Equal(UsageState.Failed, UsageParse.CodexServer(Json("{}"), Now).State);
    }

    [Fact]
    public void CodexsSessionLogIsReadBackwardsForTheLatestQuota()
    {
        const string tail = """
            {"timestamp":"2026-10-29T19:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":5,"window_minutes":300,"resets_at":1793306209}}}}
            {"timestamp":"2026-10-29T19:30:00Z","type":"response_item","payload":{"type":"message","content":"rate_limits are mentioned here"}}
            {"timestamp":"2026-10-29T19:40:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":7.5,"window_minutes":300},"secondary":{"used_percent":60,"window_minutes":10080}}}}
            {"timestamp":"2026-10-29T23:00:00Z","type":"event_msg","payload":{"type":"token_count","rate_limits":{"primary":{"used_percent":99,"window_minutes":300}}}}
            """;
        var snapshot = UsageParse.CodexSessionTail(tail, Now)!;
        // The line from the future is no measurement.
        Assert.Equal(7.5, snapshot.Windows[0].Used);
        Assert.Equal(UsageSource.LocalSession, snapshot.Source);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 19, 40, 0, TimeSpan.Zero), snapshot.MeasuredAt);
        Assert.Null(UsageParse.CodexLimits(Json("""{"limit_id":"other","primary":{"used_percent":5}}"""), "used_percent", "resets_at", Now));
        Assert.Null(UsageParse.CodexLimits(Json("""{"primary":{"used_percent":140}}"""), "used_percent", "resets_at", Now));
    }

    [Fact]
    public void PercentsNeverReadAsFullBeforeTheyAre()
    {
        Assert.Equal("99", UsageWindow.PercentText(99.6));
        Assert.Equal("100", UsageWindow.PercentText(100));
        Assert.Equal("13", UsageWindow.PercentText(12.5));
        Assert.Equal("—", UsageWindow.PercentText(null));
        var window = new UsageWindow { Used = 95, ResetsAt = Now.AddMinutes(-1) };
        Assert.True(window.ResetPassed(Now));
        Assert.False(window.IsHot(Now));
        Assert.Equal(5, (window with { Used = 95 }).Remaining);
    }

    [Fact]
    public void TheCadenceKeepsToTheMac()
    {
        Assert.Equal(TimeSpan.FromMinutes(20), UsagePolicy.RemoteInterval(UsageVendor.Claude, economy: false, visible: true));
        Assert.Equal(TimeSpan.FromMinutes(1), UsagePolicy.RemoteInterval(UsageVendor.Codex, economy: false, visible: true));
        Assert.Equal(TimeSpan.FromMinutes(5), UsagePolicy.RemoteInterval(UsageVendor.Gemini, economy: false, visible: false));
        Assert.Equal(TimeSpan.FromMinutes(15), UsagePolicy.RemoteInterval(UsageVendor.Codex, economy: true, visible: true));
        Assert.Equal([1, 1, 2, 4, 8, 16, 30, 30], new[] { 0, 1, 2, 3, 4, 5, 6, 9 }.Select(failures => UsagePolicy.RetryDelay(failures).TotalMinutes));
    }

    [Fact]
    public void AWindowPast90WarnsOnceUntilItDropsBack()
    {
        var warnings = new UsageWarnings();
        UsageSnapshot Reading(double used) => new()
        {
            Vendor = UsageVendor.Codex,
            Windows = [new UsageWindow { Kind = UsageWindowKind.FiveHour, Used = used }],
        };
        Assert.Empty(warnings.Read(Reading(80), Now));
        Assert.Single(warnings.Read(Reading(91), Now));
        Assert.Empty(warnings.Read(Reading(95), Now));
        Assert.Empty(warnings.Read(Reading(50), Now));
        Assert.Single(warnings.Read(Reading(92), Now));

        // Already hot in the cache: warned about last time.
        var fresh = new UsageWarnings();
        Assert.Empty(fresh.Read(Reading(93), Now, fromCache: true));
        Assert.Empty(fresh.Read(Reading(94), Now));
    }

    [Fact]
    public void TheCacheKeepsOnlyNumbers()
    {
        var snapshot = UsageParse.CodexServer(Json("""{"rateLimits":{"planType":"plus","primary":{"usedPercent":10,"windowDurationMins":300}}}"""), Now);
        var json = JsonSerializer.Serialize(new Dictionary<string, UsageSnapshot> { ["codex"] = snapshot }, UsageJsonContext.Default.DictionaryStringUsageSnapshot);
        var back = JsonSerializer.Deserialize(json, UsageJsonContext.Default.DictionaryStringUsageSnapshot)!["codex"];
        Assert.True(back.SameAs(snapshot));
        Assert.Contains("\"fiveHour\"", json, StringComparison.OrdinalIgnoreCase);
    }
}
