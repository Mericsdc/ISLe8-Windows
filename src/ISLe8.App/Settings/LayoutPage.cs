using ISLe8.Core.Island;
using ISLe8.Island;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ISLe8.Settings;

/// <summary>
/// Settings › Layout, after the Mac's (CustomizationSettings.swift, LayoutSettings): the dock's tabs,
/// their order and which ones show; then Home's columns, their order, and which ones are there.
/// </summary>
internal static class LayoutPage
{
    public static FrameworkElement Build(SettingsContext context)
    {
        var page = new StackPanel { MaxWidth = 860 };
        page.Children.Add(SettingsUi.PageTitle(L.Get("Settings_Layout")));
        page.Children.Add(SettingsUi.SectionHeader(L.Get("Settings_Tabs")));
        var tabs = new StackPanel();
        page.Children.Add(tabs);
        page.Children.Add(SettingsUi.Description(context.Palette, L.Get("Settings_TabsNote")));
        FillTabs(tabs, context);

        page.Children.Add(SettingsUi.SectionHeader(L.Get("Settings_HomeColumns")));
        var columns = new StackPanel();
        page.Children.Add(columns);
        page.Children.Add(SettingsUi.Description(context.Palette, L.Get("Settings_HomeColumnsNote")));
        FillColumns(columns, context);
        return page;
    }

    // MARK: Tabs

    /// <summary>One card per tab: on or off, up or down. At least one stays on.</summary>
    private static void FillTabs(StackPanel rows, SettingsContext context)
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
                TabsChanged(context);
            });

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children = { toggle },
            };
            AddMoveButtons(controls, position, order.Count, to =>
            {
                (order[position], order[to]) = (order[to], order[position]);
                TabsChanged(context);
                FillTabs(rows, context);
            });
            rows.Children.Add(SettingsUi.Card(context.Palette, Heading(TabDock.Glyph(tab), TabDock.Title(tab)), controls));
        }
    }

    private static void TabsChanged(SettingsContext context)
    {
        context.Save();
        context.Island.ApplyTabs();
    }

    // MARK: Home columns

    /// <summary>One card per column: up, down or away; then "add column" with the ones not there.</summary>
    private static void FillColumns(StackPanel rows, SettingsContext context)
    {
        var settings = context.Settings;
        rows.Children.Clear();
        var columns = settings.HomeTiles;
        for (var index = 0; index < columns.Count; index++)
        {
            var id = columns[index];
            var position = index;
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            AddMoveButtons(controls, position, columns.Count, to =>
            {
                (columns[position], columns[to]) = (columns[to], columns[position]);
                ColumnsChanged(rows, context);
            });
            controls.Children.Add(SmallButton(Glyphs.Remove, L.Get("Settings_Remove"), enabled: true, () =>
            {
                columns.RemoveAt(position);
                ColumnsChanged(rows, context);
            }));
            rows.Children.Add(SettingsUi.Card(context.Palette, Heading(HomeView.Glyph(id), HomeView.Title(id)), controls));
        }

        var missing = HomeColumns.Missing(settings);
        if (missing.Count == 0) return;
        var menu = new MenuFlyout();
        foreach (var id in missing)
        {
            var item = new MenuFlyoutItem { Text = HomeView.Title(id), Icon = new FontIcon { Glyph = HomeView.Glyph(id) } };
            item.Click += (_, _) =>
            {
                columns.Add(id);
                ColumnsChanged(rows, context);
            };
            menu.Items.Add(item);
        }
        rows.Children.Add(new DropDownButton
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { new FontIcon { Glyph = Glyphs.Add, FontSize = 12 }, new TextBlock { Text = L.Get("Settings_AddColumn") } },
            },
            Flyout = menu,
            Margin = new Thickness(0, 4, 0, 8),
        });
    }

    private static void ColumnsChanged(StackPanel rows, SettingsContext context)
    {
        context.Save();
        context.Island.ApplyHomeColumns();
        FillColumns(rows, context);
    }

    // MARK: Pieces

    private static StackPanel Heading(string glyph, string title) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 12,
        Children = { new FontIcon { Glyph = glyph, FontSize = 16 }, new TextBlock { Text = title } },
    };

    /// <summary>Up and down, as the Mac's MoveButtons: <paramref name="swap"/> gets the place to swap with.</summary>
    private static void AddMoveButtons(Panel controls, int position, int count, Action<int> swap)
    {
        controls.Children.Add(SmallButton(Glyphs.Up, L.Get("Settings_MoveUp"), position > 0, () => swap(position - 1)));
        controls.Children.Add(SmallButton(Glyphs.Down, L.Get("Settings_MoveDown"), position < count - 1, () => swap(position + 1)));
    }

    private static Button SmallButton(string glyph, string tooltip, bool enabled, Action click)
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
