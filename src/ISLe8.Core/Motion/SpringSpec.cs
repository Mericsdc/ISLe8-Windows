namespace ISLe8.Core.Motion;

/// <summary>
/// A spring in SwiftUI's terms, so the Windows island moves like the Mac one.
/// <see cref="Response"/> is the undamped period in seconds; Composition calls it <c>Period</c>.
/// <see cref="DampingFraction"/> is Composition's <c>DampingRatio</c>.
/// </summary>
public readonly record struct SpringSpec(double Response, double DampingFraction)
{
    /// <summary>Growing: lively, with a little overshoot (ISLe8 for Mac, phase 7).</summary>
    public static readonly SpringSpec Open = new(0.34, 0.78);

    /// <summary>Shrinking: quick and settled.</summary>
    public static readonly SpringSpec Close = new(0.28, 0.95);

    /// <summary>Faster for speed &gt; 1, slower for speed &lt; 1; held to 0.5–2 like the Mac's.</summary>
    public SpringSpec WithSpeed(double speed) => this with { Response = Response / Math.Clamp(speed, 0.5, 2) };

    /// <summary>
    /// The value for Composition's <c>Period</c>, read as 1/ω₀ the way its samples use it (springs
    /// of ~50 ms that visibly bounce for a third of a second). SwiftUI's response is 2π/ω₀.
    /// Checked by eye on Windows; see DESIGN.md, "Riskler".
    /// </summary>
    public TimeSpan CompositionPeriod => TimeSpan.FromSeconds(Response / (2 * Math.PI));
}

/// <summary>The open island's content settling in and folding away, as on the Mac (IslandView.swift).</summary>
public static class MotionTimings
{
    /// <summary>The open island's content arrives this long after the shell starts to grow.</summary>
    public static readonly TimeSpan ContentDelay = TimeSpan.FromMilliseconds(60);
    public static readonly TimeSpan ContentFadeIn = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan ContentFadeOut = TimeSpan.FromMilliseconds(160);

    /// <summary>The content grows from this scale, anchored at the top, while it fades in.</summary>
    public const float ContentScaleIn = 0.94f;

    /// <summary>…and shrinks to this one, folding up with the shell, while it fades out.</summary>
    public const float ContentScaleOut = 0.9f;

    /// <summary>Changing tab is a quick crossfade of the content alone; the island keeps its size.</summary>
    public static readonly TimeSpan TabCrossfade = TimeSpan.FromMilliseconds(180);

    /// <summary>Used instead of springs when Windows' "Animation effects" is off: an ease-in-out.</summary>
    public static readonly TimeSpan ReducedMotion = TimeSpan.FromMilliseconds(160);
}
