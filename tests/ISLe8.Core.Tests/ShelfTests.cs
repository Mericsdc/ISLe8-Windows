using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Settings;
using ISLe8.Core.Shelf;

namespace ISLe8.Core.Tests;

/// <summary>The shelf and the drop target (PORTING.md §3.10).</summary>
public sealed class ShelfTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "isle8-shelf-" + Guid.NewGuid().ToString("N"));
    private readonly string _data;
    private readonly string _files;
    private readonly Locator _locator = new();
    private DateTimeOffset _now = new(2026, 10, 29, 19, 0, 0, TimeSpan.FromHours(3));

    public ShelfTests()
    {
        _data = Path.Combine(_root, "data");
        _files = Path.Combine(_root, "files");
        Directory.CreateDirectory(_files);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string File(string name, string content = "x")
    {
        var path = Path.Combine(_files, name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    private ShelfStore Store() => new(_data, _locator, () => _now);

    [Fact]
    public void DroppedFilesGoToTheFrontAndAFileAgainOnlyMoves()
    {
        var store = Store();
        var a = File("a.txt");
        var b = File("b.png");
        store.Add([a], copy: false);
        store.Add([b], copy: false);
        store.Add([a], copy: false);

        Assert.Equal(["a.txt", "b.png"], store.Items.Select(item => item.Name));
        Assert.True(store.Items[1].IsImage);
        Assert.False(store.Items[0].IsCopy);
    }

    [Fact]
    public void ACopyLivesInTheShelfFolderAndGoesWithItsItem()
    {
        var store = Store();
        store.Add([File("rapor.pdf", "pdf")], copy: true);
        var item = store.Items.Single();

        Assert.True(item.IsCopy);
        Assert.StartsWith(store.FilesFolder, item.Path);
        Assert.Equal("pdf", System.IO.File.ReadAllText(item.Path));

        store.Remove(item.Id);
        Assert.False(System.IO.File.Exists(item.Path));
        Assert.True(System.IO.File.Exists(Path.Combine(_files, "rapor.pdf")));
    }

    [Fact]
    public void AMovedFileIsFoundAgainAndAGoneOneLeaves()
    {
        var store = Store();
        var moving = File("taşınan.txt");
        var gone = File("silinen.txt");
        store.Add([moving, gone], copy: false);

        var moved = Path.Combine(_files, "yeni adı.txt");
        System.IO.File.Move(moving, moved);
        _locator.Moves[moving] = moved;
        System.IO.File.Delete(gone);

        Assert.Equal([moved], Store().Items.Select(item => item.Path));
    }

    [Fact]
    public void ZipsAndConversionsGetAFreeName()
    {
        File("foto.png");
        File("foto 2.png");

        Assert.Equal(Path.Combine(_files, "foto 3.png"), ShelfStore.UniquePath(_files, "foto", "png"));
        Assert.Equal(Path.Combine(_files, "foto.zip"), ShelfStore.UniquePath(_files, "foto", "zip"));
    }

    [Fact]
    public void DraggingOntoTheIslandOpensTheDropTargetAndAwayClosesIt()
    {
        var model = new IslandModel(new AppSettings(), new ManualClock());

        model.SetFileDragNearby(true);
        Assert.Equal(IslandState.DropTarget, model.State);
        model.SetHovering(false);
        Assert.Equal(IslandState.DropTarget, model.State);

        model.SetFileDragNearby(false);
        Assert.Equal(IslandState.Pill, model.State);

        model.SetFileDragNearby(true);
        model.ShowShelfAfterDrop();
        Assert.Equal((IslandState.Expanded, IslandTab.Shelf), (model.State, model.Tab));
        Assert.Equal(new DipSize(460, 144), IslandSizes.DropTarget);
    }

    [Fact]
    public void ACopyMovedOutOfTheShelfsFilesIsNeverDeleted()
    {
        var store = Store();
        store.Add([File("photo.png")], copy: true);
        var copy = store.Items[0].Path;
        Assert.NotNull(store.CopyFolderOf(copy));
        // The user takes the copy out (Show in folder, then drag it to Desktop).
        var desktop = Path.Combine(_root, "Desktop");
        Directory.CreateDirectory(desktop);
        var moved = Path.Combine(desktop, "photo.png");
        System.IO.File.Move(copy, moved);
        System.IO.File.WriteAllText(Path.Combine(desktop, "other.txt"), "keep");
        _locator.Moves[copy] = moved;

        var reopened = Store();
        var item = Assert.Single(reopened.Items);
        Assert.Equal(moved, item.Path);
        Assert.False(item.IsCopy);
        reopened.Remove(item.Id);
        Assert.True(System.IO.File.Exists(moved));
        Assert.True(System.IO.File.Exists(Path.Combine(desktop, "other.txt")));
        Assert.Null(reopened.CopyFolderOf(moved));
    }

    /// <summary>Stands in for NTFS file IDs: a file's id is the path it was added from.</summary>
    private sealed class Locator : IFileLocator
    {
        public Dictionary<string, string> Moves { get; } = [];

        public string? IdOf(string path) => path;

        public string? PathOf(string id) => Moves.TryGetValue(id, out var path) ? path : null;
    }
}
