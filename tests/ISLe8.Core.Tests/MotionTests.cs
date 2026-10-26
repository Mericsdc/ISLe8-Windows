using ISLe8.Core.Motion;

namespace ISLe8.Core.Tests;

public class MotionTests
{
    [Fact]
    public void CompositionPeriodIsResponseOverTwoPi()
    {
        Assert.Equal(54.1, SpringSpec.Open.CompositionPeriod.TotalMilliseconds, precision: 1);
        Assert.Equal(44.6, SpringSpec.Close.CompositionPeriod.TotalMilliseconds, precision: 1);
    }

    [Fact]
    public void SpeedScalesTheResponse()
    {
        Assert.Equal(0.17, SpringSpec.Open.WithSpeed(2).Response, precision: 6);
        Assert.Equal(SpringSpec.Open.DampingFraction, SpringSpec.Open.WithSpeed(2).DampingFraction);
        // Held to 0.5–2, as on the Mac.
        Assert.Equal(0.34 * 2, SpringSpec.Open.WithSpeed(0.01).Response, precision: 6);
        Assert.Equal(0.34 / 2, SpringSpec.Open.WithSpeed(9).Response, precision: 6);
    }

    [Fact]
    public void ContentSettlesInAndFoldsAwayLikeTheMac()
    {
        Assert.Equal(60, MotionTimings.ContentDelay.TotalMilliseconds);
        Assert.Equal(220, MotionTimings.ContentFadeIn.TotalMilliseconds);
        Assert.Equal(160, MotionTimings.ContentFadeOut.TotalMilliseconds);
        Assert.Equal(0.94f, MotionTimings.ContentScaleIn);
        Assert.Equal(0.9f, MotionTimings.ContentScaleOut);
    }
}
