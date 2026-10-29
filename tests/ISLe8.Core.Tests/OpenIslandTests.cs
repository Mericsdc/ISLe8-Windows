using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Settings;
using ISLe8.Core.Theming;

namespace ISLe8.Core.Tests;

/// <summary>The open island: its sizes per layout, its tabs, swipes and the slack (PORTING.md §2).</summary>
public class OpenIslandTests
{
    private readonly ManualClock _clock = new();
    private readonly AppSettings _settings = new();
    private readonly IslandModel _model;

    public OpenIslandTests()
    {
        _model = new IslandModel(_settings, _clock);
    }

    private void Wait(double milliseconds) => _clock.Advance(TimeSpan.FromMilliseconds(milliseconds));

    // MARK: Sizes

    [Theory]
    [InlineData(HomeStyle.Dashboard, IslandTab.Home, 820, 396)]
    [InlineData(HomeStyle.Dashboard, IslandTab.Devices, 820, 396)]
    [InlineData(HomeStyle.Compact, IslandTab.Home, 870, 192)]
    [InlineData(HomeStyle.Compact, IslandTab.Shelf, 870, 288)]
    [InlineData(HomeStyle.Minimal, IslandTab.Home, 720, 156)]
    [InlineData(HomeStyle.Minimal, IslandTab.Agents, 720, 288)]
    public void EachLayoutHasTheMacsSizes(HomeStyle style, IslandTab tab, double width, double height)
    {
        _settings.HomeStyle = style;

        Assert.Equal(new DipSize(width, height), IslandSizes.Open(_settings, tab));
    }

    [Fact]
    public void OnlyTheWideLayoutFollowsTheWidthSetting()
    {
        _settings.ExpandedWidth = 980;
        Assert.Equal(980, IslandSizes.Width(_settings));

        _settings.HomeStyle = HomeStyle.Compact;
        Assert.Equal(870, IslandSizes.Width(_settings));
    }

    [Fact]
    public void TheWindowIsSizedForTheTallestTab()
    {
        _settings.HomeStyle = HomeStyle.Minimal;

        Assert.Equal(new DipSize(720, 288), IslandSizes.Largest(_settings));
    }

    [Theory]
    [InlineData(16, 28)] // Ember, Notch Black
    [InlineData(14, 26)] // Graphite
    [InlineData(6, 18)] // LRX Mono
    public void TheOpenCornersFollowTheTheme(double themeCorner, double islandCorner)
    {
        Assert.Equal(islandCorner, IslandSizes.CornerRadius(IslandTheme.NotchBlack with { CornerRadius = themeCorner }));
    }

    [Fact]
    public void AShorterTabHangsFromTheSameTopInTheMiddle()
    {
        _settings.HomeStyle = HomeStyle.Minimal;
        var layout = IslandLayout.Compute(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 1, IslandSizes.Largest(_settings));

        var home = layout.Open(IslandSizes.Open(_settings, IslandTab.Home));

        Assert.Equal(layout.Expanded.Y, home.Y);
        Assert.Equal(156, home.Height);
        Assert.Equal(layout.Area.Width / 2, home.X + home.Width / 2, precision: 6);
    }

    // MARK: Settings

    [Fact]
    public void TabsReadFromAFileAreCompletedAndOneAlwaysStays()
    {
        var settings = SettingsStore.Import("""{ "tabOrder": ["devices", "gone", "home"], "hiddenTabs": ["home", "shelf", "clipboard", "agents", "devices"] }""");

        Assert.Equal(["devices", "home", "shelf", "clipboard", "agents"], settings.TabOrder);
        Assert.Empty(settings.HiddenTabs);
    }

    [Fact]
    public void TheDockShowsTheChosenTabsInOrder()
    {
        _settings.TabOrder = ["agents", "home", "shelf", "clipboard", "devices"];
        _settings.HiddenTabs = ["shelf", "devices"];

        Assert.Equal([IslandTab.Agents, IslandTab.Home, IslandTab.Clipboard], IslandTabs.Visible(_settings));
    }

    [Fact]
    public void LayoutSettingsAreBroughtIntoRange()
    {
        var settings = SettingsStore.Import("""{ "homeStyle": "MINIMAL", "expandedWidth": 2000 }""");

        Assert.Equal(HomeStyle.Minimal, settings.HomeStyle);
        Assert.Equal(980, settings.ExpandedWidth);
        Assert.Equal(HomeStyle.Dashboard, SettingsStore.Import("""{ "homeStyle": "huge" }""").HomeStyle);
    }

    // MARK: Tabs

    private void OpenAndClose()
    {
        _model.Click();
        _model.Close();
    }

