using ISLe8.Core.Settings;

namespace ISLe8.Core.Geometry;

/// <summary>What the wide pill shows in its ears.</summary>
public enum PillActivity
{
    None,
    Media,
    Timer,
    /// <summary>An agent working or waiting, or a script's activity (PORTING.md §3.12).</summary>
    Agent,
}

public enum PillKind
{
    /// <summary>Nothing going on: a plain pill.</summary>
    Idle,
    /// <summary>Something is live (music playing): a wider pill with room for artwork and bars.</summary>
    Compact,
}

/// <summary>
/// The island's sizes in DIPs. XAML multiplies DIPs by the monitor's scale, so the island looks the
/// same size on a 1080p, 1440p or 4K monitor at any scale from 100 % to 200 %.
/// </summary>
public static class IslandMetrics
{
    /// <summary>Gap between the top of the screen and the pill; the mouse target still reaches the edge.</summary>
    public const double TopMargin = 6;

    /// <summary>
    /// The strip reserved at the top of the screen, as an app bar, so maximised windows start below
    /// the pill: the pill's margin and height and the peek's growth. On the Mac the menu bar is that strip.
    /// </summary>
    public const double ReservedHeight = TopMargin + PillHeight + 6;

    /// <summary>The reserved strip on a screen at <paramref name="scale"/>, for an island <paramref name="gap"/> from its edge.</summary>
    public static int ReservedPixels(double scale, double gap = TopMargin, bool fitsPill = false) =>
        (int)Math.Ceiling((gap + PillHeight + (fitsPill ? 0 : PeekGrowth.Height)) * (scale > 0 ? scale : 1));
    public const double PillHeight = 32;
    public const double IdlePillWidth = 200;
    public const double CompactPillWidth = 260;
    /// <summary>Room around the open island for its shadow (blur 24 + offset 8).</summary>
    public const double ShadowMargin = 36;

    /// <summary>The pill takes the mouse this far beyond its edge, so its hairline and the spring's overshoot aren't cut off.</summary>
    public const double HitSlop = 2;

    /// <summary>In click mode a resting pointer swells the pill by this much (IslandModel.swift, <c>.peek</c>).</summary>
    public static readonly DipSize PeekGrowth = new(18, 6);
    /// <summary>The open island keeps at least this far from the monitor's sides and the work area's bottom.</summary>
    public const double SideInset = 16;
}

/// <summary>
/// Where the island window sits on one monitor and where the island sits inside it.
/// The window never changes size while the island animates: it is as big as the open island plus
/// its shadow, and a window region decides which part of it takes the mouse.
/// </summary>
public sealed record IslandLayout
{
    /// <summary>The window, in physical pixels and screen coordinates.</summary>
    public required PixelRect Window { get; init; }

    /// <summary>Physical pixels per DIP: the monitor's DPI / 96.</summary>
    public required double Scale { get; init; }

    /// <summary>The window's size in DIPs, as XAML sees it.</summary>
    public required DipSize Canvas { get; init; }

    /// <summary>The column the open island fills, in canvas DIPs. It takes the hover.</summary>
    public required DipRect Area { get; init; }

    /// <summary>Relative to <see cref="Area"/>.</summary>
    public required DipRect IdlePill { get; init; }

    /// <summary>Relative to <see cref="Area"/>.</summary>
    public required DipRect CompactPill { get; init; }

    /// <summary>Relative to <see cref="Area"/>.</summary>
    public required DipRect Expanded { get; init; }

    /// <summary>On the taskbar: the island stands on its bottom edge and opens upwards.</summary>
    public bool FromBottom { get; init; }

    /// <summary>Which side of <see cref="Area"/> the island keeps to as it grows: -1 left, 0 centre, 1 right.</summary>
    public int Align { get; init; }

    /// <summary>Flush with the top of the screen: the shape runs off the top, so its top corners are square.</summary>
    public bool Flush { get; init; }

    public DipRect Pill(PillKind kind) => kind == PillKind.Compact ? CompactPill : IdlePill;

    /// <summary>Where a shape <paramref name="width"/> wide starts across <see cref="Area"/>.</summary>
    private double X(double width) => Align switch
    {
        < 0 => 0,
        > 0 => Area.Width - width,
        _ => (Area.Width - width) / 2,
    };

    /// <summary>
    /// The open island at <paramref name="size"/> (one tab's, <see cref="IslandSizes.Open"/>): hanging
    /// from the pill's top (or standing on its bottom, on the taskbar), on the pill's side, never bigger
    /// than <see cref="Expanded"/>, the largest the window was sized for. Relative to <see cref="Area"/>.
    /// </summary>
    public DipRect Open(DipSize size)
    {
        var width = Math.Min(size.Width, Expanded.Width);
        var height = Math.Min(size.Height, Expanded.Height);
        return new DipRect(X(width), FromBottom ? Expanded.Bottom - height : Expanded.Y, width, height);
    }

    /// <summary>An open island in window pixels: the part of the window that counts as "on the island".</summary>
    public PixelRect OpenRegion(DipRect open) => Clip(open.Offset(Area.X, Area.Y).ToPixels(Scale));

    /// <summary>The pill swollen a little under a resting pointer, on the same side and from the same edge. Relative to <see cref="Area"/>.</summary>
    public DipRect Peek(PillKind kind)
    {
        var pill = Pill(kind);
        var width = Math.Min(pill.Width + IslandMetrics.PeekGrowth.Width, Expanded.Width);
        var height = pill.Height + IslandMetrics.PeekGrowth.Height;
        return new DipRect(X(width), FromBottom ? pill.Bottom - height : pill.Y, width, height);
    }

