namespace ISLe8.Core.Geometry;

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
    public const double PillHeight = 32;
    public const double IdlePillWidth = 200;
    public const double CompactPillWidth = 260;
    public const double PillCornerRadius = PillHeight / 2;
    public const double ExpandedCornerRadius = 24;
    /// <summary>Room around the open island for its shadow (blur 24 + offset 8).</summary>
    public const double ShadowMargin = 36;

    /// <summary>The pill takes the mouse this far beyond its edge, so its hairline and the spring's overshoot aren't cut off.</summary>
    public const double HitSlop = 2;
    /// <summary>The open island keeps at least this far from the monitor's sides and the work area's bottom.</summary>
    public const double SideInset = 16;

    /// <summary>The open island while it only holds the player (W0). Home grows it to 820 × 396 in W1.</summary>
    public static readonly DipSize DefaultExpanded = new(560, 160);
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
    /// The part of the window that takes the mouse while the island is a pill: the pill itself,
    /// stretched up to the top edge so flinging the mouse to the top of the screen still hits it.
    /// Relative to the window, in physical pixels.
    /// </summary>
    public PixelRect PillRegion(PillKind kind)
    {
        var pill = Pill(kind).Offset(Area.X, Area.Y);
        var slop = IslandMetrics.HitSlop;
        return Clip(new DipRect(pill.X - slop, 0, pill.Width + 2 * slop, pill.Bottom + slop).ToPixels(Scale));
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
