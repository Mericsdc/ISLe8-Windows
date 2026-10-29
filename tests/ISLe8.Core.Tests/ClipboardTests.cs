using ISLe8.Core.Clipboard;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Tests;

/// <summary>The clipboard history (PORTING.md §3.9).</summary>
public sealed class ClipboardTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "isle8-clipboard-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 29, 18, 0, 0, TimeSpan.FromHours(3));
    private static readonly TimeSpan Month = TimeSpan.FromDays(30);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private ClipboardHistory History() => new(_folder);

    private void Copy(ClipboardHistory history, string text, string? app = null, int limit = 200)
    {
        _now = _now.AddSeconds(1);
        history.Record(ClipboardCapture.ForText(text, null), app, _now, limit, Month);
    }

    [Fact]
    public void ToplantiFindsToplantiAndWordStartsMatchWithoutCaseOrAccents()
    {
        var history = History();
        Copy(history, "Yarınki Toplantı saat 10'da");
        Copy(history, "Şirket çalışanları");
        Copy(history, "something else");

        Assert.Single(history.Items("toplanti", 50));
        Assert.Single(history.Items("TOPLANTI", 50));
        Assert.Single(history.Items("sirket calis", 50));
        Assert.Single(history.Items("yarin top", 50));
        Assert.Empty(history.Items("planti", 50)); // inside a word, not its start
        Assert.Equal(3, history.Items("", 50).Count);
    }

    [Fact]
    public void TheSameContentAgainMovesToTheTop()
    {
        var history = History();
        Copy(history, "bir");
        Copy(history, "iki");
        Copy(history, "bir", app: "notepad.exe");

        var items = history.Items("", 50);
        Assert.Equal(["bir", "iki"], items.Select(item => item.Text));
        Assert.Equal("notepad.exe", items[0].SourceApp);
        Assert.Equal(2, history.Count);
    }

    [Fact]
    public void TheLimitAndTheAgeTrimEverythingButThePinned()
    {
        var history = History();
        Copy(history, "pinned");
        history.SetPinned(history.Items("", 10)[0].Id, true);
        for (var i = 0; i < 5; i++) Copy(history, $"item {i}", limit: 3);

        var items = history.Items("", 50);
        Assert.Equal(["pinned", "item 4", "item 3", "item 2"], items.Select(item => item.Text));

        _now = _now.AddDays(31);
        history.Prune(3, Month, _now);
        Assert.Equal(["pinned"], history.Items("", 50).Select(item => item.Text));
    }

    [Fact]
    public void ClearingKeepsThePinnedAndDeletingRemovesImages()
    {
        var history = History();
        Copy(history, "keep");
        history.SetPinned(history.Items("", 10)[0].Id, true);
        history.Record(ClipboardCapture.ForImage([1, 2, 3]), null, _now, 200, Month);
        var image = history.Items("", 10).First(item => item.Kind == ClipboardKind.Image);
        Assert.True(File.Exists(Path.Combine(history.ImagesFolder, image.ImageName!)));

        history.Clear();

        Assert.Equal(["keep"], history.Items("", 10).Select(item => item.Text));
        Assert.False(File.Exists(Path.Combine(history.ImagesFolder, image.ImageName!)));
    }

    [Fact]
    public void TheHistoryIsThereAfterARestart()
    {
        var history = History();
        Copy(history, "kalıcı");
        Copy(history, "https://example.com/a");

        var again = History();
        Assert.Equal([ClipboardKind.Url, ClipboardKind.Text], again.Items("", 10).Select(item => item.Kind));
        Copy(again, "yeni");
        Assert.Equal(3, again.Items("", 10).Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public void ADamagedFileIsSetAsideAndTheHistoryStartsAgain()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "clipboard.json"), """[ { "value": [ { "id": 1 } ] } ]""");

        var history = History();
        Assert.True(history.IsAvailable);
        Assert.Equal(0, history.Count);
        Assert.True(File.Exists(Path.Combine(_folder, "clipboard.json.broken")));

        Copy(history, "yeni başlangıç");
        Assert.Single(History().Items("", 10));
    }

    [Fact]
    public void KindsTitlesColoursAndAddresses()
    {
        Assert.Equal(ClipboardKind.Url, ClipboardCapture.ForText("https://isle8.app", null).Kind);
        Assert.Equal(ClipboardKind.Text, ClipboardCapture.ForText("see https://isle8.app", null).Kind);
        Assert.Equal(ClipboardKind.RichText, ClipboardCapture.ForText("bold", @"{\rtf1 \b bold}").Kind);

        var files = ClipboardCapture.ForFiles([@"C:\Users\me\a.txt", @"C:\Users\me\b.png"]);
        var entry = new ClipboardEntry { Id = 1, Kind = files.Kind, Text = files.Text, Hash = files.Hash, CopiedAt = _now };
        Assert.Equal("a.txt, b.png", entry.Title);

        var colour = new ClipboardEntry { Id = 2, Kind = ClipboardKind.Text, Text = " #FF9F0A ", Hash = "x", CopiedAt = _now };
        Assert.Equal(((byte)0xFF, (byte)0x9F, (byte)0x0A), colour.Color);
        var mail = colour with { Text = "ayse@example.com" };
        Assert.Equal("ayse@example.com", mail.EmailAddress);
        Assert.Equal("satır bir iki", (colour with { Text = "satır\n  bir\tiki" }).Title);
    }

    [Fact]
    public void TheShortcutOpensOnTheClipboardAndClosesItAgain()
    {
        var model = new Island.IslandModel(new AppSettings(), new ManualClock());

        model.Toggle(Island.IslandTab.Clipboard);
        Assert.Equal((Island.IslandState.Expanded, Island.IslandTab.Clipboard), (model.State, model.Tab));

        model.Toggle(Island.IslandTab.Clipboard);
        Assert.Equal(Island.IslandState.Pill, model.State);
    }

    [Fact]
    public void LimitsStayInTheMacsRangesAndPasswordManagersAreExcluded()
    {
        var settings = SettingsStore.Import("""{ "clipboardLimit": 1234, "clipboardMaxAgeDays": 0 }""");

        Assert.Equal(1000, settings.ClipboardLimit);
        Assert.Equal(1, settings.ClipboardMaxAgeDays);
        Assert.Contains("1Password.exe", new AppSettings().ClipboardExcludedApps);
    }
}
