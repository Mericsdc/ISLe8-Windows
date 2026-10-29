using ISLe8.Core.Island;
using ISLe8.Core.Settings;
using ISLe8.Core.Theming;

namespace ISLe8.Core.Geometry;

/// <summary>
/// The open island's size in each Home layout, after IslandModel.swift (expandedWidth,
/// contentHeight, bottomSpace) and PORTING.md §2: a header row as tall as the pill, the tab's
/// content, then the dock. In the wide layout every tab is the same size, so switching tabs never
/// resizes the island; compact and minimal Home are strips as wide as their columns need, and the
/// other tabs are taller than that.
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

    /// <summary>The wide layout's from the setting; the strips' from their columns (<see cref="HomeStrip"/>).</summary>
    public static double Width(AppSettings settings)
    {
        var strip = HomeStrip.Width(settings.HomeTiles, settings.HomeStyle) + HomeStrip.SideRoom;
        return settings.HomeStyle switch
        {
            HomeStyle.Compact => Math.Max(strip, Math.Max(Notch + 440, 640)),
            HomeStyle.Minimal => Math.Max(strip, Math.Max(Notch + 400, 600)),
            _ => Math.Max(settings.ExpandedWidth, Notch + 480),
        };
    }

    public static double ContentHeight(HomeStyle style, IslandTab tab) => style switch
    {
        HomeStyle.Compact => tab == IslandTab.Home ? 100 : 196,
        HomeStyle.Minimal => tab == IslandTab.Home ? 64 : 196,
        _ => 304,
    };

    /// <summary>
    /// Home's columns sit this far in from the island's sides: the content margin on the dashboard,
    /// half the strip's side room in the strips, so the strip is as wide as the Mac's.
    /// </summary>
    public static double HomeInset(HomeStyle style) => style == HomeStyle.Dashboard ? ContentInset : HomeStrip.SideRoom / 2;

    /// <summary>The open island showing <paramref name="tab"/>.</summary>
    public static DipSize Open(AppSettings settings, IslandTab tab) =>
        new(Width(settings), HeaderHeight + ContentTop + ContentHeight(settings.HomeStyle, tab) + DockArea);

    /// <summary>The biggest the island gets in the current layout; the window is sized for it.</summary>
    public static DipSize Largest(AppSettings settings) =>
        Enum.GetValues<IslandTab>().Select(tab => Open(settings, tab)).MaxBy(size => size.Height);

    /// <summary>
    /// A banner: the wide pill plus 60, at least 340, and the pill's row plus 46 for its content
    /// (IslandModel.swift, size(for: .alert)). Its top row stays empty, as the notch's does on the Mac.
    /// </summary>
    public static DipSize Alert(IslandAlert alert) =>
        new(Math.Max(IslandMetrics.CompactPillWidth + 60, 340), HeaderHeight + 46);

    /// <summary>The drop target's two zones under the pill's row (IslandModel.swift, size(for: .dropTarget)).</summary>
    public static DipSize DropTarget => new(Math.Max(460, Notch + 240), HeaderHeight + 112);

    /// <summary>A banner's corners: the Mac's bottom radius for alerts (IslandModel.swift, bottomRadius).</summary>
    public const double AlertCornerRadius = 20;

    /// <summary>The open island follows the theme's corners: sharp themes get a crisper outline.</summary>
    public static double CornerRadius(IslandTheme theme) => Math.Min(theme.CornerRadius + 12, 28);
}
