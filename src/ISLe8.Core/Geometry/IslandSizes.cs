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
    /// A banner: the wide pill plus 60, at least 340, and the pill's row plus 46 for its content;
    /// notifications and meetings, with more to say, the pill plus 200, at least 400, and the row
    /// plus 74 (IslandModel.swift, size(for: .alert)). On the Mac the pill's row stays empty under the notch; with no
    /// notch here that row was only a black band (at the bottom on the taskbar), so a banner is its content and
    /// <see cref="BannerInset"/> (PORTING.md §2).
    /// </summary>
    public static DipSize Alert(IslandAlert alert) => alert switch
    {
        HudAlert => new(HudWidth, HeaderHeight),
        _ when alert.IsStatus() => new(Notch + 2 * StatusEarWidth + 2 * EarShoulder, HeaderHeight),
        NotificationAlert or MeetingAlert => new(Math.Max(400, Notch + 200), BannerInset + 74),
        DeviceAlert => new(Math.Max(420, Notch + 220), BannerInset + 56),
        ApprovalAlert => new(Math.Max(440, Notch + 240), BannerInset + 100),
        AgentDoneAlert => new(Math.Max(400, Notch + 200), BannerInset + 50),
        _ => new(Math.Max(IslandMetrics.CompactPillWidth + 60, 340), BannerInset + 46),
    };

    /// <summary>What a banner adds to its content row, in place of the pill's row: 5 above it and 5 below, less the 4 the row had below.</summary>
    public const double BannerInset = 6;

    /// <summary>
    /// Volume and brightness in one row a little wider than the pill: the icon, the bar, the figure. The Mac puts
    /// them in ears either side of the notch (IslandModel.swift, hudEarWidth 92); with no notch here that left an
    /// empty black middle and a HUD twice the size it needs.
    /// </summary>
    public const double HudWidth = 260;

    /// <summary>Ears for "charging" and "LAN": the island widens sideways to say it, as on iPhone (statusEarWidth).</summary>
    public const double StatusEarWidth = 104;

    /// <summary>The shoulder beside each ear, the wide pill's (shoulder(for: .compact)).</summary>
    public const double EarShoulder = 6;

    /// <summary>The middle the ears sit either side of: the idle pill, as the notch on the Mac.</summary>
    public const double EarGap = Notch;

    /// <summary>The drop target's two zones under the pill's row (IslandModel.swift, size(for: .dropTarget)).</summary>
    public static DipSize DropTarget => new(Math.Max(460, Notch + 240), HeaderHeight + 112);

    /// <summary>A banner's corners: the Mac's bottom radius for alerts (IslandModel.swift, bottomRadius).</summary>
    public const double AlertCornerRadius = 20;

    /// <summary>The open island follows the theme's corners: sharp themes get a crisper outline.</summary>
    public static double CornerRadius(IslandTheme theme) => Math.Min(theme.CornerRadius + 12, 28);

    /// <summary>
    /// Flush with the top of the screen, the island is the Mac's notch shape (IslandShape.swift): it
    /// flares into the top edge with two concave shoulders this wide (IslandModel.swift, shoulder(for:)).
    /// </summary>
    public static double FlushShoulder(IslandState state, PillKind pill) => state switch
    {
        IslandState.Pill => pill == PillKind.Compact ? 6 : 0,
        IslandState.Peek => 6,
        IslandState.Alert => 8,
        _ => 10,
    };

    /// <summary>…and rounds off at the bottom with these (IslandModel.swift, bottomRadius(for:)).</summary>
    public static double FlushRadius(IslandState state, PillKind pill, IslandTheme theme) => state switch
    {
        IslandState.Pill => pill == PillKind.Compact ? 12 : 9,
        IslandState.Peek => 14,
        IslandState.Alert => AlertCornerRadius,
        _ => CornerRadius(theme),
    };
}
