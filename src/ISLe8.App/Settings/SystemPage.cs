using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ISLe8.Settings;

/// <summary>
/// Settings › System, after the Mac's (SettingsView.swift, SystemSettings). Its other sections
/// (battery, camera and microphone, keep awake) arrive with what they set; the Mac's "iPhone and
/// iPad batteries" switch comes with reading them (PORTING.md §3.14).
/// </summary>
internal static class SystemPage
{
    public static FrameworkElement Build(SettingsContext context)
    {
        var settings = context.Settings;
        var palette = context.Palette;
        var page = new StackPanel { MaxWidth = 860 };
        page.Children.Add(SettingsUi.PageTitle(L.Get("Settings_System")));

        page.Children.Add(SettingsUi.SectionHeader(L.Get("Settings_WirelessDevices")));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_DeviceConnectedAlert"), SettingsUi.Switch(settings.ShowDeviceConnectedAlert, on =>
        {
            settings.ShowDeviceConnectedAlert = on;
            context.Save();
        })));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_DeviceLowAlert"), SettingsUi.Switch(settings.ShowDeviceLowBatteryAlert, on =>
        {
            settings.ShowDeviceLowBatteryAlert = on;
            context.Save();
        })));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_DevicesChip"), SettingsUi.Switch(settings.ShowDevicesChip, on =>
        {
            settings.ShowDevicesChip = on;
            context.Save();
            context.Island.ApplyDeviceSettings();
        })));
        page.Children.Add(SettingsUi.Description(palette, L.Get("Settings_WirelessNote")));
        return page;
    }
}
