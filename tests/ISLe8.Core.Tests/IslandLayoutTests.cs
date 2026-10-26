using ISLe8.Core.Geometry;

namespace ISLe8.Core.Tests;

public class IslandLayoutTests
{
    private static readonly DipSize Expanded = IslandMetrics.DefaultExpanded;

    private static IslandLayout On(int width, int height, double scale, int x = 0, int y = 0) =>
        IslandLayout.Compute(new PixelRect(x, y, width, height), new PixelRect(x, y, width, height - 48), scale, Expanded);

    [Theory]
    [InlineData(1920, 1080, 1.00)] // 1080p
    [InlineData(1920, 1080, 1.25)]
    [InlineData(2560, 1440, 1.00)] // 2K
    [InlineData(2560, 1440, 1.25)]
    [InlineData(2560, 1440, 1.50)]
    [InlineData(3840, 2160, 1.50)] // 4K
    [InlineData(3840, 2160, 1.75)]
    [InlineData(3840, 2160, 2.00)]
    public void PillHasTheSameSizeInDipsAndScalesInPixels(int width, int height, double scale)
    {
        var layout = On(width, height, scale);

        Assert.Equal(IslandMetrics.IdlePillWidth, layout.IdlePill.Width);
        Assert.Equal(IslandMetrics.PillHeight, layout.IdlePill.Height);

        // The mouse target: the pill plus its slop, from the very top of the screen.
        var region = layout.PillRegion(PillKind.Idle);
        var targetWidth = (IslandMetrics.IdlePillWidth + 2 * IslandMetrics.HitSlop) * scale;
        var targetHeight = (IslandMetrics.TopMargin + IslandMetrics.PillHeight + IslandMetrics.HitSlop) * scale;
        Assert.Equal(0, region.Y);
        Assert.InRange(region.Width, targetWidth, targetWidth + 2);
        Assert.InRange(region.Height, targetHeight, targetHeight + 1);
    }

    [Theory]
    [InlineData(1920, 1080, 1.00)]
    [InlineData(2560, 1440, 1.25)]
    [InlineData(3840, 2160, 1.50)]
    [InlineData(3840, 2160, 2.00)]
    public void WindowIsCenteredAtTheTopAndInsideTheMonitor(int width, int height, double scale)
    {
        var layout = On(width, height, scale);

        Assert.Equal(0, layout.Window.Y);
        Assert.InRange(layout.Window.X + layout.Window.Width / 2, width / 2 - 1, width / 2 + 1);
        Assert.True(layout.Window.X >= 0 && layout.Window.Right <= width);

        // The pill sits in the middle of the window, so in the middle of the screen.
        var pill = layout.PillRegion(PillKind.Idle);
        var pillCenter = layout.Window.X + pill.X + pill.Width / 2.0;
        Assert.InRange(pillCenter, width / 2.0 - 1, width / 2.0 + 1);
    }

    [Fact]
    public void ExactPixelsAt1080p100()
    {
        var layout = On(1920, 1080, 1.0);

        Assert.Equal(new PixelRect(644, 0, 632, 202), layout.Window);
        Assert.Equal(new PixelRect(214, 0, 204, 40), layout.PillRegion(PillKind.Idle));
        Assert.Equal(new PixelRect(184, 0, 264, 40), layout.PillRegion(PillKind.Compact));
        Assert.Equal(new PixelRect(36, 0, 560, 166), layout.AreaRegion);
    }

    [Fact]
    public void ExactPixelsAt4K150()
    {
        var layout = On(3840, 2160, 1.5);

        Assert.Equal(948, layout.Window.Width);
        Assert.Equal(new PixelRect(321, 0, 306, 60), layout.PillRegion(PillKind.Idle));
    }

    [Fact]
    public void WorksOnAMonitorLeftOfTheMainOne()
    {
        var layout = On(2560, 1440, 1.25, x: -2560, y: -200);

        Assert.Equal(-200, layout.Window.Y);
        Assert.InRange(layout.Window.X + layout.Window.Width / 2, -1280 - 1, -1280 + 1);
    }

    [Fact]
    public void TopTaskbarPushesTheIslandBelowIt()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);
        var workArea = new PixelRect(0, 48, 1920, 1032);

        var layout = IslandLayout.Compute(monitor, workArea, 1.0, Expanded);

        Assert.Equal(48, layout.Window.Y);
    }

    [Fact]
    public void ShrinksToFitASmallScreen()
    {
        // 1024 px at 125 % is 819 DIPs wide; a 980 DIP island must shrink.
        var layout = IslandLayout.Compute(new PixelRect(0, 0, 1024, 768), new PixelRect(0, 0, 1024, 728), 1.25, new DipSize(980, 900));

        Assert.True(layout.Expanded.Width <= 1024 / 1.25 - 2 * IslandMetrics.SideInset + 0.001);
        Assert.True(layout.Window.Right <= 1024);
        Assert.True(layout.Window.Bottom <= 768);
    }

    [Fact]
    public void ExpandedRegionIsTheWholeWindow()
    {
        var layout = On(2560, 1440, 1.25);

        Assert.Equal(new PixelRect(0, 0, layout.Window.Width, layout.Window.Height), layout.ExpandedRegion);
    }

    [Fact]
    public void IgnoresANonsenseScale()
    {
        var layout = IslandLayout.Compute(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), double.NaN, Expanded);

        Assert.Equal(1, layout.Scale);
    }
}
