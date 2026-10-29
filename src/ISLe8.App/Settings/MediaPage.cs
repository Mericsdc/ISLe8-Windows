using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ISLe8.Settings;

/// <summary>
/// Settings › Media, after the Mac's (SettingsView.swift, MediaSettings). Its MediaRemote bridge row
/// has no Windows counterpart: SMTC is part of Windows.
/// </summary>
internal static class MediaPage
{
    public static FrameworkElement Build(SettingsContext context)
    {
        var settings = context.Settings;
        var page = new StackPanel { MaxWidth = 860 };
        page.Children.Add(SettingsUi.PageTitle(L.Get("Settings_Media")));
        page.Children.Add(SettingsUi.Card(context.Palette, L.Get("Settings_TrackChangeAlert"), SettingsUi.Switch(settings.ShowTrackChangeAlert, on =>
        {
            settings.ShowTrackChangeAlert = on;
            context.Save();
        })));
        page.Children.Add(SettingsUi.Card(context.Palette, L.Get("Settings_AudioBars"), SettingsUi.Switch(settings.AudioBars, on =>
        {
            settings.AudioBars = on;
            context.Save();
            context.Island.ApplyMediaSettings();
        })));
        return page;
    }
}
