using ISLe8.Core.Island;
using ISLe8.Core.Settings;
using ISLe8.Core.Theming;

namespace ISLe8.Core.Geometry;

/// <summary>
/// The open island's size in each Home layout, after IslandModel.swift (expandedWidth,
/// contentHeight, bottomSpace) and PORTING.md §2: a header row as tall as the pill, the tab's
/// content, then the dock. Within the wide and compact layouts every tab is the same size, so
/// switching tabs never resizes the island; minimal Home is a single strip.
/// </summary>
public static class IslandSizes
{
    /// <summary>The header row (status chips, pin, settings) has the pill's height.</summary>
    public const double HeaderHeight = IslandMetrics.PillHeight;

    /// <summary>Between the header and the content.</summary>
    public const double ContentTop = 6;

    /// <summary>The content's left and right margin.</summary>
    public const double ContentInset = 14;

    public const double DockTop = 8;
    public const double DockHeight = 36;
    public const double DockBottom = 10;

    /// <summary>The dock and the space around it.</summary>
    public const double DockArea = DockTop + DockHeight + DockBottom;

    /// <summary>The Mac sizes the island by its notch; here the idle pill stands in for it.</summary>
    private const double Notch = IslandMetrics.IdlePillWidth;

    public static double Width(AppSettings settings) => settings.HomeStyle switch
    {
        HomeStyle.Compact => Math.Max(640, Notch + 400),
        HomeStyle.Minimal => Math.Max(600, Notch + 380),
        _ => Math.Max(settings.ExpandedWidth, Notch + 480),
    };

    public static double ContentHeight(HomeStyle style, IslandTab tab) => style switch
    {
        HomeStyle.Compact => 196,
        HomeStyle.Minimal => tab == IslandTab.Home ? 64 : 196,
        _ => 304,
    };

    /// <summary>The open island showing <paramref name="tab"/>.</summary>
    public static DipSize Open(AppSettings settings, IslandTab tab) =>
        new(Width(settings), HeaderHeight + ContentTop + ContentHeight(settings.HomeStyle, tab) + DockArea);

    /// <summary>The biggest the island gets in the current layout; the window is sized for it.</summary>
    public static DipSize Largest(AppSettings settings) =>
        Enum.GetValues<IslandTab>().Select(tab => Open(settings, tab)).MaxBy(size => size.Height);

    /// <summary>The open island follows the theme's corners: sharp themes get a crisper outline.</summary>
    public static double CornerRadius(IslandTheme theme) => Math.Min(theme.CornerRadius + 12, 28);
}