    [Fact]
    public void ReopeningSoonReturnsToTheLastTab()
    {
        _model.Click();
        _model.Select(IslandTab.Clipboard);
        _model.Close();
        Wait(14_000);
        _model.Click();

        Assert.Equal(IslandTab.Clipboard, _model.Tab);
    }

    [Fact]
    public void ReopeningLaterStartsAtTheFirstTab()
    {
        _settings.TabOrder = ["agents", "home", "shelf", "clipboard", "devices"];
        _model.Click();
        _model.Select(IslandTab.Clipboard);
        _model.Close();
        Wait(15_001);
        _model.Click();

        Assert.Equal(IslandTab.Agents, _model.Tab);
    }

    [Fact]
    public void AHiddenTabGivesWayToTheFirst()
    {
        _model.Click();
        _model.Select(IslandTab.Shelf);
        _settings.HiddenTabs = ["shelf"];
        _model.TabsChanged();

        Assert.Equal(IslandTab.Home, _model.Tab);
    }

    [Fact]
    public void SwitchingTabsKeepsTheIslandOpen()
    {
        var states = new List<IslandState>();
        _model.StateChanged += _ => states.Add(_model.State);
        _model.Click();

        foreach (var tab in new[] { IslandTab.Shelf, IslandTab.Clipboard, IslandTab.Agents, IslandTab.Devices, IslandTab.Home })
        {
            _model.Select(tab);
        }

        Assert.Equal([IslandState.Expanded], states);
        Assert.Equal(IslandTab.Home, _model.Tab);
    }

    [Fact]
    public void SwipingMovesOneTabAndStopsAtTheEnds()
    {
        _model.Click();
        _model.SelectAdjacent(-1);
        Assert.Equal(IslandTab.Home, _model.Tab);

        _model.SelectAdjacent(1);
        Assert.Equal(IslandTab.Shelf, _model.Tab);

        _model.Close();
        _model.SelectAdjacent(1);
        Assert.Equal(IslandTab.Shelf, _model.Tab);
    }

    [Fact]
    public void ATabChangeSaysWhichTabItLeft()
    {
        var left = new List<IslandTab>();
        _model.TabChanged += old => left.Add(old);
        OpenAndClose();
        _model.Click();
        _model.Select(IslandTab.Devices);

        Assert.Equal([IslandTab.Home], left);
    }

    // MARK: Swipes

    [Fact]
    public void ASwipeCountsOnceItIsLongEnough()
    {
        var swipe = new SwipeTracker();
        var now = DateTimeOffset.UnixEpoch;

        Assert.Equal(0, swipe.Add(60, now));
        Assert.Equal(1, swipe.Add(60, now.AddMilliseconds(16)));
        // The same swipe keeps going: still one tab.
        Assert.Equal(0, swipe.Add(500, now.AddMilliseconds(32)));
        // After a pause, a new swipe the other way.
        Assert.Equal(-1, swipe.Add(-120, now.AddMilliseconds(400)));
    }

    [Fact]
    public void SmallTwitchesNeverAddUpAcrossPauses()
    {
        var swipe = new SwipeTracker();
        var now = DateTimeOffset.UnixEpoch;

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(0, swipe.Add(60, now.AddMilliseconds(i * 300)));
        }
    }

    // MARK: Slack

    private static readonly PixelRect Tall = new(0, 0, 600, 288);
    private static readonly PixelRect Short = new(0, 0, 600, 156);

    [Fact]
    public void AShrinkingIslandKeepsThePointerThatClickedTheDock()
    {
        var outline = new HoverOutline();
        outline.Reset(Tall);

        outline.Resize(Short, 300, 260);

        Assert.True(outline.Contains(300, 260));
        Assert.Equal(Tall, outline.Slack);
    }

    [Fact]
    public void TheSlackEndsOnEnteringTheNewOutlineOrLeavingBoth()
    {
        var outline = new HoverOutline();
        outline.Reset(Tall);
        outline.Resize(Short, 300, 260);

        Assert.True(outline.Contains(300, 100));
        Assert.Null(outline.Slack);
        Assert.False(outline.Contains(300, 260));

        outline.Reset(Tall);
        outline.Resize(Short, 300, 260);
        Assert.False(outline.Contains(300, 400));
        Assert.False(outline.Contains(300, 260));
    }

    [Fact]
    public void GrowingOrAPointerAlreadyInsideNeedsNoSlack()
    {
        var outline = new HoverOutline();
        outline.Reset(Short);
        outline.Resize(Tall, 300, 100);
        Assert.Null(outline.Slack);

        outline.Resize(Short, 300, 100);
        Assert.Null(outline.Slack);
    }
}
