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

        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_OpenAtLogin"), SettingsUi.Switch(StartAtLogin.IsOn, StartAtLogin.Set)));
        page.Children.Add(ScreenCard(context));
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

        page.Children.Add(SettingsUi.Gap());
        var version = typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_Version"), new TextBlock { Text = version, IsTextSelectionEnabled = true }));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_Developer"), new TextBlock { Text = "LRXe8" }));
        return page;
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
            context.PlaceIsland();
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
    private static Border SecondsCard(SettingsPalette palette, string title, double value, double maximum, Action<double> changed)
    {
        var label = new TextBlock { MinWidth = 60, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = maximum,
            StepFrequency = 0.05,
            SmallChange = 0.05,
            Width = 200,
            Value = value,
            IsThumbToolTipEnabled = false,
        };
        void Show(double seconds) => label.Text = L.Format("Settings_Seconds", seconds.ToString("0.00", L.Culture));
        Show(value);
        slider.ValueChanged += (_, e) =>
        {
            var seconds = Math.Round(e.NewValue, 2);
            Show(seconds);
            changed(seconds);
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { slider, label } };
        return SettingsUi.Card(palette, title, row);
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
