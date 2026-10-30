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

    // MARK: ASUS ROG Omni Receiver

    /// <summary>A 64-byte answer that starts with these bytes (as read from a ROG Azoth on its Omni Receiver).</summary>
    private static byte[] Answer(string hex)
    {
        var report = new byte[AsusProtocol.ReportLength];
        Convert.FromHexString(hex.Replace("-", "")).CopyTo(report, 0);
        return report;
    }

    [Fact]
    public void AsusQuestionsAre64ByteReportsWithTheirIds()
    {
        Assert.Equal([0x01, 0xA0, 0x00, 0x00], AsusProtocol.PairListRequest()[..4]);
        Assert.Equal([0x02, 0x12, 0x01], AsusProtocol.KeyboardBatteryRequest()[..3]);
        Assert.Equal([0x03, 0x12, 0x07], AsusProtocol.MouseBatteryRequest()[..3]);
        Assert.All([AsusProtocol.PairListRequest(), AsusProtocol.KeyboardBatteryRequest(), AsusProtocol.MouseBatteryRequest()], report => Assert.Equal(64, report.Length));
    }

    [Fact]
    public void TheReceiverListsTheAzoth()
    {
        var pairs = AsusProtocol.Paired(Answer("01-A0-00-01-00-85-1A-02-04-00-00"));
        Assert.Equal([0x1A85], pairs);
        Assert.Equal("ROG Azoth", AsusProtocol.Keyboards[pairs[0]]);
        Assert.Equal([0x1A85, 0x1B1A], AsusProtocol.Paired(Answer("01-A0-00-02-00-85-1A-02-04-1A-1B-01-04-00-00")));
        Assert.Empty(AsusProtocol.Paired(Answer("02-12-01-00")));
    }

    [Fact]
    public void TheAzothAnswers46PercentCharging()
    {
        var answer = Answer("02-12-01-00-00-00-2E-02-01-01-14-2E-62-0F");
        Assert.Equal(new AsusProtocol.Reading(46, true), AsusProtocol.KeyboardBattery(answer));
        Assert.Equal(new AsusProtocol.Reading(46, false), AsusProtocol.KeyboardBattery(Answer("02-12-01-00-00-00-2E-02-01-00")));
        // Asleep: nothing to show. Another command's answer, or the error answer: not this one.
        Assert.Null(AsusProtocol.KeyboardBattery(Answer("02-12-01-00-00-00-00-02-01-00")));
        Assert.Null(AsusProtocol.KeyboardBattery(Answer("02-12-00-00-00-00-2E")));
        Assert.Null(AsusProtocol.KeyboardBattery(Answer("02-FF-AA")));
    }

    [Fact]
    public void AnOmniMouseAnswersAtByteFive()
    {
        Assert.Equal(new AsusProtocol.Reading(80, false), AsusProtocol.MouseBattery(Answer("03-12-07-00-00-50-00-00-00-00-00")));
        Assert.Equal(new AsusProtocol.Reading(80, true), AsusProtocol.MouseBattery(Answer("03-12-07-00-00-50-00-00-00-00-01")));
        Assert.Null(AsusProtocol.MouseBattery(Answer("03-12-07-00-00-00-00-00-00-00-00")));
        Assert.Equal("ROG Keris II Ace", AsusProtocol.Mice[0x1B1A]);
    }
}
