namespace ISLe8.Core.Geometry;

/// <summary>A rectangle in physical pixels, the unit Win32 uses for monitors, windows and regions.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    public PixelRect Union(PixelRect other) => FromEdges(
        Math.Min(X, other.X), Math.Min(Y, other.Y),
        Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
}

/// <summary>A size in device-independent pixels (1/96 inch), the unit XAML lays out in.</summary>
public readonly record struct DipSize(double Width, double Height);

/// <summary>A rectangle in device-independent pixels.</summary>
public readonly record struct DipRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public DipRect Offset(double dx, double dy) => this with { X = X + dx, Y = Y + dy };

    /// <summary>The smallest pixel rectangle that covers this one at <paramref name="scale"/>.</summary>
    public PixelRect ToPixels(double scale) => PixelRect.FromEdges(
        (int)Math.Floor(X * scale), (int)Math.Floor(Y * scale),
        (int)Math.Ceiling(Right * scale), (int)Math.Ceiling(Bottom * scale));
}
