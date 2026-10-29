using ISLe8.Core.Devices;
using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Tests;

/// <summary>Wireless device batteries and the Devices tab's layout (PORTING.md §3.14).</summary>
public class DeviceTests
{
    private static WirelessDevice Device(string id, int? main, string? name = null) => new(id, name ?? id, DeviceKind.Mouse, main);

    [Fact]
    public void TheShortNameDropsTheOwner()
    {
        Assert.Equal("AirPods Pro 3", Device("a", 80, "Deniz (AirPods Pro 3)").ShortName);
        Assert.Equal("AirPods Pro", Device("a", 80, "Deniz's AirPods Pro").ShortName);
        Assert.Equal("AirPods", Device("a", 80, "Deniz’s AirPods").ShortName);
        Assert.Equal("MX Master 3", Device("a", 80, "MX Master 3").ShortName);
    }

    [Fact]
    public void TheLowestLevelLeavesTheCaseOut()
    {
        var pods = new WirelessDevice("p", "AirPods", DeviceKind.AirPods, null, Left: 60, Right: 40, Case: 5);
        Assert.Equal(40, pods.Lowest);
        Assert.True(pods.HasBuds);
        Assert.Null(new WirelessDevice("c", "Case only", DeviceKind.AirPods, null, Case: 50).Lowest);
    }

    [Fact]
    public void RingsAreGreenThenOrangeFrom30ThenRedFrom15()
    {
        Assert.Equal(BatteryTint.Green, DeviceRules.Tint(31));
        Assert.Equal(BatteryTint.Orange, DeviceRules.Tint(30));
        Assert.Equal(BatteryTint.Orange, DeviceRules.Tint(16));
        Assert.Equal(BatteryTint.Red, DeviceRules.Tint(15));
    }

    [Fact]
    public void AirPodsAreKnownByNameOrByApplesProductId()
    {
        Assert.Equal(DeviceKind.AirPodsPro, DeviceRules.Kind("Deniz's AirPods Pro", null, default));
        Assert.Equal(DeviceKind.AirPodsPro, DeviceRules.Kind("Kulaklık", 0x2014, default));
        Assert.Equal(DeviceKind.AirPodsMax, DeviceRules.Kind("Max", 0x201F, default));
        Assert.Equal(DeviceKind.AirPods, DeviceRules.Kind("AirPods", null, default));
        Assert.Equal(DeviceKind.Beats, DeviceRules.Kind("Beats Studio Buds", null, default));
        Assert.Equal(0x2014, DeviceRules.AppleModel(["BTHENUM\\Dev_34E6", "BTHENUM\\{0000110b-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&2014"]));
        Assert.Null(DeviceRules.AppleModel(["BTHENUM\\{0000110b-0000-1000-8000-00805f9b34fb}_VID&0002046d_PID&b023"]));
        Assert.Null(DeviceRules.AppleModel(["BTHENUM\\{0000110b-0000-1000-8000-00805f9b34fb}_LOCALMFG&0046"]));
    }

    [Fact]
    public void OtherDevicesSayWhatTheyAreInTheirClass()
    {
        // The LG speakers paired with the test PC: audio/video, loudspeaker and hi-fi.
        Assert.Equal(DeviceKind.Speaker, DeviceRules.Kind("LG-LK72", null, new DeviceClass(4, 5)));
        Assert.Equal(DeviceKind.Speaker, DeviceRules.Kind("LG SNH5(B9)", null, new DeviceClass(4, 10)));
        Assert.Equal(DeviceKind.Headphones, DeviceRules.Kind("WH-1000XM5", null, new DeviceClass(4, 6)));
        Assert.Equal(DeviceKind.Mouse, DeviceRules.Kind("MX Master", null, new DeviceClass(5, 0x20)));
        Assert.Equal(DeviceKind.Keyboard, DeviceRules.Kind("K380", null, new DeviceClass(5, 0x10)));
        Assert.Equal(DeviceKind.Gamepad, DeviceRules.Kind("Xbox Wireless Controller", null, new DeviceClass(5, 0x02)));
        Assert.Equal(DeviceKind.Mouse, DeviceRules.Kind("BLE", null, new DeviceClass(AppearanceCategory: 15, AppearanceSubcategory: 2)));
        Assert.Equal(DeviceKind.Keyboard, DeviceRules.Kind("Magic Keyboard", null, default));
        Assert.Equal(DeviceKind.Other, DeviceRules.Kind("Tracker", null, default));
    }

    [Fact]
    public void TheSameNameTwiceBecomesOneEntryWithEveryField()
    {
        var merged = DeviceRules.Merge([
            new WirelessDevice("le", "AirPods", DeviceKind.Other, null, Left: 70, Right: 60, Case: 90),
            new WirelessDevice("classic", "AirPods", DeviceKind.AirPods, 65),
            Device("m", 50, "A mouse"),
        ]);
        Assert.Equal(["A mouse", "AirPods"], merged.Select(device => device.Name));
        var pods = merged[1];
        Assert.Equal(("le", DeviceKind.AirPods, 65, 70, 60, 90), (pods.Id, pods.Kind, pods.Main, pods.Left, pods.Right, pods.Case));
    }

