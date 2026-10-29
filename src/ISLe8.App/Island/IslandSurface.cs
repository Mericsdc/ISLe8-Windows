using System.Numerics;
using ISLe8.Core.Geometry;
using ISLe8.Core.Motion;
using ISLe8.Core.Theming;
using ISLe8.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace ISLe8.Island;

/// <summary>
/// The island's shape, drawn and animated by the compositor: one rounded rectangle that springs
/// between pill and open sizes, its shadow, and a clip that shapes the content the same way. It is
/// black; while the island is open or showing a banner the theme fades in over the black, never
/// replacing it, so the island is never see-through halfway (IslandView.swift, IslandBackground).
/// Once an animation starts the UI thread has nothing to do, and at rest nothing runs at all.
/// </summary>
internal sealed class IslandSurface
{
    private const float ShadowOpacity = 0.55f;

    private readonly Compositor _compositor;
    private readonly CompositionRoundedRectangleGeometry _shapeGeometry;
    private readonly CompositionRoundedRectangleGeometry _clipGeometry;
    private readonly CompositionSpriteShape _shape;
    private readonly ShapeVisual _shapeVisual;
    private readonly CompositionSpriteShape _themeShape;
    private readonly ShapeVisual _themeVisual;
    private readonly ContainerVisual _root;
    private readonly ShapeVisual _maskVisual;
    private readonly CompositionVisualSurface _maskSurface;
    private readonly SpriteVisual _shadowVisual;
    private readonly DropShadow _shadow;
    private readonly bool _animationsEnabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private IslandTheme _theme = IslandTheme.NotchBlack;
    private IslandLayout? _layout;
    private Vector2 _areaOffset;
    private int _morphs;
    private int _crossfades;
    private bool _open;

