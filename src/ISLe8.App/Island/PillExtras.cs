using System.Globalization;
using System.Numerics;
using ISLe8.Status;
using ISLe8.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Island;

/// <summary>
/// The song's title in the middle of the wide pill, between the artwork and the bars (Settings ›
/// Appearance › Island; Windows only: on the Mac the notch sits there). A title that doesn't fit
/// scrolls by, pausing at the start of each pass; the compositor moves it, so nothing ticks here.
/// </summary>
internal sealed class PillTitle
{
    private const double Gap = 32;
    /// <summary>DIPs per second, about a word a second.</summary>
    private const double Speed = 28;
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(1.6);

    private readonly TextBlock _first = Text();
    private readonly TextBlock _second = Text();
    private readonly StackPanel _strip;
    private readonly Compositor _compositor;
    private string _title = "";
    private bool _running;

    public PillTitle()
    {
        _strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Gap, VerticalAlignment = VerticalAlignment.Center, Children = { _first, _second } };
        ElementCompositionPreview.SetIsTranslationEnabled(_strip, true);
        Root = new Grid { VerticalAlignment = VerticalAlignment.Stretch, Children = { _strip } };
        Root.SizeChanged += (_, _) =>
        {
            Root.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, Root.ActualWidth, Root.ActualHeight) };
            Restart();
        };
        _compositor = ElementCompositionPreview.GetElementVisual(_strip).Compositor;
    }

    public Grid Root { get; }

    /// <summary>The title, and whether it may move: only while the wide pill can be seen.</summary>
    public void Show(string? title, bool running)
    {
        title ??= "";
        if (title == _title && running == _running) return;
        _title = title;
        _running = running;
        _first.Text = _second.Text = title;
        Restart();
    }

    private void Restart()
    {
        var visual = ElementCompositionPreview.GetElementVisual(_strip);
        visual.StopAnimation("Translation");
        visual.Properties.InsertVector3("Translation", Vector3.Zero);
        _first.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = _first.DesiredSize.Width;
        var room = Root.ActualWidth;
        var scrolls = room > 0 && width > room;
        _second.Visibility = scrolls ? Visibility.Visible : Visibility.Collapsed;
        _strip.HorizontalAlignment = scrolls ? HorizontalAlignment.Left : HorizontalAlignment.Center;
        if (!scrolls || !_running) return;

        // One pass: wait at the start, then slide by one title and gap, where the copy stands in for it.
        var distance = (float)(width + Gap);
        var travel = TimeSpan.FromSeconds(distance / Speed);
        var whole = Pause + travel;
        var start = (float)(Pause / whole);
        var slide = _compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(start, Vector3.Zero, _compositor.CreateLinearEasingFunction());
        slide.InsertKeyFrame(1, new Vector3(-distance, 0, 0), _compositor.CreateLinearEasingFunction());
        slide.Duration = whole;
        slide.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Translation", slide);
    }

    private static TextBlock Text()
    {
        var text = Ui.Text(11, alpha: 0xD9, semibold: true);
        text.TextTrimming = TextTrimming.None;
        text.VerticalAlignment = VerticalAlignment.Center;
        return text;
    }
}

/// <summary>
/// The time on the idle pill (Settings › Appearance › Island; Windows only). It wakes once a minute,
/// on the minute, and only while it is wanted.
/// </summary>
internal sealed class PillClock
{
    private readonly DispatcherQueueTimer _timer;
    private bool _running;

    public PillClock(DispatcherQueue queue)
    {
        Text = Ui.Text(12, alpha: 0xE6, semibold: true);
        Text.HorizontalAlignment = HorizontalAlignment.Center;
        Text.VerticalAlignment = VerticalAlignment.Center;
        StatusGlyphs.Tabular(Text);
        // Placed by its margin and size, as the other layers are.
        Root = new Grid { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Children = { Text } };
        _timer = queue.CreateTimer();
        _timer.IsRepeating = false;
        _timer.OnTick(() => Tick());
    }

