using ISLe8.Core.Island;
using ISLe8.Core.Notes;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Tests;

/// <summary>The Notes tab (PORTING.md §3.19).</summary>
public sealed class NotesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "isle8-notes-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 29, 18, 0, 0, TimeSpan.FromHours(3));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void NotesComeNewestFirstAndStayAfterARestart()
    {
        var store = new NotesStore(_folder, () => _now);
        store.Add("  Süt al  ");
        _now = _now.AddMinutes(1);
        store.Add("Faturayı öde");
        store.Add("   ");

        var again = new NotesStore(_folder, () => _now);
        Assert.Equal(["Faturayı öde", "Süt al"], again.Notes.Select(note => note.Text));
    }

    [Fact]
    public void EditingAndEmptyingANote()
    {
        var store = new NotesStore(_folder, () => _now);
        store.Add("Süt al");
        var id = store.Notes[0].Id;

        _now = _now.AddHours(2);
        store.Update(id, "Süt ve ekmek al");
        Assert.Equal(("Süt ve ekmek al", _now), (store.Notes[0].Text, store.Notes[0].Updated));

        store.Update(id, "  ");
        Assert.Empty(store.Notes);
    }

    [Fact]
    public void DeletingSaysSo()
    {
        var store = new NotesStore(_folder, () => _now);
        store.Add("Bir");
        var changes = 0;
        store.Changed += () => changes++;

        store.Delete(store.Notes[0].Id);
        store.Delete(Guid.NewGuid());

        Assert.Empty(new NotesStore(_folder, () => _now).Notes);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(0.5, RelativeTime.Unit.Now, 0)]
    [InlineData(5, RelativeTime.Unit.Minutes, 5)]
    [InlineData(3 * 60, RelativeTime.Unit.Hours, 3)]
    [InlineData(20 * 60, RelativeTime.Unit.Yesterday, 1)]
    [InlineData(4 * 24 * 60, RelativeTime.Unit.Days, 4)]
    [InlineData(15 * 24 * 60, RelativeTime.Unit.Weeks, 2)]
    [InlineData(70 * 24 * 60, RelativeTime.Unit.Months, 2)]
    public void WhenANoteWasWrittenReadsLikeTheMacs(double minutesAgo, RelativeTime.Unit unit, int count)
    {
        Assert.Equal((unit, count), RelativeTime.Of(_now.AddMinutes(-minutesAgo), _now));
    }

    [Fact]
    public void TheNewTabsJoinASavedTabOrderAtTheEnd()
    {
        var settings = SettingsStore.Import("""{ "tabOrder": ["devices", "home", "shelf", "clipboard", "agents"] }""");

        Assert.Equal(["devices", "home", "shelf", "clipboard", "agents", "notes", "system"], settings.TabOrder);
        Assert.Equal(["home", "shelf", "clipboard", "notes", "agents", "devices", "system"], new AppSettings().TabOrder);
        Assert.Equal(IslandTab.Notes, IslandTabs.Parse("notes"));
    }
}
