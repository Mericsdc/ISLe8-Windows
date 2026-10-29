using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ISLe8.Settings;

/// <summary>
/// Settings › Shelf, after the Mac's (SettingsView.swift, ShelfSettings). Its AirDrop row has no
/// Windows counterpart: files from Nearby Share can't be told apart in Downloads (PORTING.md §3.10).
/// </summary>
internal static class ShelfPage
{
    public static FrameworkElement Build(SettingsContext context)
    {
        var settings = context.Settings;
        var page = new StackPanel { MaxWidth = 860 };
        page.Children.Add(SettingsUi.PageTitle(L.Get("Settings_Shelf")));
        page.Children.Add(SettingsUi.Card(context.Palette, L.Get("Settings_CopyToShelf"), SettingsUi.Switch(settings.CopyFilesToShelf, on =>
        {
            settings.CopyFilesToShelf = on;
            context.Save();
        }), L.Get("Settings_CopyToShelfNote")));
        return page;
    }
}
