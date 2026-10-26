using ISLe8.Core.Geometry;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ISLe8.Island;

/// <summary>
/// The open island's top row (HeaderBar.swift): status chips on the left, pin and settings on the
/// right. The chips arrive with what they show (battery in step 8), so the left side starts empty.
/// </summary>
internal sealed class HeaderBar
{
    private readonly Button _pin;
    private readonly FontIcon _pinIcon;

    public HeaderBar()
    {
        Chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        _pin = CapsuleButton(Glyphs.Pin, L.Get("Menu_Pin"), () => PinClicked?.Invoke(), out _pinIcon);
        var settings = CapsuleButton(Glyphs.Settings, L.Get("Header_Settings"), () => SettingsClicked?.Invoke(), out _);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _pin, settings },
        };
        Root = new Grid
        {
            Height = IslandSizes.HeaderHeight,
            Padding = new Thickness(10, 0, 10, 0),
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } },
        };
        Root.Children.Add(Chips);
        Grid.SetColumn(buttons, 1);
        Root.Children.Add(buttons);
    }

    public Grid Root { get; }

    /// <summary>Where the status chips go, left to right.</summary>
    public StackPanel Chips { get; }

    public event Action? PinClicked;
    public event Action? SettingsClicked;

    public void SetPinned(bool pinned)
    {
        _pinIcon.Glyph = pinned ? Glyphs.Pinned : Glyphs.Pin;
        ToolTipService.SetToolTip(_pin, L.Get(pinned ? "Header_Unpin" : "Menu_Pin"));
    }

    /// <summary>A small capsule holding one icon, as on the Mac (IconButton).</summary>
    private static Button CapsuleButton(string glyph, string tooltip, Action click, out FontIcon icon)
    {
        icon = new FontIcon { Glyph = glyph, FontSize = 11, Foreground = Ui.White(0xCC) };
        var button = new Button
        {
            Content = icon,
            Width = 26,
            Height = 24,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0),
            Background = Ui.White(0x12),
        };
        button.Resources["ButtonBackgroundPointerOver"] = Ui.White(0x24);
        button.Resources["ButtonBackgroundPressed"] = Ui.White(0x18);
        button.Resources["ButtonBorderBrushPointerOver"] = Ui.Clear();
        button.Resources["ButtonBorderBrushPressed"] = Ui.Clear();
        button.Resources["ButtonForegroundPointerOver"] = Ui.White(0xFF);
        button.Resources["ButtonForegroundPressed"] = Ui.White(0xCC);
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => click();
        return button;
    }
}
