using System.Globalization;
using System.Net;
using ISLe8.Core.Calendar;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Tests;

/// <summary>The calendar from ICS feeds (PORTING.md §3.6).</summary>
public class CalendarTests
{
    private static readonly TimeSpan Istanbul = TimeSpan.FromHours(3);

    private const string Feed = "BEGIN:VCALENDAR\r\n" +
        "X-WR-CALNAME:İş\r\n" +
        "BEGIN:VEVENT\r\nUID:standup\r\nSUMMARY:Günlük toplantı\r\n" +
        "DTSTART;TZID=Turkey Standard Time:20261026T093000\r\nDTEND;TZID=Turkey Standard Time:20261026T094500\r\n" +
        "RRULE:FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=6\r\nEXDATE;TZID=Turkey Standard Time:20261028T093000\r\n" +
        "LOCATION:https://teams.microsoft.com/l/meetup-join/abc\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:standup\r\nRECURRENCE-ID;TZID=Turkey Standard Time:20261030T093000\r\nSUMMARY:Günlük toplantı (kaydı)\r\n" +
        "DTSTART;TZID=Turkey Standard Time:20261030T110000\r\nDTEND;TZID=Turkey Standard Time:20261030T111500\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:trip\r\nSUMMARY:Ankara\\, gezi\r\nDTSTART;VALUE=DATE:20261029\r\nDTEND;VALUE=DATE:20261031\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:lunch\r\nSUMMARY:Öğle\r\nDTSTART;TZID=Europe/Istanbul:20261029T123000\r\nDURATION:PT1H\r\n" +
        "DESCRIPTION:Bağlantı: https://meet.google.com/xyz-abcd-efg\\nGörüşürüz\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:gone\r\nSUMMARY:İptal\r\nSTATUS:CANCELLED\r\nDTSTART:20261029T100000Z\r\nDTEND:20261029T110000Z\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nUID:folded\r\nSUMMARY:Uzun bir\r\n  başlık\r\nDTSTART:20261029T150000Z\r\nDTEND:20261029T160000Z\r\nEND:VEVENT\r\n" +
        "END:VCALENDAR\r\n";

