using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ISLe8.Settings;

/// <summary>
/// Settings › Clipboard, after the Mac's (SettingsView.swift, ClipboardSettings). The Mac's "Erişim"
/// row is its pasteboard permission; Windows asks for none, so it isn't here.
/// </summary>
internal static class ClipboardPage
{
    public static FrameworkElement Build(SettingsContext context)
    {
        var settings = context.Settings;
        var page = new StackPanel { MaxWidth = 860 };
        page.Children.Add(SettingsUi.PageTitle(L.Get("Settings_Clipboard")));
        page.Children.Add(SettingsUi.Card(context.Palette, L.Get("Settings_RecordClipboard"), SettingsUi.Switch(settings.RecordClipboard, on =>
        {
            settings.RecordClipboard = on;
            Changed(context);
        })));
        page.Children.Add(SettingsUi.Card(context.Palette, L.Get("Settings_Shortcut"),
            new TextBlock { Text = "Ctrl+Alt+V", Foreground = context.Palette.SecondaryText }));

        page.Children.Add(SettingsUi.SectionHeader(L.Get("Settings_Retention")));
        page.Children.Add(WidgetsPage.Stepper(context, "Settings_ClipboardLimit", settings.ClipboardLimit, 50, 1000, 50, count =>
        {
            settings.ClipboardLimit = count;
            Changed(context);
        }));
        page.Children.Add(WidgetsPage.Stepper(context, "Settings_ClipboardDays", settings.ClipboardMaxAgeDays, 1, 365, 1, days =>
        {
            settings.ClipboardMaxAgeDays = days;
            Changed(context);
        }));
        var clear = new Button { Content = L.Get("Settings_ClearHistory") };
        clear.Click += (_, _) => context.Island.Clipboard.Clear();
        page.Children.Add(SettingsUi.Card(context.Palette, L.Get("Settings_PinnedStay"), clear));

        page.Children.Add(SettingsUi.SectionHeader(L.Get("Settings_ExcludedApps")));
        var apps = new StackPanel();
        page.Children.Add(apps);
        void Fill()
        {
            apps.Children.Clear();
            foreach (var app in settings.ClipboardExcludedApps.ToList())
            {
                var remove = new Button { Content = new FontIcon { Glyph = Glyphs.Remove, FontSize = 12 }, Width = 32, Height = 32, Padding = new Thickness(0) };
                ToolTipService.SetToolTip(remove, L.Get("Settings_Remove"));
                remove.Click += (_, _) =>
                {
                    settings.ClipboardExcludedApps.Remove(app);
                    context.Save();
                    Fill();
                };
                apps.Children.Add(SettingsUi.Card(context.Palette, Path.GetFileNameWithoutExtension(app), remove));
            }
        }
        Fill();

        var add = new Button { Content = L.Get("Settings_AddApp"), Margin = new Thickness(0, 4, 0, 8) };
        add.Click += async (_, _) =>
        {
            // The Mac picks an app bundle; here a program's .exe.
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".exe");
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(context.Owner));
            if (await picker.PickSingleFileAsync() is not { } file) return;
            if (!settings.ClipboardExcludedApps.Contains(file.Name, StringComparer.OrdinalIgnoreCase)) settings.ClipboardExcludedApps.Add(file.Name);
            context.Save();
            Fill();
        };
        page.Children.Add(add);
        return page;
    }

    private static void Changed(SettingsContext context)
    {
        context.Save();
        context.Island.ApplyClipboardSettings();
    }
}