    /// <summary>
    /// The part of the window that takes the mouse while the island is a pill: the pill itself,
    /// stretched to the window's edge (the top of the screen, or the taskbar) so flinging the mouse there still hits it.
    /// Relative to the window, in physical pixels.
    /// </summary>
    public PixelRect PillRegion(PillKind kind) => EdgeAnchoredRegion(Pill(kind));

    /// <summary>The same for the peek, so the swollen pill keeps the pointer that swelled it.</summary>
    public PixelRect PeekRegion(PillKind kind) => EdgeAnchoredRegion(Peek(kind));

    private PixelRect EdgeAnchoredRegion(DipRect shape)
    {
        var rect = shape.Offset(Area.X, Area.Y);
        var slop = IslandMetrics.HitSlop;
        return Clip((FromBottom
            ? new DipRect(rect.X - slop, rect.Y - slop, rect.Width + 2 * slop, Area.Bottom - rect.Y + slop)
            : new DipRect(rect.X - slop, 0, rect.Width + 2 * slop, rect.Bottom + slop)).ToPixels(Scale));
    }

    /// <summary>The open island's column in window pixels: leaving it closes the island.</summary>
    public PixelRect AreaRegion => Clip(Area.ToPixels(Scale));

    /// <summary>The whole window, so the open island's shadow is not cut off.</summary>
    public PixelRect ExpandedRegion => new(0, 0, Window.Width, Window.Height);

    private PixelRect Clip(PixelRect rect) => PixelRect.FromEdges(
        Math.Max(rect.X, 0), Math.Max(rect.Y, 0),
        Math.Min(rect.Right, Window.Width), Math.Min(rect.Bottom, Window.Height));

    /// <param name="monitor">The monitor's bounds, in physical pixels.</param>
    /// <param name="workArea">The monitor's work area; a taskbar docked at the top pushes the island below it.</param>
    /// <param name="scale">The monitor's DPI / 96.</param>
    /// <param name="expanded">The open island's preferred size; shrunk to fit small screens.</param>
    /// <param name="placement">At the top, or on the taskbar (the work area's bottom) at its left, centre or right.</param>
    /// <param name="flushTop">At the top: no gap above the pill.</param>
    public static IslandLayout Compute(PixelRect monitor, PixelRect workArea, double scale, DipSize expanded, IslandPlacement placement = IslandPlacement.Top, bool flushTop = false)
    {
        if (!(scale > 0)) scale = 1;
        var fromBottom = placement != IslandPlacement.Top;
        var flush = flushTop && !fromBottom;
        var gap = flush ? 0 : IslandMetrics.TopMargin;
        var align = placement switch
        {
            IslandPlacement.TaskbarLeft => -1,
            IslandPlacement.TaskbarRight => 1,
            _ => 0,
        };

        var top = Math.Max(workArea.Y, monitor.Y);
        var monitorWidth = monitor.Width / scale;
        var usableHeight = (Math.Min(workArea.Bottom, monitor.Bottom) - top) / scale;

        var width = Math.Min(expanded.Width, Math.Max(IslandMetrics.CompactPillWidth, monitorWidth - 2 * IslandMetrics.SideInset));
        var height = Math.Min(expanded.Height, Math.Max(IslandMetrics.PillHeight, usableHeight - gap - IslandMetrics.SideInset));

        var canvasWidth = Math.Min(width + 2 * IslandMetrics.ShadowMargin, monitorWidth);
        var canvasHeight = gap + height + IslandMetrics.ShadowMargin;
        var windowWidth = Math.Min(Pixels(canvasWidth, scale), monitor.Width);
        var windowHeight = Pixels(canvasHeight, scale);
        var canvas = new DipSize(windowWidth / scale, windowHeight / scale);

        // In a corner the window keeps to the work area and the island to its side of the window,
        // SideInset from the screen's edge; the shadow on that side is cut short.
        var left = Math.Max(workArea.X, monitor.X);
        var right = Math.Min(workArea.Right, monitor.Right);
        var inset = Math.Min(IslandMetrics.SideInset, Math.Max(canvas.Width - width, 0));
        var (windowX, areaX) = align switch
        {
            < 0 => (left, inset),
            > 0 => (right - windowWidth, canvas.Width - inset - width),
            _ when fromBottom => (left + (right - left - windowWidth) / 2, (canvas.Width - width) / 2),
            _ => (monitor.X + (monitor.Width - windowWidth) / 2, (canvas.Width - width) / 2),
        };
        var windowY = fromBottom ? Math.Min(workArea.Bottom, monitor.Bottom) - windowHeight : top;
        var area = fromBottom
            ? new DipRect(areaX, canvas.Height - gap - height, width, gap + height)
            : new DipRect(areaX, 0, width, gap + height);
        var pillY = fromBottom ? height - IslandMetrics.PillHeight : gap;
        DipRect Pill(double pillWidth)
        {
            pillWidth = Math.Min(pillWidth, width);
            var x = align switch { < 0 => 0, > 0 => width - pillWidth, _ => (width - pillWidth) / 2 };
            return new DipRect(x, pillY, pillWidth, IslandMetrics.PillHeight);
        }

        return new IslandLayout
        {
            Window = new PixelRect(windowX, windowY, windowWidth, windowHeight),
            Scale = scale,
            Canvas = canvas,
            Area = area,
            IdlePill = Pill(IslandMetrics.IdlePillWidth),
            CompactPill = Pill(IslandMetrics.CompactPillWidth),
            Expanded = new DipRect(0, fromBottom ? 0 : gap, width, height),
            FromBottom = fromBottom,
            Align = align,
            Flush = flush,
        };
    }

    private static int Pixels(double dips, double scale) =>
        (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);
}
