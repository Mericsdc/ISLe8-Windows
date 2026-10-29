using ISLe8.Core.Devices;

namespace ISLe8.Core.Tests;

/// <summary>Batteries of devices on their own receivers (Razer, SteelSeries), from the answers the test PC's devices gave.</summary>
public class VendorProtocolTests
{
    [Fact]
    public void ARazerQuestionIsTheSynapsePacket()
    {
        var request = RazerProtocol.Request(0x1F, RazerProtocol.PowerClass, RazerProtocol.BatteryCommand);
        Assert.Equal(RazerProtocol.ReportLength, request.Length);
        Assert.Equal((0x00, 0x00, 0x1F, 0x02, 0x07, 0x80), (request[0], request[1], request[2], request[6], request[7], request[8]));
        // The checksum: data size ^ class ^ command.
        Assert.Equal(0x02 ^ 0x07 ^ 0x80, request[89]);
    }

    [Fact]
    public void TheViperV3ProsAnswerIsItsBattery()
    {
        // Razer Viper V3 Pro on its receiver (1532:00C1): status 0x02, level 0x2C of 255.
        var answer = new byte[RazerProtocol.ReportLength];
        answer[1] = 0x02;
        answer[2] = 0x1F;
        answer[7] = 0x07;
        answer[8] = 0x80;
        answer[10] = 0x2C;
        Assert.Equal(RazerProtocol.Status.Successful, RazerProtocol.StatusOf(answer));
        Assert.Equal((byte)0x2C, RazerProtocol.Argument(answer, RazerProtocol.BatteryCommand));
        Assert.Equal(17, RazerProtocol.Percent(0x2C));
        Assert.Equal(100, RazerProtocol.Percent(0xFF));
        Assert.Null(RazerProtocol.Argument(answer, RazerProtocol.ChargingCommand));

        answer[1] = 0x04;
        Assert.Null(RazerProtocol.Argument(answer, RazerProtocol.BatteryCommand));
        Assert.Equal(RazerProtocol.Status.Failure, RazerProtocol.StatusOf(new byte[10]));
    }

    [Fact]
    public void TheNovaProOmniTellsTheHeadsetAndTheSpare()
    {
        // Arctis Nova Pro Omni (1038:2290), vendor collection 0xFFC0, report 0x01.
        byte[] answer = [0x01, 0xB0, 0x00, 0x00, 0x01, 0x00, 0x5E, 0x64, 0x08, 0x01, 0x01, 0x0A, 0x05, 0x00, 0x08, 0x08, 0x03, 0x00];
        Assert.Equal(new SteelSeriesProtocol.Reading(94, 100, false), SteelSeriesProtocol.Parse(answer));
        Assert.Equal([0x01, 0xB0], SteelSeriesProtocol.Request(0x01)[..2]);
        Assert.Equal(64, SteelSeriesProtocol.Request(0x01).Length);

        answer[15] = 0x02;
        Assert.True(SteelSeriesProtocol.Parse(answer)!.IsCharging);
        // The headset is off: nothing to show.
        answer[15] = 0x01;
        Assert.Null(SteelSeriesProtocol.Parse(answer));
        Assert.Null(SteelSeriesProtocol.Parse([0x01, 0xB7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x08]));
    }

    [Fact]
    public void TheSpareNeverRunsOutFirst()
    {
        var headset = new WirelessDevice("s", "Arctis Nova Pro Omni", DeviceKind.Headphones, 94, Spare: 5);
        Assert.Equal(94, headset.Lowest);
        var merged = DeviceRules.Merge([new WirelessDevice("a", "X", DeviceKind.Other, 50), new WirelessDevice("b", "X", DeviceKind.Headphones, null, Spare: 80)]);
        Assert.Equal((50, 80, DeviceKind.Headphones), (merged[0].Main, merged[0].Spare, merged[0].Kind));
    }
}
