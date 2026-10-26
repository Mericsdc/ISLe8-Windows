using ISLe8.Core.Island;
using ISLe8.Island;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ISLe8.Settings;

/// <summary>
/// Settings › Layout, after the Mac's (CustomizationSettings.swift, LayoutSettings): the dock's tabs,
/// their order and which ones show. Home's columns join in step 4, with the columns themselves.
/// </summary>
internal static class LayoutPage
{
    public static FrameworkElement Build(SettingsContext context)
    {
        var page = new StackPanel { MaxWidth = 860 };
        page.Children.Add(SettingsUi.PageTitle(L.Get("Settings_Layout")));
        page.Children.Add(SettingsUi.SectionHeader(L.Get("Settings_Tabs")));
        var rows = new StackPanel();
        page.Children.Add(rows);
        page.Children.Add(SettingsUi.Description(context.Palette, L.Get("Settings_TabsNote")));
        Fill(rows, context);
        return page;
    }

    /// <summary>One card per tab: on or off, up or down. At least one stays on.</summary>
    private static void Fill(StackPanel rows, SettingsContext context)
    {
        var settings = context.Settings;
        rows.Children.Clear();
        var order = settings.TabOrder;
        for (var index = 0; index < order.Count; index++)
        {
            var id = order[index];
            if (IslandTabs.Parse(id) is not { } tab) continue;
            var position = index;

            ToggleSwitch? toggle = null;
            toggle = SettingsUi.Switch(!settings.HiddenTabs.Contains(id), on =>
            {
                if (on) settings.HiddenTabs.Remove(id);
                else if (order.Count - settings.HiddenTabs.Count > 1) settings.HiddenTabs.Add(id);
                else
                {
                    // The last tab showing stays on.
                    toggle!.IsOn = true;
                    return;
                }
                Changed(context);
            });

            var up = MoveButton(Glyphs.Up, L.Get("Settings_MoveUp"), position > 0, () => Swap(rows, context, position, position - 1));
            var down = MoveButton(Glyphs.Down, L.Get("Settings_MoveDown"), position < order.Count - 1, () => Swap(rows, context, position, position + 1));
            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { toggle, up, down },
            };

            var title = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children = { new FontIcon { Glyph = TabDock.Glyph(tab), FontSize = 16 }, new TextBlock { Text = TabDock.Title(tab) } },
            };
            rows.Children.Add(SettingsUi.Card(context.Palette, title, controls));
        }
    }

    private static void Swap(StackPanel rows, SettingsContext context, int from, int to)
    {
        var order = context.Settings.TabOrder;
        (order[from], order[to]) = (order[to], order[from]);
        Changed(context);
        Fill(rows, context);
    }

    private static void Changed(SettingsContext context)
    {
        context.Save();
        context.Island.ApplyTabs();
    }

    private static Button MoveButton(string glyph, string tooltip, bool enabled, Action click)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            IsEnabled = enabled,
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => click();
        return button;
    }
}