    [Fact]
    public void AFeedReadsItsEventsZonesAndText()
    {
        var (name, events) = IcsParser.Parse(Feed);

        Assert.Equal("İş", name);
        Assert.DoesNotContain(events, e => e.Uid == "gone");
        var standup = events.First(e => e.Uid == "standup" && e.RecurrenceId is null);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 30, 0, Istanbul), standup.Start);
        Assert.Equal(TimeSpan.FromMinutes(15), standup.Length);
        var lunch = events.First(e => e.Uid == "lunch");
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 12, 30, 0, Istanbul), lunch.Start);
        Assert.Equal(TimeSpan.FromHours(1), lunch.Length);
        Assert.Equal("Ankara, gezi", events.First(e => e.Uid == "trip").Title);
        Assert.True(events.First(e => e.Uid == "trip").IsAllDay);
        Assert.Equal("Uzun bir başlık", events.First(e => e.Uid == "folded").Title);
    }

    [Fact]
    public void ARepeatingEventSkipsItsExceptionsAndGivesWayToItsMovedCopy()
    {
        var service = Service(Feed, out _);
        service.Refresh().GetAwaiter().GetResult();

        var week = service.Between(new DateTimeOffset(2026, 10, 26, 0, 0, 0, Istanbul), new DateTimeOffset(2026, 11, 2, 0, 0, 0, Istanbul))
            .Where(e => e.Title.StartsWith("Günlük")).ToList();

        // Monday, (not Wednesday: EXDATE), Friday moved to 11:00.
        Assert.Equal(
            [new DateTimeOffset(2026, 10, 26, 9, 30, 0, Istanbul), new DateTimeOffset(2026, 10, 30, 11, 0, 0, Istanbul)],
            week.Select(e => e.Start));
        Assert.Equal("Microsoft Teams", week[0].Detail);
        Assert.Equal("https://teams.microsoft.com/l/meetup-join/abc", week[0].MeetingLink);
    }

    [Fact]
    public void ACountEndsTheRule()
    {
        var (_, events) = IcsParser.Parse(Feed);
        var standup = events.First(e => e.Uid == "standup" && e.RecurrenceId is null);

        var all = Recurrence.Occurrences(standup, DateTimeOffset.MinValue.AddYears(2000), DateTimeOffset.MaxValue.AddYears(-1)).ToList();

        Assert.Equal(5, all.Count); // six by COUNT, one of them excluded
        Assert.Equal(new DateTimeOffset(2026, 11, 6, 9, 30, 0, Istanbul), all[^1]);
    }

    [Fact]
    public void AMultiDayEventMarksEachDayAndMeetingLinksAreFoundInTheNotes()
    {
        var service = Service(Feed, out _);
        service.Refresh().GetAwaiter().GetResult();

        var days = service.EventDays(new DateOnly(2026, 10, 28), new DateOnly(2026, 11, 1));
        Assert.True(days.ContainsKey(new DateOnly(2026, 10, 29)));
        Assert.True(days.ContainsKey(new DateOnly(2026, 10, 30)));
        Assert.False(days.ContainsKey(new DateOnly(2026, 10, 31)));
        Assert.False(days.ContainsKey(new DateOnly(2026, 10, 28)));

        var lunch = service.On(new DateOnly(2026, 10, 29)).First(e => e.Title == "Öğle");
        Assert.Equal("https://meet.google.com/xyz-abcd-efg", lunch.MeetingLink);
        Assert.Equal("Google Meet", lunch.Detail);
        // All-day first.
        Assert.True(service.On(new DateOnly(2026, 10, 29))[0].IsAllDay);
    }

    [Fact]
    public void FiveMinutesBeforeAMeetingAHeadsUpComesOnce()
    {
        var service = Service(Feed, out var clock, start: new DateTimeOffset(2026, 10, 29, 12, 0, 0, Istanbul));
        var soon = new List<CalendarEntry>();
        service.MeetingSoon += soon.Add;
        service.Refresh().GetAwaiter().GetResult();

        clock.Advance(TimeSpan.FromMinutes(24));
        Assert.Empty(soon);
        clock.Advance(TimeSpan.FromMinutes(1.1));
        Assert.Equal(["Öğle"], soon.Select(e => e.Title));
        clock.Advance(TimeSpan.FromMinutes(3));
        Assert.Single(soon);
    }

    [Theory]
    [InlineData("14:30 Toplantı", "Toplantı", 14, 30)]
    [InlineData("9.05 Diş", "Diş", 9, 5)]
    [InlineData("Toplantı", "Toplantı", 13, 0)]
    public void QuickAddReadsATimeOrTakesTheNextHour(string text, string title, int hour, int minute)
    {
        var now = new DateTimeOffset(2026, 10, 29, 12, 20, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 29, 12, 20, 0)));

        var parsed = CalendarService.ParseQuickAdd(text, new DateOnly(2026, 10, 29), now);

        Assert.Equal(title, parsed!.Value.Title);
        Assert.Equal((hour, minute), (parsed.Value.Start.Hour, parsed.Value.Start.Minute));
        Assert.Null(CalendarService.ParseQuickAdd("   ", new DateOnly(2026, 10, 29), now));
        Assert.Equal(9, CalendarService.ParseQuickAdd("Diş", new DateOnly(2026, 10, 30), now)!.Value.Start.Hour);
    }

    [Fact]
    public void AQuickAddedEventIsAFileTheCalendarAppReads()
    {
        var ics = IcsParser.Write("Kahve, Ayşe ile", new DateTimeOffset(2026, 10, 29, 14, 30, 0, Istanbul), TimeSpan.FromHours(1), DateTimeOffset.UnixEpoch);
        var (_, events) = IcsParser.Parse(ics);

        Assert.Equal("Kahve, Ayşe ile", events.Single().Title);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 14, 30, 0, Istanbul), events.Single().Start);
    }

    [Fact]
    public void TheMonthIsWholeWeeksFromTheCulturesFirstDay()
    {
        var days = CalendarGrid.MonthDays(new DateOnly(2026, 10, 1), DayOfWeek.Monday);

        Assert.Equal(new DateOnly(2026, 9, 28), days[0]);
        Assert.Equal(new DateOnly(2026, 11, 1), days[^1]);
        Assert.Equal(35, days.Count);
        Assert.Equal("Pzt", CalendarGrid.WeekdaySymbols(CultureInfo.GetCultureInfo("tr-TR"))[0]);
        Assert.Equal("Sun", CalendarGrid.WeekdaySymbols(CultureInfo.GetCultureInfo("en-US"))[0]);
    }

    [Fact]
    public void OnlyWebLinksAreKeptAsFeeds()
    {
        var settings = SettingsStore.Import("""{ "calendarFeeds": ["webcal://p01.icloud.com/x.ics", " https://a/b.ics ", "file:///c:/x.ics", "https://a/b.ics"] }""");

        Assert.Equal(["webcal://p01.icloud.com/x.ics", "https://a/b.ics"], settings.CalendarFeeds);
    }

    private static CalendarService Service(string feed, out ManualClock clock, DateTimeOffset? start = null)
    {
        clock = start is { } at ? new ManualClock(at) : new ManualClock();
        var settings = new AppSettings { CalendarFeeds = ["https://calendar.example/feed.ics"] };
        return new CalendarService(settings, new HttpClient(new Reply(feed)), clock);
    }

    private sealed class Reply(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