    public Grid Root { get; }
    public TextBlock Text { get; }

    public void SetRunning(bool running)
    {
        if (running == _running) return;
        _running = running;
        if (running) Tick();
        else _timer.Stop();
    }

    public void Stop() => SetRunning(false);

    private void Tick()
    {
        var now = DateTime.Now;
        // The user's own short time format (Windows' region settings), as the taskbar shows it.
        Text.Text = now.ToString("t", CultureInfo.CurrentCulture);
        _timer.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(50);
        _timer.Start();
    }
}

/// <summary>
/// Frames, CPU and GPU across the wide pill (Settings › General › Performance; Windows only):
/// "21:45   144 FPS   CPU %23 54°   GPU %61 64°   RAM %48", as chosen there, each figure left out when this
/// computer can't say it, and the pill as wide as the chosen ones need. It changes once a second, when the
/// sampler has a new sample.
/// </summary>
internal sealed class PerformanceEar
{
    /// <summary>The pill's round ends, either side of the figures.</summary>
    private const double Ends = 2 * 16;

    private readonly TextBlock _text = Text();
    /// <summary>Never shown: the figures at their widest, measured for the pill's width.</summary>
    private readonly TextBlock _probe = Text();
    private (Core.SystemInfo.PerformanceShow, string)? _measured;
    private double _width;

    public PerformanceEar()
    {
        // The pill's layer leaves 5 on the left and 14 on the right for the artwork; the figures sit in the middle.
        Root = new Grid { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Margin = new Thickness(9, 0, 0, 0), Children = { _text } };
    }

    public Grid Root { get; }

    public void SetShown(bool shown) => Root.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>How wide the pill must be for what is chosen at its widest; measured once per choice.</summary>
    public double Width(Core.SystemInfo.PerformanceShow show, CultureInfo culture)
    {
        var key = (show, culture.Name + "/" + CultureInfo.CurrentCulture.Name);
        if (_measured == key) return _width;
        Fill(_probe, Core.SystemInfo.PerformanceMath.Parts(
            Core.SystemInfo.PerformanceMath.Widest, show, frames: true, Core.SystemInfo.PerformanceMath.WidestTime(CultureInfo.CurrentCulture), culture));
        _probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        _measured = key;
        _width = Math.Ceiling(_probe.DesiredSize.Width) + Ends;
        return _width;
    }

    /// <param name="frames">A game is in front, so its frames have a place even before they are counted.</param>
    public void Show(Core.SystemInfo.PerformanceSample? sample, Core.SystemInfo.PerformanceShow show, bool frames, CultureInfo culture)
    {
        if (sample is null)
        {
            _text.Inlines.Clear();
            return;
        }
        // The user's own short time format, as the idle pill's clock and the taskbar show it.
        Fill(_text, Core.SystemInfo.PerformanceMath.Parts(sample, show, frames, DateTime.Now.ToString("t", CultureInfo.CurrentCulture), culture));
    }

    private static TextBlock Text()
    {
        var text = Ui.Text(11, semibold: true);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.TextTrimming = TextTrimming.None;
        StatusGlyphs.Tabular(text);
        return text;
    }

    private static void Fill(TextBlock text, IReadOnlyList<Core.SystemInfo.PerformancePart> parts)
    {
        text.Inlines.Clear();
        foreach (var part in parts)
        {
            if (text.Inlines.Count > 0) text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "   " });
            var dim = new Microsoft.UI.Xaml.Documents.Run { Text = part.Label, Foreground = Ui.White(0x8C) };
            var bright = new Microsoft.UI.Xaml.Documents.Run { Text = part.Value };
            if (part.LabelFirst)
            {
                text.Inlines.Add(dim);
                text.Inlines.Add(bright);
            }
            else
            {
                text.Inlines.Add(bright);
                if (part.Label.Length > 0) text.Inlines.Add(dim);
            }
            if (part.Degrees is { } degrees) text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = (part.Value.Length > 0 ? " " : "") + degrees });
        }
    }
}
