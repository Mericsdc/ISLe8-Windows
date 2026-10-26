using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace ISLe8.Media;

/// <summary>
/// The little equaliser in the pill while music plays. Looping compositor animations: the UI
/// thread does nothing per frame, and the bars stop completely when paused or hidden.
/// </summary>
internal sealed class AudioBars
{
    private const float BarWidth = 3;
    private const float Gap = 2.5f;
    private const float Height = 14;
    private const float Rest = 0.3f;
    private static readonly float[] Peaks = [0.9f, 0.55f, 1f, 0.7f];
    private static readonly int[] PeriodsMs = [820, 640, 950, 720];

    private readonly Compositor _compositor;
    private readonly CompositionColorBrush _brush;
    private readonly SpriteVisual[] _bars;
    private bool _running;

    public AudioBars(UIElement host)
    {
        _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _brush = _compositor.CreateColorBrush(Color.FromArgb(255, 255, 255, 255));

        var root = _compositor.CreateContainerVisual();
        root.Size = new Vector2(Peaks.Length * BarWidth + (Peaks.Length - 1) * Gap, Height);
        _bars = new SpriteVisual[Peaks.Length];
        for (var i = 0; i < _bars.Length; i++)
        {
            var bar = _compositor.CreateSpriteVisual();
            bar.Size = new Vector2(BarWidth, Height);
            bar.Offset = new Vector3(i * (BarWidth + Gap), 0, 0);
            bar.CenterPoint = new Vector3(BarWidth / 2, Height, 0);
            bar.Scale = new Vector3(1, Rest, 1);
            bar.Brush = _brush;
            root.Children.InsertAtTop(bar);
            _bars[i] = bar;
        }
        ElementCompositionPreview.SetElementChildVisual(host, root);
    }

    public static double Width => Peaks.Length * BarWidth + (Peaks.Length - 1) * Gap;

    public void SetColor(Color color) => _brush.Color = color;

    public void Start()
    {
        if (_running) return;
        _running = true;
        for (var i = 0; i < _bars.Length; i++)
        {
            var bounce = _compositor.CreateScalarKeyFrameAnimation();
            bounce.InsertKeyFrame(0f, Rest);
            bounce.InsertKeyFrame(0.5f, Peaks[i]);
            bounce.InsertKeyFrame(1f, Rest);
            bounce.Duration = TimeSpan.FromMilliseconds(PeriodsMs[i]);
            bounce.IterationBehavior = AnimationIterationBehavior.Forever;
            _bars[i].StartAnimation("Scale.Y", bounce);
        }
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        foreach (var bar in _bars)
        {
            bar.StopAnimation("Scale.Y");
            bar.Scale = new Vector3(1, Rest, 1);
        }
    }
}
