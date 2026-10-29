using ISLe8.Core.Geometry;
using ISLe8.Core.Settings;
using ISLe8.Core.Shelf;
using ISLe8.Core.Status;

namespace ISLe8.Core.Tests;

/// <summary>Where the island sits (Settings › Appearance › Island), the volume keys' steps, and shelf copies.</summary>
public sealed class PlacementTests : IDisposable
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);
    /// <summary>A 48-pixel taskbar at the bottom.</summary>
    private static readonly PixelRect WorkArea = new(0, 0, 1920, 1032);
    private static readonly DipSize Expanded = new(870, 288);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "isle8-placement-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static IslandLayout At(IslandPlacement placement, bool flush = false) =>
        IslandLayout.Compute(Monitor, WorkArea, 1, Expanded, placement, flush);

    /// <summary>A rect relative to the area, in screen pixels at scale 1.</summary>
    private static DipRect OnScreen(IslandLayout layout, DipRect rect) =>
        rect.Offset(layout.Window.X + layout.Area.X, layout.Window.Y + layout.Area.Y);

    [Fact]
    public void TheDefaultIsTheMacsTopCentre()
    {
        var layout = At(IslandPlacement.Top);
        var pill = OnScreen(layout, layout.IdlePill);

        Assert.Equal(IslandPlacement.Top, new AppSettings().Placement);
        Assert.False(new AppSettings().FlushTop);
        Assert.Equal(IslandMetrics.TopMargin, pill.Y);
        Assert.Equal(1920 / 2.0, pill.X + pill.Width / 2);
        Assert.False(layout.FromBottom);
        Assert.Equal(layout.IdlePill.Y, layout.Open(new DipSize(640, 200)).Y);
    }

    [Fact]
    public void FlushWithTheTopHasNoGap()
    {
        var layout = At(IslandPlacement.Top, flush: true);

        Assert.True(layout.Flush);
        Assert.Equal(0, OnScreen(layout, layout.IdlePill).Y);
        Assert.Equal(0, layout.Open(new DipSize(640, 200)).Y);
        Assert.Equal(38, IslandMetrics.ReservedPixels(1, gap: 0));
        // Flush only means something at the top.
        Assert.False(At(IslandPlacement.TaskbarCenter, flush: true).Flush);
    }

    [Theory]
    [InlineData(IslandPlacement.TaskbarLeft)]
    [InlineData(IslandPlacement.TaskbarCenter)]
    [InlineData(IslandPlacement.TaskbarRight)]
    public void OnTheTaskbarThePillStandsOnItAndTheIslandOpensUpwards(IslandPlacement placement)
    {
        var layout = At(placement);
        var pill = OnScreen(layout, layout.IdlePill);
        var open = OnScreen(layout, layout.Open(new DipSize(640, 200)));
        var peek = OnScreen(layout, layout.Peek(Geometry.PillKind.Idle));

        Assert.True(layout.FromBottom);
        Assert.Equal(WorkArea.Bottom - IslandMetrics.TopMargin, pill.Bottom);
        Assert.Equal(pill.Bottom, open.Bottom);
        Assert.Equal(pill.Bottom, peek.Bottom);
        Assert.Equal(200, open.Height);
        // The window never reaches over the taskbar.
        Assert.True(layout.Window.Bottom <= WorkArea.Bottom);
        // The hit area runs from the pill to the window's bottom edge.
        var region = layout.PillRegion(Geometry.PillKind.Idle);
        Assert.Equal(layout.Window.Height, region.Bottom);
    }

    [Fact]
    public void InACornerTheIslandKeepsToItsSide()
    {
        var left = At(IslandPlacement.TaskbarLeft);
        var right = At(IslandPlacement.TaskbarRight);
        var centre = At(IslandPlacement.TaskbarCenter);

        var leftPill = OnScreen(left, left.IdlePill);
        var leftOpen = OnScreen(left, left.Open(new DipSize(640, 200)));
        Assert.Equal(IslandMetrics.SideInset, leftPill.X);
        Assert.Equal(leftPill.X, leftOpen.X);

        var rightPill = OnScreen(right, right.CompactPill);
        var rightOpen = OnScreen(right, right.Open(new DipSize(640, 200)));
        Assert.Equal(1920 - IslandMetrics.SideInset, rightPill.Right);
        Assert.Equal(rightPill.Right, rightOpen.Right);

        var centrePill = OnScreen(centre, centre.IdlePill);
        Assert.Equal(1920 / 2.0, centrePill.X + centrePill.Width / 2);
        Assert.True(left.Window.X >= 0 && right.Window.Right <= 1920);
    }

    [Fact]
    public void VolumeKeysStepByTwoAndLandOnWholeSteps()
    {
        Assert.Equal(0.52, VolumeSteps.Next(0.50, up: true), 6);
        Assert.Equal(0.48, VolumeSteps.Next(0.50, up: false), 6);
        Assert.Equal(0.52, VolumeSteps.Next(0.507, up: true), 6);
        Assert.Equal(1, VolumeSteps.Next(0.99, up: true), 6);
        Assert.Equal(0, VolumeSteps.Next(0.01, up: false), 6);
        Assert.Equal(1, VolumeSteps.Next(1, up: true), 6);
        Assert.False(new AppSettings().ReplaceSystemHud);
    }

    [Fact]
    public void ACopyMadeOutsideTheStoreIsKeptAndDeletedAsACopy()
    {
        var store = new ShelfStore(Path.Combine(_root, "data"), new NoLocator(), () => DateTimeOffset.UnixEpoch);
        var folder = store.NewCopyFolder();
        var copy = Path.Combine(folder, "photo.png");
        File.WriteAllText(copy, "x");

        store.AddCopies([copy]);

        var item = Assert.Single(store.Items);
        Assert.True(item.IsCopy);
        Assert.Equal(copy, item.Path);
        store.Remove(item.Id);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void FlushTheIslandIsTheMacsNotchShape()
    {
        var theme = Theming.IslandTheme.NotchBlack;
        Assert.Equal(0, IslandSizes.FlushShoulder(Island.IslandState.Pill, Geometry.PillKind.Idle));
        Assert.Equal(6, IslandSizes.FlushShoulder(Island.IslandState.Pill, Geometry.PillKind.Compact));
        Assert.Equal(6, IslandSizes.FlushShoulder(Island.IslandState.Peek, Geometry.PillKind.Idle));
        Assert.Equal(8, IslandSizes.FlushShoulder(Island.IslandState.Alert, Geometry.PillKind.Idle));
        Assert.Equal(10, IslandSizes.FlushShoulder(Island.IslandState.Expanded, Geometry.PillKind.Idle));
        Assert.Equal(9, IslandSizes.FlushRadius(Island.IslandState.Pill, Geometry.PillKind.Idle, theme));
        Assert.Equal(12, IslandSizes.FlushRadius(Island.IslandState.Pill, Geometry.PillKind.Compact, theme));
        Assert.Equal(14, IslandSizes.FlushRadius(Island.IslandState.Peek, Geometry.PillKind.Idle, theme));
        Assert.Equal(20, IslandSizes.FlushRadius(Island.IslandState.Alert, Geometry.PillKind.Idle, theme));
        Assert.Equal(IslandSizes.CornerRadius(theme), IslandSizes.FlushRadius(Island.IslandState.Expanded, Geometry.PillKind.Idle, theme));
    }

    [Fact]
    public void TheIdleClockAndTheTitleAreOffByDefault()
    {
        Assert.False(new AppSettings().ShowIdleClock);
        Assert.False(new AppSettings().ShowPillTitle);
        Assert.True(new AppSettings().HideInFullscreen);
    }

    private sealed class NoLocator : IFileLocator
    {
        public string? IdOf(string path) => null;
        public string? PathOf(string id) => null;
    }
}