    [Fact]
    public void TheChipShowsTheDeviceThatRunsOutFirst()
    {
        Assert.Null(DeviceRules.ChipDevice([]));
        Assert.Equal("b", DeviceRules.ChipDevice([Device("a", 80), Device("b", 20), new WirelessDevice("c", "c", DeviceKind.AirPods, null, Case: 5)])!.Id);
    }

    [Fact]
    public void CardsShareRowsOfAtMostFiveRings()
    {
        var rows = DeviceRules.Rows([1, 4, 1, 3, 2], rings => rings);
        Assert.Equal([[1, 4], [1, 3], [2]], rows);
        Assert.Equal(58, DeviceRules.RingSize(1));
        Assert.Equal(46, DeviceRules.RingSize(2));
        // A lone card stays narrow; two share the row by their rings.
        Assert.Equal(180, DeviceRules.CardWidth(600, 1, 1, 1));
        Assert.Equal(118.4, DeviceRules.CardWidth(600, 2, 5, 1), 3);
        Assert.Equal(473.6, DeviceRules.CardWidth(600, 2, 5, 4), 3);
    }

    [Fact]
    public void RowsThatWouldOverflowShareTheHeightWithSmallerRings()
    {
        // One row keeps the Mac's sizes in every layout.
        Assert.Equal((140, 58), DeviceRules.Fit(1, 196));
        Assert.Equal((112, 46), DeviceRules.Fit(2, 304));
        // Two rows in the compact layouts' 196: 94 each, rings of 30.
        Assert.Equal((94, 30), DeviceRules.Fit(2, 196));
        var (row, ring) = DeviceRules.Fit(3, 196);
        Assert.Equal(60, row);
        Assert.Equal(28, ring);
    }

    [Fact]
    public void ConnectingGetsABannerButNotTheDevicesThereAtLaunch()
    {
        var alerts = new DeviceAlerts();
        Assert.Empty(alerts.Read([Device("mouse", 80)]));

        var connected = alerts.Read([Device("mouse", 80), Device("keyboard", 60)]);
        Assert.Equal([("keyboard", false)], connected.Select(alert => (alert.Device.Id, alert.IsLow)));
        Assert.Empty(alerts.Read([Device("mouse", 80), Device("keyboard", 60)]));
    }

    [Fact]
    public void ALowBatteryIsSaidOnceUntilItChargesAbove30()
    {
        var alerts = new DeviceAlerts();
        // At launch only a very low device is worth a warning.
        Assert.Empty(alerts.Read([Device("mouse", 14)]));
        Assert.Equal([("mouse", true)], alerts.Read([Device("mouse", 14)]).Select(alert => (alert.Device.Id, alert.IsLow)));
        Assert.Empty(alerts.Read([Device("mouse", 12)]));
        Assert.Empty(alerts.Read([Device("mouse", 30)]));
        Assert.Empty(alerts.Read([Device("mouse", 15)]));
        Assert.Empty(alerts.Read([Device("mouse", 31)]));
        Assert.Single(alerts.Read([Device("mouse", 15)]));

        var fresh = new DeviceAlerts();
        Assert.Single(fresh.Read([Device("pods", 9)]));
        // A device that connects already low says so instead of "connected".
        Assert.Equal([true], fresh.Read([Device("pods", 9), Device("pad", 10)]).Select(alert => alert.IsLow));
    }

    [Fact]
    public void AirPodsAdvertiseEachBudAndTheCase()
    {
        // Proximity pairing, AirPods Pro (0x200E); the right bud is the primary one.
        byte[] data = [0x07, 0x19, 0x01, 0x0E, 0x20, 0x2B, 0x98, 0x5F, 0x01, 0x00, 0x00];
        var reading = AirPodsReading.Parse(data)!;
        Assert.Equal(0x200E, reading.Model);
        Assert.Equal((80, 90, (int?)null), (reading.Left, reading.Right, reading.Case));
        Assert.Equal((true, false, true), (reading.LeftCharging, reading.RightCharging, reading.CaseCharging));

        // The left bud primary: the nibbles and the charging bits swap.
        data[5] = 0x0B;
        data[7] = 0x16;
        reading = AirPodsReading.Parse(data)!;
        Assert.Equal((90, 80, (int?)60), (reading.Left, reading.Right, reading.Case));
        Assert.Equal((false, true, false), (reading.LeftCharging, reading.RightCharging, reading.CaseCharging));

        Assert.Null(AirPodsReading.Parse([0x10, 0x05, 0x01]));
        Assert.Null(AirPodsReading.Parse([0x07, 0x19, 0x01, 0x0E]));
    }

    [Fact]
    public void ADeviceBannerHasTheMacsSizeAndTime()
    {
        var alert = new DeviceAlert(Device("m", 80), IsLow: false);
        Assert.Equal(new DipSize(420, 88), IslandSizes.Alert(alert));
        Assert.Equal(TimeSpan.FromSeconds(4), DeviceAlertDurations.Connected);
        Assert.Equal(TimeSpan.FromSeconds(5), DeviceAlertDurations.Low);
        var settings = new AppSettings();
        Assert.True(settings.ShowDeviceConnectedAlert && settings.ShowDeviceLowBatteryAlert && settings.ShowDevicesChip);
    }
}
