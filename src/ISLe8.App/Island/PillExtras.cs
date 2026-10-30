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
/// "144 FPS   CPU %23 54°   GPU %61 64°", each figure left out when this computer can't say it.
/// It changes once a second, when the sampler has a new sample.
/// </summary>
internal sealed class PerformanceEar
{
    private readonly TextBlock _text = Ui.Text(11, semibold: true);

    public PerformanceEar()
    {
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        _text.VerticalAlignment = VerticalAlignment.Center;
        _text.TextTrimming = TextTrimming.None;
        StatusGlyphs.Tabular(_text);
        Root = new Grid { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Children = { _text } };
    }

    public Grid Root { get; }

    public void SetShown(bool shown) => Root.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

    public void Show(Core.SystemInfo.PerformanceSample? sample, CultureInfo culture)
    {
        _text.Inlines.Clear();
        if (sample is null) return;
        if (sample.Fps is { } fps) Add($"{fps}", " FPS", null, labelFirst: false);
        Add(Core.SystemInfo.PerformanceMath.Percent(sample.Cpu, culture), "CPU ", sample.CpuTemperature, labelFirst: true);
        if (sample.Gpu is not null || sample.GpuTemperature is not null)
        {
            Add(sample.Gpu is { } gpu ? Core.SystemInfo.PerformanceMath.Percent(gpu, culture) : "", "GPU ", sample.GpuTemperature, labelFirst: true);
        }
    }

    private void Add(string value, string label, double? degrees, bool labelFirst)
    {
        if (_text.Inlines.Count > 0) _text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "   " });
        var dim = new Microsoft.UI.Xaml.Documents.Run { Text = label, Foreground = Ui.White(0x8C) };
        var bright = new Microsoft.UI.Xaml.Documents.Run { Text = value };
        if (labelFirst)
        {
            _text.Inlines.Add(dim);
            _text.Inlines.Add(bright);
        }
        else
        {
            _text.Inlines.Add(bright);
            _text.Inlines.Add(dim);
        }
        if (degrees is { } celsius)
        {
            _text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = (value.Length > 0 ? " " : "") + Core.SystemInfo.PerformanceMath.Degrees(celsius) });
        }
    }
}
