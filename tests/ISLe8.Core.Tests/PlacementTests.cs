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
    public void InsideTheTaskbarThePillRestsInItsMiddleWhereverItWasPut()
    {
        var layout = IslandLayout.Compute(Monitor, WorkArea, 1, Expanded, IslandPlacement.InTaskbar, taskbarPosition: 0.25);
        var pill = OnScreen(layout, layout.IdlePill);
        var open = OnScreen(layout, layout.Open(new DipSize(640, 200)));

        Assert.True(layout.InTaskbar);
        Assert.True(layout.FromBottom);
        // The 48-pixel taskbar's middle, a quarter of the way along.
        Assert.Equal(1032 + 48 / 2.0, pill.Y + pill.Height / 2, 3);
        Assert.Equal(1920 * 0.25, pill.X + pill.Width / 2, 3);
        // It opens upwards from the pill, on the screen.
        Assert.Equal(pill.Bottom, open.Bottom, 3);
        Assert.True(open.X <= pill.X && open.Right >= pill.Right);
        Assert.True(layout.Window.X >= 0 && layout.Window.Right <= 1920 && layout.Window.Bottom <= 1080);
        // The wide pill keeps the same place as it grows.
        var compact = OnScreen(layout, layout.CompactPill);
        Assert.True(compact.X <= pill.X && compact.Right >= pill.Right);
    }

    [Fact]
    public void InsideTheTaskbarAtItsEndsTheIslandStaysOnTheScreen()
    {
        var left = IslandLayout.Compute(Monitor, WorkArea, 1, Expanded, IslandPlacement.InTaskbar, taskbarPosition: 0);
        var right = IslandLayout.Compute(Monitor, WorkArea, 1, Expanded, IslandPlacement.InTaskbar, taskbarPosition: 1);

        Assert.Equal(IslandMetrics.SideInset, OnScreen(left, left.IdlePill).X, 3);
        Assert.Equal(1920 - IslandMetrics.SideInset, OnScreen(right, right.IdlePill).Right, 3);
        Assert.Equal(IslandMetrics.SideInset, OnScreen(left, left.Open(new DipSize(640, 200))).X, 3);
    }

    [Fact]
    public void ATaskbarThatHidesItselfLeavesThePillStandingOnTheScreensEdge()
    {
        var layout = IslandLayout.Compute(Monitor, Monitor, 1, Expanded, IslandPlacement.InTaskbar, taskbarPosition: 0.5);

        Assert.False(layout.InTaskbar);
        Assert.Equal(1080 - IslandMetrics.TopMargin, OnScreen(layout, layout.IdlePill).Bottom, 3);
    }

    [Fact]
    public void TheWidePillCanTakeAnyWidthOnItsSide()
    {
        var centre = At(IslandPlacement.Top).WithCompactWidth(330);
        var left = At(IslandPlacement.TaskbarLeft).WithCompactWidth(330);

        Assert.Equal(330, centre.CompactPill.Width);
        Assert.Equal(centre.IdlePill.X + centre.IdlePill.Width / 2, centre.CompactPill.X + centre.CompactPill.Width / 2, 3);
        Assert.Equal(left.IdlePill.X, left.CompactPill.X);
        Assert.Same(centre, centre.WithCompactWidth(330));
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
    public void TheStripCanBeJustThePill()
    {
        // At 150 %: the pill and its swell, then the pill alone, flush or 6 DIP down.
        Assert.Equal(57, IslandMetrics.ReservedPixels(1.5, gap: 0));
        Assert.Equal(48, IslandMetrics.ReservedPixels(1.5, gap: 0, fitsPill: true));
        Assert.Equal(57, IslandMetrics.ReservedPixels(1.5, fitsPill: true));
        Assert.False(new AppSettings().StripFitsPill);
        Assert.True(new AppSettings().ReserveTopStrip);
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

    [Fact]
    public void TheThinBarSitsInThePillsMiddleAtItsTopOrHalfwayDownOnTheTaskbar()
    {
        var top = At(IslandPlacement.Top);
        var bar = top.Bar();
        Assert.Equal((140, 6), (bar.Width, bar.Height));
        Assert.Equal(top.IdlePill.Y, bar.Y);
        Assert.Equal(top.IdlePill.X + top.IdlePill.Width / 2, bar.X + bar.Width / 2, 3);

        var taskbar = At(IslandPlacement.TaskbarCenter);
        Assert.Equal(taskbar.IdlePill.Y + taskbar.IdlePill.Height / 2, taskbar.Bar().Y + 3, 3);
    }

    /// <summary>
    /// The user: "tam olarak işaretlediğim yerden algılıyor ... cok engelliyor". The whole pill took the pointer, 51 px down
    /// a 1440p screen at 150 % under a bar 9 px tall; now only the bar and its slop do, out to the screen's edge.
    /// </summary>
    [Fact]
    public void TheThinBarTakesThePointerOnlyAroundItselfOutToTheEdge()
    {
        var flush = IslandLayout.Compute(new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1368), 1.5, Expanded, IslandPlacement.Top, flushTop: true);
        var pill = flush.PillRegion(Geometry.PillKind.Idle);
        var bar = flush.BarRegion();
        Assert.Equal((0, 51), (pill.Y, pill.Height));
        Assert.Equal(0, bar.Y);
        Assert.Equal((IslandMetrics.BarHeight + IslandMetrics.HitSlop) * 1.5, bar.Height);
        Assert.InRange(bar.Width, (IslandMetrics.BarWidth + 2 * IslandMetrics.HitSlop) * 1.5, (IslandMetrics.BarWidth + 2 * IslandMetrics.HitSlop) * 1.5 + 1);
        Assert.Equal(pill.X + pill.Width / 2.0, bar.X + bar.Width / 2.0, 1);

        var top = At(IslandPlacement.Top);
        Assert.Equal((0, IslandMetrics.TopMargin + IslandMetrics.BarHeight + IslandMetrics.HitSlop), (top.BarRegion().Y, (double)top.BarRegion().Height));

        // On the taskbar the bar is halfway down the pill; its target runs from just above it to the bottom.
        var taskbar = At(IslandPlacement.TaskbarCenter);
        var low = taskbar.BarRegion();
        Assert.Equal(taskbar.PillRegion(Geometry.PillKind.Idle).Bottom, low.Bottom);
        Assert.Equal(taskbar.Bar().Y + taskbar.Area.Y - IslandMetrics.HitSlop, low.Y, 1);
    }

    [Fact]
    public void AGameIsWhatGameBarRecognised()
    {
        var games = Games.Set([@"D:\Games\NFS WORLD\nfsw.exe", "  ", null, @"""C:\Program Files\Epic Games\GTAV\GTA5.exe"""]);

        Assert.Equal(2, games.Count);
        Assert.True(Games.IsGame(@"d:\games\nfs world\NFSW.EXE", games));
        Assert.True(Games.IsGame(@"C:\Program Files\Epic Games\GTAV\GTA5.exe", games));
        Assert.False(Games.IsGame(@"C:\Program Files\Google\Chrome\Application\chrome.exe", games));
        Assert.False(Games.IsGame(null, games));
    }
}
