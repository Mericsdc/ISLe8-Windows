using System.Globalization;
using ISLe8.Core.SystemInfo;

namespace ISLe8.Core.Tests;

/// <summary>The System tab's figures (PORTING.md §3.20).</summary>
public class SystemTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void CpuIsWhatIsntIdleOfKernelAndUserTime()
    {
        // 1000 ticks passed on all cores: kernel 600 (of which idle 500), user 400 → 50 % busy.
        Assert.Equal(0.5, SystemMath.Cpu((1000, 2000, 3000), (1500, 2600, 3400)), precision: 6);
        Assert.Equal(0, SystemMath.Cpu((1, 1, 1), (1, 1, 1)));
    }

    [Fact]
    public void AProcessOnTwoCoresIsAboveAHundred()
    {
        Assert.Equal(200, SystemMath.ProcessCpu(0, 4_0000000, 2), precision: 6);
        Assert.Equal(0, SystemMath.ProcessCpu(10, 5, 2));
    }

    [Fact]
    public void ACounterGoingBackwardsIsNoTraffic()
    {
        Assert.Equal(1000, SystemMath.Rate(5000, 7000, 2));
        Assert.Equal(0, SystemMath.Rate(7000, 5000, 2));
    }

    [Theory]
    [InlineData(0.3, GaugeLevel.Fine)]
    [InlineData(0.6, GaugeLevel.Busy)]
    [InlineData(0.85, GaugeLevel.High)]
    public void GaugesChangeColourAtSixtyAndEightyFive(double fraction, GaugeLevel level)
    {
        Assert.Equal(level, SystemMath.Level(fraction));
    }

    [Fact]
    public void FiguresReadAsOnTheMac()
    {
        Assert.Equal("%42", SystemMath.Percent(0.42, Turkish));
        Assert.Equal("42%", SystemMath.Percent(0.42, English).Replace(" ", ""));
        Assert.Equal("16 GB", SystemMath.Bytes(16UL << 30, memory: true, Turkish));
        Assert.Equal("1,2 MB", SystemMath.Bytes(1_234_567, memory: false, Turkish));
        Assert.Equal("512 KB", SystemMath.Bytes(512_000, memory: false, English));
        Assert.Equal("1.23 GB", SystemMath.Bytes(1_234_000_000, memory: false, English));
        Assert.Equal("200 bayt", SystemMath.Bytes(200, memory: false, Turkish));
    }
}
