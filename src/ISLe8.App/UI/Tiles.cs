using System.Numerics;
using ISLe8.Core.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.UI;

/// <summary>The open island's cards and the artwork they show.</summary>
internal static class Tiles
{
    /// <summary>A card in the theme's surface, border and corners (Theme.swift, islandTile).</summary>
    public static Border Tile(IslandTheme theme, UIElement child, Thickness padding) => new()
    {
        Background = theme.Surface.ToBrush(),
        BorderBrush = theme.Border.IsClear ? null : theme.Border.ToBrush(),
        BorderThickness = new Thickness(theme.Border.IsClear ? 0 : 1),
        CornerRadius = new CornerRadius(theme.CornerRadius),
        Padding = padding,
        Child = child,
    };

    /// <summary>A song's artwork, or a note on a faint square while there is none (MediaViews.swift, ArtworkView).</summary>
    public static Artwork Artwork(double size, double radius, float shadowBlur = 0, float shadowOffset = 0) => new(size, radius, shadowBlur, shadowOffset);

    /// <summary>
    /// A soft shadow under a rounded square, cast through a mask as the island's own is: a
    /// LayerVisual would draw as a black box in the island's per-pixel-alpha window. Nothing animates.
    /// </summary>
    public static void CastShadow(FrameworkElement host, double size, double radius, float blur, float offsetY, float opacity)
    {
        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        var extent = new Vector2((float)size, (float)size);
        var geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Size = extent;
        geometry.CornerRadius = new Vector2((float)radius, (float)radius);
        var shape = compositor.CreateSpriteShape(geometry);
        shape.FillBrush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
        var mask = compositor.CreateShapeVisual();
        mask.Size = extent;
        mask.Shapes.Add(shape);
        var surface = compositor.CreateVisualSurface();
        surface.SourceVisual = mask;
        surface.SourceSize = extent;

        var dropShadow = compositor.CreateDropShadow();
        dropShadow.BlurRadius = blur;
        dropShadow.Offset = new Vector3(0, offsetY, 0);
        dropShadow.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        dropShadow.Opacity = opacity;
        dropShadow.Mask = compositor.CreateSurfaceBrush(surface);

        var caster = compositor.CreateSpriteVisual();
        caster.Size = extent;
        caster.Shadow = dropShadow;
        ElementCompositionPreview.SetElementChildVisual(host, caster);
    }
}

internal sealed class Artwork
{
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    private readonly Grid _placeholder;

    public Artwork(double size, double radius, float shadowBlur, float shadowOffset)
    {
        _placeholder = new Grid
        {
            Background = Ui.White(0x14),
            Children = { new FontIcon { Glyph = Glyphs.Music, FontSize = size * 0.4, Foreground = Ui.White(0x80) } },
        };
        var picture = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(radius),
            Child = new Grid { Children = { _placeholder, _image } },
        };
        if (shadowBlur <= 0)
        {
            Root = picture;
            return;
        }
        // Black at 35 %, as on the Mac; its blur radius is about twice SwiftUI's shadow radius.
        var caster = new Grid { Width = size, Height = size };
        Tiles.CastShadow(caster, size, radius, shadowBlur, shadowOffset, opacity: 0.35f);
        Root = new Grid { Width = size, Height = size, Children = { caster, picture } };
    }

    public FrameworkElement Root { get; }

    public void Set(ImageSource? source)
    {
        _image.Source = source;
        _placeholder.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }
}
