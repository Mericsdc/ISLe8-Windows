using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ISLe8.UI;

/// <summary>
/// The few brushes the settings window sets by hand, for its light or dark theme. Controls pick
/// their own colours from the theme; a brush read from code would stay in the app's theme, which
/// is dark for the island. One set is shared by every card, so a theme change recolours them all.
/// </summary>
internal sealed class SettingsPalette
{
    public SolidColorBrush Card { get; } = new();
    public SolidColorBrush CardStroke { get; } = new();
    public SolidColorBrush SecondaryText { get; } = new();

    /// <summary>Windows 11's CardBackgroundFillColorDefault, CardStrokeColorDefault and TextFillColorSecondary.</summary>
    public void Apply(ElementTheme theme)
    {
        var dark = theme == ElementTheme.Dark;
        Card.Color = dark ? Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF);
        CardStroke.Color = dark ? Color.FromArgb(0x19, 0, 0, 0) : Color.FromArgb(0x0F, 0, 0, 0);
        SecondaryText.Color = dark ? Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x9E, 0, 0, 0);
    }
}

/// <summary>Builders for the settings window, in the Windows 11 Settings style: a title, then cards.</summary>
internal static class SettingsUi
{
    public static TextBlock PageTitle(string text) => new()
    {
        Text = text,
        FontSize = 28,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 16),
    };

    public static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(1, 20, 0, 6),
    };

    /// <summary>Space between groups of cards that need no heading.</summary>
    public static FrameworkElement Gap() => new Border { Height = 20 };

    /// <summary>A setting: its name (and what it does) on the left, its control on the right.</summary>
    public static Border Card(SettingsPalette palette, string title, FrameworkElement control, string? description = null) =>
        Card(palette, title, control, description is null ? null : Description(palette, description));

    /// <summary>The same, with a description the page can change later.</summary>
    public static Border Card(SettingsPalette palette, string title, FrameworkElement control, TextBlock? description)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        if (description is not null) text.Children.Add(description);

        control.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid
        {
            ColumnSpacing = 16,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        grid.Children.Add(text);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        return new Border
        {
            Background = palette.Card,
            BorderBrush = palette.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 12, 16, 12),
            MinHeight = 64,
            Margin = new Thickness(0, 0, 0, 4),
            Child = grid,
        };
    }

    public static TextBlock Description(SettingsPalette palette, string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = palette.SecondaryText,
        TextWrapping = TextWrapping.Wrap,
    };

    public static ComboBox Choices(IEnumerable<string> items, int selected, Action<int> chose)
    {
        var box = new ComboBox { MinWidth = 220 };
        foreach (var item in items) box.Items.Add(item);
        box.SelectedIndex = selected;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0) chose(box.SelectedIndex);
        };
        return box;
    }

    /// <summary>A switch without "On"/"Off" beside it: WinUI would write those in Windows' language, not the island's.</summary>
    public static ToggleSwitch Switch(bool on, Action<bool> toggled)
    {
        var toggle = new ToggleSwitch { IsOn = on, OnContent = "", OffContent = "", MinWidth = 0 };
        toggle.Toggled += (_, _) => toggled(toggle.IsOn);
        return toggle;
    }
}
