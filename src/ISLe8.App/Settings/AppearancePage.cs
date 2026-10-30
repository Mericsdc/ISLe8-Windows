using ISLe8.Core.Settings;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ISLe8.Settings;

/// <summary>
/// Settings › Appearance, after the Mac's (CustomizationSettings.swift, AppearanceSettings). Left out
/// for now: the theme gallery and editor (W5) and the header chips' switches (they come with the
/// chips). The Mac's "size on external screens" has no Windows counterpart: the island already has
/// one size on every screen.
/// </summary>
internal static class AppearancePage
{
    private static readonly HomeStyle[] Styles = [HomeStyle.Dashboard, HomeStyle.Compact, HomeStyle.Minimal];

    /// <summary>What a new ISLe8 starts with, for the sliders' Restore default.</summary>
    internal static readonly AppSettings Standard = new();

    public static FrameworkElement Build(SettingsContext context)
    {
        var settings = context.Settings;
        var palette = context.Palette;
        var page = new StackPanel { MaxWidth = 860 };
        page.Children.Add(SettingsUi.PageTitle(L.Get("Settings_Appearance")));

        var themes = IslandTheme.Presets.Concat(settings.CustomThemes).ToList();
        page.Children.Add(SettingsUi.Card(palette, L.Get("Menu_Theme"), SettingsUi.Choices(
            themes.Select(ThemeName),
            Math.Max(themes.FindIndex(theme => theme.Id == settings.Theme.Id), 0),
            index =>
            {
                settings.ThemeId = themes[index].Id;
                context.Save();
                context.Island.ApplyTheme();
            })));

        var width = WidthCard(context);
        width.Visibility = settings.HomeStyle == HomeStyle.Dashboard ? Visibility.Visible : Visibility.Collapsed;
        var styleNote = SettingsUi.Description(palette, StyleNote(settings.HomeStyle));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_HomeStyle"), SettingsUi.Choices(
            [L.Get("Settings_StyleWide"), L.Get("Settings_StyleCompact"), L.Get("Settings_StyleMinimal")],
            Array.IndexOf(Styles, settings.HomeStyle),
            index =>
            {
                settings.HomeStyle = Styles[index];
                context.Save();
                context.Island.ApplyLayoutSettings();
                styleNote.Text = StyleNote(Styles[index]);
                width.Visibility = Styles[index] == HomeStyle.Dashboard ? Visibility.Visible : Visibility.Collapsed;
            }), styleNote));

        page.Children.Add(IslandSection(context));

        page.Children.Add(SettingsUi.SectionHeader(L.Get("Settings_SizeAndMotion")));
        page.Children.Add(width);
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_AnimationSpeed"), SettingsUi.SliderRow(
            settings.AnimationSpeed, 0.6, 1.6, 0.1,
            value => L.Format("Settings_Times", value.ToString("0.0", L.Culture)),
            value =>
            {
                settings.AnimationSpeed = value;
                context.Save();
            }, standard: Standard.AnimationSpeed)));

        page.Children.Add(SettingsUi.Gap());
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_HideFromCapture"), SettingsUi.Switch(settings.HideFromScreenCapture, on =>
        {
            settings.HideFromScreenCapture = on;
            context.Save();
            context.Island.ApplyCapture();
        }), L.Get("Settings_HideFromCaptureNote")));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_SwipeTabs"), SettingsUi.Switch(settings.SwipeToChangeTabs, on =>
        {
            settings.SwipeToChangeTabs = on;
            context.Save();
        })));

        // The Mac's keep-awake and camera/microphone switches arrive with what they show (PORTING.md §3.4).
        page.Children.Add(SettingsUi.SectionHeader(L.Get("Settings_HeaderBar")));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_BatteryChip"), SettingsUi.Switch(settings.ShowBatteryChip, on =>
        {
            settings.ShowBatteryChip = on;
            context.Save();
            context.Island.ApplyStatusSettings();
        })));
        page.Children.Add(SettingsUi.Card(palette, L.Get("Settings_NetworkChip"), SettingsUi.Switch(settings.ShowNetworkChip, on =>
        {
            settings.ShowNetworkChip = on;
            context.Save();
            context.Island.ApplyStatusSettings();
        })));
        return page;
    }

    private static readonly IslandPlacement[] Placements = [IslandPlacement.Top, IslandPlacement.TaskbarLeft, IslandPlacement.TaskbarCenter, IslandPlacement.TaskbarRight];

    /// <summary>
    /// Where the island sits and what the pill shows besides the Mac's (Windows only: there is no notch
    /// to hang from, so the island can go elsewhere, and the pill's middle is free).
    /// </summary>
    private static StackPanel IslandSection(SettingsContext context)
    {
        var settings = context.Settings;
        var palette = context.Palette;
        var section = new StackPanel();
        section.Children.Add(SettingsUi.SectionHeader(L.Get("Settings_Island")));
        var flush = SettingsUi.Card(palette, L.Get("Settings_FlushTop"), SettingsUi.Switch(settings.FlushTop, on =>
        {
            settings.FlushTop = on;
            context.Save();
            context.Island.ApplyIslandSettings();
        }), L.Get("Settings_FlushTopNote"));
        flush.Visibility = settings.Placement == IslandPlacement.Top ? Visibility.Visible : Visibility.Collapsed;
        section.Children.Add(SettingsUi.Card(palette, L.Get("Settings_Placement"), SettingsUi.Choices(
            [L.Get("Settings_PlacementTop"), L.Get("Settings_PlacementTaskbarLeft"), L.Get("Settings_PlacementTaskbarCenter"), L.Get("Settings_PlacementTaskbarRight")],
            Math.Max(Array.IndexOf(Placements, settings.Placement), 0),
            index =>
            {
                settings.Placement = Placements[index];
                context.Save();
                context.Island.ApplyIslandSettings();
                flush.Visibility = Placements[index] == IslandPlacement.Top ? Visibility.Visible : Visibility.Collapsed;
            }), L.Get("Settings_PlacementNote")));
        section.Children.Add(flush);
        // Windows has no menu bar for the island to sit in, as the Mac's notch does; a strip like the
        // taskbar's can keep one free: the pill and its swell, just the pill, or none.
        var strip = !settings.ReserveTopStrip ? 2 : settings.StripFitsPill ? 1 : 0;
        section.Children.Add(SettingsUi.Card(palette, L.Get("Settings_ReserveStrip"), SettingsUi.Choices(
            [L.Get("Settings_StripPill"), L.Get("Settings_StripTight"), L.Get("Settings_StripNone")],
            strip,
            index =>
            {
                settings.ReserveTopStrip = index < 2;
                settings.StripFitsPill = index == 1;
                context.Save();
                context.Island.PlaceIsland();
            }), L.Get("Settings_ReserveStripNote")));
        section.Children.Add(SettingsUi.Card(palette, L.Get("Settings_ClosedLook"), SettingsUi.Choices(
            [L.Get("Settings_ClosedBlack"), L.Get("Settings_ClosedTheme"), L.Get("Settings_ClosedClear")],
            (int)settings.ClosedLook,
            index =>
            {
                settings.ClosedLook = (ClosedLook)index;
                context.Save();
                context.Island.ApplyIslandSettings();
            }), L.Get("Settings_ClosedLookNote")));
        section.Children.Add(SettingsUi.Card(palette, L.Get("Settings_IdleBar"), SettingsUi.Switch(settings.IdleBar, on =>
        {
            settings.IdleBar = on;
            context.Save();
            context.Island.ApplyIslandSettings();
        }), L.Get("Settings_IdleBarNote")));
        section.Children.Add(SettingsUi.Card(palette, L.Get("Settings_IdleClock"), SettingsUi.Switch(settings.ShowIdleClock, on =>
        {
            settings.ShowIdleClock = on;
            context.Save();
            context.Island.ApplyIslandSettings();
        })));
        section.Children.Add(SettingsUi.Card(palette, L.Get("Settings_PillTitle"), SettingsUi.Switch(settings.ShowPillTitle, on =>
        {
            settings.ShowPillTitle = on;
            context.Save();
            context.Island.ApplyIslandSettings();
        }), L.Get("Settings_PillTitleNote")));
        return section;
    }

    /// <summary>Only the wide layout has a width to choose.</summary>
    private static Border WidthCard(SettingsContext context) => SettingsUi.Card(context.Palette, L.Get("Settings_ExpandedWidth"), SettingsUi.SliderRow(
        context.Settings.ExpandedWidth, 680, 980, 10,
        value => L.Format("Settings_Pixels", (int)value),
        value =>
        {
            context.Settings.ExpandedWidth = value;
            context.Save();
            context.Island.ApplyLayoutSettings();
        }, standard: Standard.ExpandedWidth));

    private static string StyleNote(HomeStyle style) => L.Get(style switch
    {
        HomeStyle.Compact => "Settings_StyleCompactNote",
        HomeStyle.Minimal => "Settings_StyleMinimalNote",
        _ => "Settings_StyleWideNote",
    });

    /// <summary>Built-in themes are named in the current language; your own keep their names.</summary>
    private static string ThemeName(IslandTheme theme) =>
        theme.IsPreset ? L.Get("Theme_" + theme.Id.Replace('-', '_')) : theme.Name;
}
