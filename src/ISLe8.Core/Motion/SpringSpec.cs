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

    /// <summary>Faster for speed &gt; 1, slower for speed &lt; 1.</summary>
    public SpringSpec WithSpeed(double speed) => this with { Response = Response / Math.Clamp(speed, 0.25, 4) };

    /// <summary>
    /// The value for Composition's <c>Period</c>, read as 1/ω₀ the way its samples use it (springs
    /// of ~50 ms that visibly bounce for a third of a second). SwiftUI's response is 2π/ω₀.
    /// Checked by eye on Windows; see DESIGN.md, "Riskler".
    /// </summary>
    public TimeSpan CompositionPeriod => TimeSpan.FromSeconds(Response / (2 * Math.PI));
}

public static class MotionTimings
{
    /// <summary>The open island's content arrives this long after the shell starts to grow.</summary>
    public static readonly TimeSpan ContentDelay = TimeSpan.FromMilliseconds(80);
    public static readonly TimeSpan ContentFadeIn = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan ContentFadeOut = TimeSpan.FromMilliseconds(110);

    /// <summary>Used instead of springs when Windows' "Animation effects" is off.</summary>
    public static readonly TimeSpan ReducedMotion = TimeSpan.FromMilliseconds(160);
}
