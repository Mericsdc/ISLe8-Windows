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
}
