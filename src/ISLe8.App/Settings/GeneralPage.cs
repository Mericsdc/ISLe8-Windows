using System.Text.Json;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.Settings;
using ISLe8.Localization;
using ISLe8.Shell;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace ISLe8.Settings;

/// <summary>Settings › General, in the Mac's order (SettingsView.swift, GeneralSettings).</summary>
internal static class GeneralPage
{
    private static readonly string[] LanguageCodes = ["system", "tr-TR", "en-US"];

    public static FrameworkElement Build(SettingsContext context)
    {
        var settings = context.Settings;
        var palette = context.Palette;
        var page = new StackPanel { MaxWidth = 860 };
        page.Children.Add(SettingsUi.PageTitle(L.Get("Settings_General")));

        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_OpenAtLogin"), LoginSwitch()));
        page.Children.Add(ScreenCard(context));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_HideInFullscreen"), SettingsUi.Switch(settings.HideInFullscreen, on =>
        {
            settings.HideInFullscreen = on;
            context.Save();
            context.Island.ApplyFullscreenSetting();
        }), L.Get("Settings_HideInFullscreenNote")));
        page.Children.Add(LanguageCard(context));

        page.Children.Add(SettingsUi.SectionHeader(L.Get("Settings_Behavior")));
        var openDelay = SecondsCard(palette, L.Get("Settings_OpenDelay"), settings.OpenDelay, 1, value =>
        {
            settings.OpenDelay = value;
            context.Save();
        });
        // Click mode opens on a click; it has no delay to set.
        openDelay.Visibility = settings.OpenMode == OpenMode.Hover ? Visibility.Visible : Visibility.Collapsed;
        var openMode = SettingsUi.Choices(
            [L.Get("Settings_OpenOnHover"), L.Get("Settings_OpenOnClick")],
            settings.OpenMode == OpenMode.Click ? 1 : 0,
            index =>
            {
                settings.OpenMode = index == 1 ? OpenMode.Click : OpenMode.Hover;
                context.Save();
                openDelay.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            });
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_OpenMode"), openMode));
        page.Children.Add(openDelay);
        page.Children.Add(SecondsCard(palette, L.Get("Settings_CloseDelay"), settings.CloseDelay, 1.5, value =>
        {
            settings.CloseDelay = value;
            context.Save();
        }));

        page.Children.Add(SettingsUi.Gap());
        page.Children.Add(BackupCard(context));

        page.Children.Add(UpdatesSection(context));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_Developer"), new TextBlock { Text = "LRXe8" }));
        return page;
    }

    // MARK: Updates

    /// <summary>
    /// Settings › General › Updates (Windows only): the version, a button that checks GitHub or, once
    /// a newer ISLe8 is out, updates to it, where it stands, and the daily check.
    /// </summary>
    private static StackPanel UpdatesSection(SettingsContext context)
    {
        var settings = context.Settings;
        var palette = context.Palette;
        var updater = context.Island.Updater;
        var section = new StackPanel();
        section.Children.Add(SettingsUi.SectionHeader(L.Get("Settings_Updates")));

        var status = SettingsUi.Description(palette, "");
        var button = new Button();
        var notes = new HyperlinkButton { Content = L.Get("Update_Notes"), Padding = new Thickness(6, 4, 6, 4) };
        var progress = new ProgressBar { Width = 120, Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { notes, progress, button } };
        button.Click += (_, _) =>
        {
            if (updater.State == UpdateState.Available) _ = updater.InstallAsync();
            else _ = updater.CheckAsync();
        };
        void Show()
        {
            var release = updater.Release;
            var busy = updater.State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing;
            button.Content = updater.State == UpdateState.Available && release is not null
                ? L.Format("Update_Install", release.Version.ToString(3))
                : L.Get("Update_Check");
            button.IsEnabled = !busy;
            if (updater.State == UpdateState.Available) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
            notes.Visibility = release is not null ? Visibility.Visible : Visibility.Collapsed;
            if (release is not null) notes.NavigateUri = release.Page;
            progress.Visibility = updater.State == UpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
            progress.Value = updater.Progress;
            status.Text = updater.State switch
            {
                UpdateState.Checking => L.Get("Update_Checking"),
                UpdateState.UpToDate => L.Get("Update_UpToDate"),
                UpdateState.Available when release is not null => L.Format("Update_Available", release.Version.ToString(3)),
                UpdateState.Downloading => L.Format("Update_Downloading", (int)Math.Round(updater.Progress * 100)),
                UpdateState.Installing => L.Get("Update_Installing"),
                UpdateState.Failed => L.Format("Update_Failed", updater.Problem ?? ""),
                _ => "",
            };
            status.Visibility = status.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        updater.Changed += Show;
        // The page is built again each time it is shown; the old one lets go when it leaves the tree.
        section.Unloaded += (_, _) => updater.Changed -= Show;
        Show();

        var heading = new TextBlock { Text = $"{L.Get("Settings_Version")} {Updater.Current.ToString(3)}", IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        section.Children.Add(SettingsUi.Card(palette, heading, controls, status));
        section.Children.Add(SettingsUi.Card(palette, L.Get("Update_Auto"), SettingsUi.Switch(settings.CheckForUpdates, on =>
        {
            settings.CheckForUpdates = on;
            context.Save();
        }), L.Get("Update_AutoNote")));
        return section;
    }

    // MARK: Screen

    /// <summary>The main screen, the active one, then every connected monitor (and a chosen one that is unplugged).</summary>
    private static Border ScreenCard(SettingsContext context)
    {
        var settings = context.Settings;
        var labels = new List<string> { L.Get("Menu_DisplayPrimary"), L.Get("Menu_DisplayActive") };
        var devices = new List<string?> { null, null };
        foreach (var monitor in Monitors.All())
        {
            labels.Add(L.Format("Menu_DisplayItem", monitor.Number, monitor.Bounds.Width, monitor.Bounds.Height, (int)Math.Round(monitor.Scale * 100)));
            devices.Add(monitor.Device);
        }
        if (settings.Monitor == MonitorChoice.Named && settings.MonitorDevice is { } chosen && !devices.Contains(chosen))
        {
            labels.Add(L.Format("Settings_ScreenDisconnected", chosen));
            devices.Add(chosen);
        }

        var selected = settings.Monitor switch
        {
            MonitorChoice.Active => 1,
            MonitorChoice.Named when devices.IndexOf(settings.MonitorDevice) is var index and >= 2 => index,
            _ => 0,
        };
        var note = SettingsUi.Description(context.Palette, ScreenNote(settings.Monitor));
        var choices = SettingsUi.Choices(labels, selected, index =>
        {
            settings.Monitor = index switch { 0 => MonitorChoice.Primary, 1 => MonitorChoice.Active, _ => MonitorChoice.Named };
            settings.MonitorDevice = index >= 2 ? devices[index] : null;
            context.Save();
            context.Island.PlaceIsland();
            note.Text = ScreenNote(settings.Monitor);
        });
        return SettingsUi.Card(context.Palette, L.Get("Menu_Display"), choices, note);
    }

    private static string ScreenNote(MonitorChoice choice) => L.Get(choice switch
    {
        MonitorChoice.Active => "Settings_ScreenActiveNote",
        MonitorChoice.Named => "Settings_ScreenNamedNote",
        _ => "Settings_ScreenPrimaryNote",
    });

    // MARK: Language

    /// <summary>As on the Mac, a new language takes a fresh start: every window is built in it.</summary>
    private static Border LanguageCard(SettingsContext context)
    {
        var settings = context.Settings;
        var choices = SettingsUi.Choices(
            [L.Get("Menu_LanguageSystem"), "Türkçe", "English"],
            Math.Max(Array.IndexOf(LanguageCodes, settings.Language), 0),
            index =>
            {
                if (LanguageCodes[index] == settings.Language) return;
                settings.Language = LanguageCodes[index];
                context.Save();
                ((App)Application.Current).Restart();
            });
        return SettingsUi.Card(context.Palette, L.Get("Menu_Language"), choices, L.Get("Settings_LanguageNote"));
    }

    // MARK: Behaviour

    /// <summary>A delay in seconds, in steps of 0.05 like the Mac's, written as "0,35 sn".</summary>
    private static Border SecondsCard(SettingsPalette palette, string title, double value, double maximum, Action<double> changed) =>
        SettingsUi.Card(palette, title, SettingsUi.SliderRow(
            value, 0, maximum, 0.05,
            seconds => L.Format("Settings_Seconds", seconds.ToString("0.00", L.Culture)),
            changed));

    /// <summary>Windows answers for the startup task in its own time; the switch waits for it, and shows what it said.</summary>
    private static ToggleSwitch LoginSwitch()
    {
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0, IsEnabled = false };
        var showing = false;
        async void Show(Task<bool> state)
        {
            var on = await state;
            showing = true;
            toggle.IsOn = on;
            showing = false;
            toggle.IsEnabled = true;
        }
        toggle.Toggled += (_, _) =>
        {
            if (showing) return;
            toggle.IsEnabled = false;
            Show(StartAtLogin.SetAsync(toggle.IsOn));
        };
        Show(StartAtLogin.IsOnAsync());
        return toggle;
    }

    // MARK: Back up

    private static Border BackupCard(SettingsContext context)
    {
        var export = new Button { Content = L.Get("Settings_Export") };
        export.Click += async (_, _) => await ExportAsync(context);
        var import = new Button { Content = L.Get("Settings_Import") };
        import.Click += async (_, _) => await ImportAsync(context);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { export, import } };
        return SettingsUi.Card(context.Palette, L.Get("Settings_Backup"), buttons, L.Get("Settings_BackupNote"));
    }

    private static async Task ExportAsync(SettingsContext context)
    {
        var picker = new FileSavePicker(context.Owner.AppWindow.Id)
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(L.Get("Settings_BackupFileName")),
            DefaultFileExtension = ".json",
        };
        picker.FileTypeChoices.Add("JSON", [".json"]);
        if (await picker.PickSaveFileAsync() is not { } file) return;
        try
        {
            await File.WriteAllTextAsync(file.Path, SettingsStore.Export(context.Settings));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "settings export");
            await ShowProblemAsync(context, L.Get("Settings_ExportFailed"), e.Message);
        }
    }

    /// <summary>The settings are read once at start, so imported ones need a fresh start, as on the Mac.</summary>
    private static async Task ImportAsync(SettingsContext context)
    {
        var picker = new FileOpenPicker(context.Owner.AppWindow.Id);
        picker.FileTypeFilter.Add(".json");
        if (await picker.PickSingleFileAsync() is not { } file) return;
        AppSettings imported;
        try
        {
            imported = SettingsStore.Import(await File.ReadAllTextAsync(file.Path));
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "settings import");
            await ShowProblemAsync(context, L.Get("Settings_ImportFailed"), L.Get("Settings_ImportFailedDetail"));
            return;
        }
        context.Store.Save(imported);
        ((App)Application.Current).Restart();
    }

    private static async Task ShowProblemAsync(SettingsContext context, string title, string detail)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = detail,
            CloseButtonText = L.Get("Common_OK"),
            XamlRoot = context.Owner.Content.XamlRoot,
            RequestedTheme = (context.Owner.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
        };
        await dialog.ShowAsync();
    }
}
