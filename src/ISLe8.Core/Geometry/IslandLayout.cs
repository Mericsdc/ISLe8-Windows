namespace ISLe8.Core.Geometry;

/// <summary>What the wide pill shows in its ears.</summary>
public enum PillActivity
{
    None,
    Media,
    Timer,
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

    /// <summary><see cref="ReservedHeight"/> on a screen at <paramref name="scale"/>.</summary>
    public static int ReservedPixels(double scale) => (int)Math.Ceiling(ReservedHeight * (scale > 0 ? scale : 1));
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

    public DipRect Pill(PillKind kind) => kind == PillKind.Compact ? CompactPill : IdlePill;

    /// <summary>
    /// The open island at <paramref name="size"/> (one tab's, <see cref="IslandSizes.Open"/>): centred,
    /// hanging from the pill's top, never bigger than <see cref="Expanded"/>, the largest the window was
    /// sized for. Relative to <see cref="Area"/>.
    /// </summary>
    public DipRect Open(DipSize size)
    {
        var width = Math.Min(size.Width, Expanded.Width);
        var height = Math.Min(size.Height, Expanded.Height);
        return new DipRect((Area.Width - width) / 2, IslandMetrics.TopMargin, width, height);
    }

    /// <summary>An open island in window pixels: the part of the window that counts as "on the island".</summary>
    public PixelRect OpenRegion(DipRect open) => Clip(open.Offset(Area.X, Area.Y).ToPixels(Scale));

    /// <summary>The pill swollen a little under a resting pointer, still centred and hanging from the same top. Relative to <see cref="Area"/>.</summary>
    public DipRect Peek(PillKind kind)
    {
        var pill = Pill(kind);
        var width = Math.Min(pill.Width + IslandMetrics.PeekGrowth.Width, Expanded.Width);
        return new DipRect(pill.X - (width - pill.Width) / 2, pill.Y, width, pill.Height + IslandMetrics.PeekGrowth.Height);
    }

    /// <summary>
    /// The part of the window that takes the mouse while the island is a pill: the pill itself,
    /// stretched up to the top edge so flinging the mouse to the top of the screen still hits it.
    /// Relative to the window, in physical pixels.
    /// </summary>
    public PixelRect PillRegion(PillKind kind) => TopAnchoredRegion(Pill(kind));

    /// <summary>The same for the peek, so the swollen pill keeps the pointer that swelled it.</summary>
    public PixelRect PeekRegion(PillKind kind) => TopAnchoredRegion(Peek(kind));

    private PixelRect TopAnchoredRegion(DipRect shape)
    {
        var rect = shape.Offset(Area.X, Area.Y);
        var slop = IslandMetrics.HitSlop;
        return Clip(new DipRect(rect.X - slop, 0, rect.Width + 2 * slop, rect.Bottom + slop).ToPixels(Scale));
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
    public static IslandLayout Compute(PixelRect monitor, PixelRect workArea, double scale, DipSize expanded)
    {
        if (!(scale > 0)) scale = 1;

        var top = Math.Max(workArea.Y, monitor.Y);
        var monitorWidth = monitor.Width / scale;
        var usableHeight = (Math.Min(workArea.Bottom, monitor.Bottom) - top) / scale;

        var width = Math.Min(expanded.Width, Math.Max(IslandMetrics.CompactPillWidth, monitorWidth - 2 * IslandMetrics.SideInset));
        var height = Math.Min(expanded.Height, Math.Max(IslandMetrics.PillHeight, usableHeight - IslandMetrics.TopMargin - IslandMetrics.SideInset));

        var canvasWidth = Math.Min(width + 2 * IslandMetrics.ShadowMargin, monitorWidth);
        var canvasHeight = IslandMetrics.TopMargin + height + IslandMetrics.ShadowMargin;
        var windowWidth = Math.Min(Pixels(canvasWidth, scale), monitor.Width);
        var windowHeight = Pixels(canvasHeight, scale);

        var canvas = new DipSize(windowWidth / scale, windowHeight / scale);
        DipRect Pill(double pillWidth)
        {
            pillWidth = Math.Min(pillWidth, width);
            return new DipRect((width - pillWidth) / 2, IslandMetrics.TopMargin, pillWidth, IslandMetrics.PillHeight);
        }

        return new IslandLayout
        {
            Window = new PixelRect(monitor.X + (monitor.Width - windowWidth) / 2, top, windowWidth, windowHeight),
            Scale = scale,
            Canvas = canvas,
            Area = new DipRect((canvas.Width - width) / 2, 0, width, IslandMetrics.TopMargin + height),
            IdlePill = Pill(IslandMetrics.IdlePillWidth),
            CompactPill = Pill(IslandMetrics.CompactPillWidth),
            Expanded = new DipRect(0, IslandMetrics.TopMargin, width, height),
        };
    }

    private static int Pixels(double dips, double scale) =>
        (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);
}
