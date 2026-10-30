using System.Globalization;
using ISLe8.Core.SystemInfo;

namespace ISLe8.Core.Tests;

/// <summary>The performance pill's arithmetic (Windows only).</summary>
public sealed class PerformanceTests
{
    [Fact]
    public void TheGpuIsTheBusiestAdaptersEnginesSummed()
    {
        // As read on a laptop: the NVIDIA GPU (0x112AF) busy with two processes, the integrated one (0x15FB1) barely.
        var use = PerformanceMath.Gpu([
            ("pid_48412_luid_0x00000000_0x000112AF_phys_0_eng_0_engtype_3D", 28.5),
            ("pid_34572_luid_0x00000000_0x000112AF_phys_0_eng_0_engtype_3D", 3.5),
            ("pid_1200_luid_0x00000000_0x00015FB1_phys_0_eng_0_engtype_3D", 4),
            ("not an engine", 90),
        ]);

        Assert.NotNull(use);
        Assert.Equal(0.32, use.Busy, 3);
        Assert.Equal(0x112AF, use.Luid);
        Assert.Null(PerformanceMath.Gpu([]));
        Assert.Equal(1, PerformanceMath.Gpu([("luid_0x00000000_0x00000001_phys_0_eng_0", 180)])!.Busy);
    }

    [Fact]
    public void AnAdaptersLuidReadsHighPartFirst()
    {
        Assert.Equal(0x1_0000_00AFL, PerformanceMath.Luid("luid_0x00000001_0x000000AF_phys_0"));
        Assert.Null(PerformanceMath.Luid("luid_zz"));
    }

    [Fact]
    public void FiguresReadAsThePillShowsThem()
    {
        Assert.Equal(144, PerformanceMath.Fps(288, 2));
        Assert.Null(PerformanceMath.Fps(0, 1));
        Assert.Equal("73°", PerformanceMath.Degrees(72.6));
        Assert.Null(PerformanceMath.Temperature(0));
        Assert.Null(PerformanceMath.Temperature(3276.7));
        Assert.Equal(54.0, PerformanceMath.Temperature(54));
        Assert.Equal("%23", PerformanceMath.Percent(0.23, CultureInfo.GetCultureInfo("tr-TR")));
    }

    private static string Said(IReadOnlyList<PerformancePart> parts) => string.Join(" | ", parts.Select(part =>
        (part.LabelFirst ? part.Label + part.Value : part.Value + part.Label) + (part.Degrees is { } degrees ? (part.Value.Length > 0 ? " " : "") + degrees : "")));

    [Fact]
    public void ThePillSaysWhatWasChosenInItsOrder()
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var sample = new PerformanceSample(144, 0.23, 0.61, 64, 54, 0.48);
        var all = new PerformanceShow(Fps: true, Cpu: true, Gpu: true, Temperature: true, Ram: true, Clock: true);

        Assert.Equal("21:45 | 144 FPS | CPU %23 54° | GPU %61 64° | RAM %48", Said(PerformanceMath.Parts(sample, all, frames: true, "21:45", tr)));
        // The defaults: no time, no memory.
        Assert.Equal("144 FPS | CPU %23 54° | GPU %61 64°", Said(PerformanceMath.Parts(sample, PerformanceShow.Of(new Settings.AppSettings()), frames: true, "", tr)));
        // A temperature alone keeps its label; a figure not chosen is left out.
        Assert.Equal("CPU 54° | GPU 64°", Said(PerformanceMath.Parts(sample, new PerformanceShow(false, false, false, true, false, false), frames: true, "", tr)));
    }

    [Fact]
    public void FramesShowForAGameEvenBeforeTheyAreCountedAndForAnyProgramThatDraws()
    {
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        var fps = new PerformanceShow(Fps: true, Cpu: false, Gpu: false, Temperature: false, Ram: false, Clock: false);

        Assert.Equal("– FPS", Said(PerformanceMath.Parts(new PerformanceSample(null, 0, null, null, null), fps, frames: true, "", tr)));
        Assert.Empty(PerformanceMath.Parts(new PerformanceSample(null, 0, null, null, null), fps, frames: false, "", tr));
        Assert.Equal("60 FPS", Said(PerformanceMath.Parts(new PerformanceSample(60, 0, null, null, null), fps, frames: false, "", tr)));
        // A GPU this computer can't read leaves no empty "GPU".
        Assert.Empty(PerformanceMath.Parts(new PerformanceSample(null, 0, null, null, null), new PerformanceShow(false, false, true, true, false, false), frames: false, "", tr));
    }
}