    /// <param name="backgroundHost">Fills the window; the shape and its shadow are drawn into it.</param>
    /// <param name="area">The island column; its content is clipped to the shape.</param>
    public IslandSurface(UIElement backgroundHost, UIElement area)
    {
        _compositor = ElementCompositionPreview.GetElementVisual(backgroundHost).Compositor;

        _shapeGeometry = _compositor.CreateRoundedRectangleGeometry();
        _shape = _compositor.CreateSpriteShape(_shapeGeometry);
        _shape.FillBrush = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
        _shapeVisual = _compositor.CreateShapeVisual();
        _shapeVisual.Shapes.Add(_shape);
        _themeShape = _compositor.CreateSpriteShape(_shapeGeometry);
        _themeVisual = _compositor.CreateShapeVisual();
        _themeVisual.Shapes.Add(_themeShape);
        _themeVisual.Opacity = 0;

        // The shadow is cast by an empty sprite through a mask: a live copy of the same shape. A
        // LayerVisual would be simpler, but in a per-pixel-alpha window it draws as an opaque,
        // unscaled black box that hides the pill.
        _maskVisual = _compositor.CreateShapeVisual();
        var mask = _compositor.CreateSpriteShape(_shapeGeometry);
        mask.FillBrush = _compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, 0, 0, 0));
        _maskVisual.Shapes.Add(mask);
        _maskSurface = _compositor.CreateVisualSurface();
        _maskSurface.SourceVisual = _maskVisual;

        _shadow = _compositor.CreateDropShadow();
        // Blur + offset stay within IslandMetrics.ShadowMargin, or the window edge cuts the shadow.
        _shadow.BlurRadius = 24;
        _shadow.Offset = new Vector3(0, 8, 0);
        _shadow.Color = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        _shadow.Opacity = 0;
        _shadow.Mask = _compositor.CreateSurfaceBrush(_maskSurface);

        _shadowVisual = _compositor.CreateSpriteVisual();
        _shadowVisual.Shadow = _shadow;

        _root = _compositor.CreateContainerVisual();
        _root.Children.InsertAtTop(_shadowVisual);
        _root.Children.InsertAtTop(_shapeVisual);
        _root.Children.InsertAtTop(_themeVisual);
        ElementCompositionPreview.SetElementChildVisual(backgroundHost, _root);

        _clipGeometry = _compositor.CreateRoundedRectangleGeometry();
        ElementCompositionPreview.GetElementVisual(area).Clip = _compositor.CreateGeometricClip(_clipGeometry);
    }

    public void SetLayout(IslandLayout layout)
    {
        _layout = layout;
        var canvas = new Vector2((float)layout.Canvas.Width, (float)layout.Canvas.Height);
        _root.Size = canvas;
        _shapeVisual.Size = canvas;
        _themeVisual.Size = canvas;
        _maskVisual.Size = canvas;
        _maskSurface.SourceSize = canvas;
        _shadowVisual.Size = canvas;
        _areaOffset = new Vector2((float)layout.Area.X, (float)layout.Area.Y);
        ApplyBrush();
    }

    public void SetTheme(IslandTheme theme)
    {
        _theme = theme;
        ApplyBrush();
    }

    /// <summary>Puts the shape at <paramref name="rect"/> (relative to the island column) at once.</summary>
    public void JumpTo(DipRect rect, double cornerRadius, bool shadow)
    {
        _morphs++;
        Set(_shapeGeometry, rect, cornerRadius, _areaOffset);
        Set(_clipGeometry, rect, cornerRadius, Vector2.Zero);
        _shadow.StopAnimation("Opacity");
        _shadow.Opacity = shadow ? ShadowOpacity : 0;
    }

    /// <summary>
    /// Springs the shape to <paramref name="rect"/>. <paramref name="settled"/> runs once it comes to
    /// rest, unless a newer morph took over in the meantime.
    /// </summary>
    public void MorphTo(DipRect rect, double cornerRadius, SpringSpec spring, bool shadow, Action settled)
    {
        var morph = ++_morphs;
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        Animate(_shapeGeometry, rect, cornerRadius, _areaOffset, spring);
        Animate(_clipGeometry, rect, cornerRadius, Vector2.Zero, spring);

        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, shadow ? ShadowOpacity : 0f);
        fade.Duration = TimeSpan.FromMilliseconds(shadow ? 260 : 140);
        _shadow.StartAnimation("Opacity", fade);

        batch.End();
        batch.Completed += (_, _) =>
        {
            if (morph == _morphs) settled();
        };
    }

    /// <summary>Fades an element by its composition visual; XAML's own Opacity is left alone.</summary>
    public void Fade(UIElement element, float to, TimeSpan delay, TimeSpan duration)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        if (!_animationsEnabled)
        {
            delay = TimeSpan.Zero;
            duration = MotionTimings.ReducedMotion;
        }
        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, to);
        fade.Duration = duration;
        fade.DelayTime = delay;
        visual.StartAnimation("Opacity", fade);
    }

    /// <summary>
    /// Changing tab: the old content fades out while the new one fades in (ease-in-out, as on the
    /// Mac). <paramref name="done"/> runs once the old one is gone, unless another crossfade took over.
    /// </summary>
    public void Crossfade(UIElement from, UIElement to, Action done)
    {
        var crossfade = ++_crossfades;
        var easing = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(0.58f, 1));
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        foreach (var (element, target) in new[] { (from, 0f), (to, 1f) })
        {
            var fade = _compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1f, target, easing);
            fade.Duration = MotionTimings.TabCrossfade;
            ElementCompositionPreview.GetElementVisual(element).StartAnimation("Opacity", fade);
        }
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (crossfade == _crossfades) done();
        };
    }

    public static void SetOpacity(UIElement element, float opacity)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        visual.Opacity = opacity;
    }

    /// <summary>
    /// The open island's content, shown (true) or hidden at once, without animating. Hidden, it sits
    /// at the scale it grows from, ready for <see cref="Reveal"/>.
    /// </summary>
    public static void SetContentShown(FrameworkElement content, bool shown)
    {
        var visual = ElementCompositionPreview.GetElementVisual(content);
        visual.StopAnimation("Opacity");
        visual.StopAnimation("Scale");
        visual.Opacity = shown ? 1 : 0;
        visual.Scale = shown ? Vector3.One : new Vector3(MotionTimings.ContentScaleIn, MotionTimings.ContentScaleIn, 1);
    }

    /// <summary>
    /// The open island's content settles in just behind the shell, growing from 94 % at its top edge,
    /// and folds away to 90 % with it, so the island never shrinks as an empty box (IslandView.swift).
    /// With "Animation effects" off it only fades.
    /// </summary>
    public void Reveal(FrameworkElement content, bool show)
    {
        var visual = ElementCompositionPreview.GetElementVisual(content);
        visual.CenterPoint = new Vector3((float)(content.Width / 2), 0, 0);

        var easing = show
            ? _compositor.CreateCubicBezierEasingFunction(new Vector2(0, 0), new Vector2(0.58f, 1)) // ease-out
            : _compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(1, 1)); // ease-in
        var delay = show ? MotionTimings.ContentDelay : TimeSpan.Zero;
        var duration = show ? MotionTimings.ContentFadeIn : MotionTimings.ContentFadeOut;
        if (!_animationsEnabled)
        {
            delay = TimeSpan.Zero;
            duration = MotionTimings.ReducedMotion;
        }

        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, show ? 1f : 0f, easing);
        fade.Duration = duration;
        fade.DelayTime = delay;
        visual.StartAnimation("Opacity", fade);

        if (!_animationsEnabled)
        {
            visual.StopAnimation("Scale");
            visual.Scale = Vector3.One;
            return;
        }
        var to = show ? 1f : MotionTimings.ContentScaleOut;
        var scale = _compositor.CreateVector3KeyFrameAnimation();
        scale.InsertKeyFrame(1f, new Vector3(to, to, 1), easing);
        scale.Duration = duration;
        scale.DelayTime = delay;
        visual.StartAnimation("Scale", scale);
    }

    private void Animate(CompositionRoundedRectangleGeometry geometry, DipRect rect, double cornerRadius, Vector2 offset, SpringSpec spring)
    {
        var position = new Vector2((float)rect.X, (float)rect.Y) + offset;
        var size = new Vector2((float)rect.Width, (float)rect.Height);
        var corner = new Vector2((float)cornerRadius);

        if (!_animationsEnabled)
        {
            // Windows' "Animation effects" is off: a short ease-in-out instead of a spring.
            geometry.StartAnimation("Offset", Eased(position));
            geometry.StartAnimation("Size", Eased(size));
            geometry.StartAnimation("CornerRadius", Eased(corner));
            return;
        }
        geometry.StartAnimation("Offset", Spring(position, spring));
        geometry.StartAnimation("Size", Spring(size, spring));
        geometry.StartAnimation("CornerRadius", Spring(corner, spring));
    }

    private SpringVector2NaturalMotionAnimation Spring(Vector2 target, SpringSpec spring)
    {
        var animation = _compositor.CreateSpringVector2Animation();
        animation.FinalValue = target;
        animation.DampingRatio = (float)spring.DampingFraction;
        animation.Period = spring.CompositionPeriod;
        return animation;
    }

    /// <summary>Instead of a spring when "Animation effects" is off: 160 ms ease-in-out, as on the Mac.</summary>
    private Vector2KeyFrameAnimation Eased(Vector2 target)
    {
        var animation = _compositor.CreateVector2KeyFrameAnimation();
        animation.InsertKeyFrame(1f, target, _compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(0.58f, 1)));
        animation.Duration = MotionTimings.ReducedMotion;
        return animation;
    }

    private static void Set(CompositionRoundedRectangleGeometry geometry, DipRect rect, double cornerRadius, Vector2 offset)
    {
        geometry.StopAnimation("Offset");
        geometry.StopAnimation("Size");
        geometry.StopAnimation("CornerRadius");
        geometry.Offset = new Vector2((float)rect.X, (float)rect.Y) + offset;
        geometry.Size = new Vector2((float)rect.Width, (float)rect.Height);
        geometry.CornerRadius = new Vector2((float)cornerRadius);
    }

    /// <summary>
    /// Open (or a banner): the theme over the black; glass alone lets the black go. Closed: black,
    /// whatever the theme, so the pill matches the screen's top edge.
    /// </summary>
    public void SetOpen(bool open, bool animate)
    {
        _open = open;
        var glass = _theme.Material == IslandMaterial.Glass;
        Animate(_themeVisual, open ? 1 : 0, animate);
        Animate(_shapeVisual, open && glass ? 0 : 1, animate);
    }

    private void Animate(Visual visual, float to, bool animate)
    {
        visual.StopAnimation("Opacity");
        if (!animate)
        {
            visual.Opacity = to;
            return;
        }
        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, to);
        fade.Duration = !_animationsEnabled ? MotionTimings.ReducedMotion : to > 0 ? MotionTimings.ContentFadeIn : MotionTimings.ContentFadeOut;
        visual.StartAnimation("Opacity", fade);
    }

    private void ApplyBrush()
    {
        _themeShape.FillBrush = _theme.Material switch
        {
            IslandMaterial.Gradient or IslandMaterial.Artwork => Gradient(),
            // Acrylic arrives in W5; until then glass is a translucent solid.
            IslandMaterial.Glass => _compositor.CreateColorBrush(_theme.Background.WithAlpha(0xD9).ToColor()),
            _ => _compositor.CreateColorBrush(_theme.Background.ToColor()),
        };
        _themeShape.StrokeBrush = _theme.Border.IsClear ? null : _compositor.CreateColorBrush(_theme.Border.ToColor());
        _themeShape.StrokeThickness = 1;
        SetOpen(_open, animate: false);
    }

    /// <summary>Top colour at the pill, bottom colour at the open island's bottom edge.</summary>
    private CompositionLinearGradientBrush Gradient()
    {
        var brush = _compositor.CreateLinearGradientBrush();
        brush.MappingMode = CompositionMappingMode.Absolute;
        var top = (float)((_layout?.Area.Y ?? 0) + IslandMetrics.TopMargin);
        var bottom = (float)((_layout?.Area.Y ?? 0) + (_layout?.Expanded.Bottom ?? IslandSizes.HeaderHeight));
        brush.StartPoint = new Vector2(0, top);
        brush.EndPoint = new Vector2(0, bottom);
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(0f, _theme.Background.ToColor()));
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(1f, _theme.GradientBottom.ToColor()));
        return brush;
    }
}
