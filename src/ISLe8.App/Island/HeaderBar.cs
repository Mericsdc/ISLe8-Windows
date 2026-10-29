using ISLe8.Core.Geometry;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ISLe8.Island;

/// <summary>
/// The open island's top row (HeaderBar.swift): status chips on the left, pin and settings on the
/// right. Each side keeps to the space beside the pill's middle; when the chips don't fit, the
/// least important ones go first.
/// </summary>
internal sealed class HeaderBar
{
    private readonly Button _pin;
    private readonly FontIcon _pinIcon;
    private readonly List<(FrameworkElement Chip, int Drop)> _chips = [];
    private readonly Dictionary<FrameworkElement, bool> _wanted = [];
    private double _side = double.PositiveInfinity;

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

    /// <summary>
    /// Adds a chip at the right end. <paramref name="drop"/> 0 always stays; otherwise the chip goes
    /// in that order when they don't all fit (devices 1, then the watts 2).
    /// </summary>
    public void AddChip(FrameworkElement chip, int drop)
    {
        _chips.Add((chip, drop));
        _wanted[chip] = false;
        chip.Visibility = Visibility.Collapsed;
        Chips.Children.Add(chip);
    }

    /// <summary>Whether a chip has something to show; it shows if there is room.</summary>
    public void Want(FrameworkElement chip, bool wanted)
    {
        _wanted[chip] = wanted;
        Fit();
    }

    /// <summary>The open island's width: each side gets what is beside the pill's middle (HeaderBar.swift, side).</summary>
    public void SetIslandWidth(double width)
    {
        _side = Math.Max((width - IslandSizes.EarGap) / 2 - 18, 0);
        Fit();
    }

    /// <summary>ViewThatFits: all of them, then without the devices chip, then without the watts too.</summary>
    private void Fit()
    {
        var steps = _chips.Count == 0 ? 0 : _chips.Max(chip => chip.Drop);
        for (var dropping = 0; dropping <= steps; dropping++)
        {
            var width = 0.0;
            var shown = 0;
            foreach (var (chip, drop) in _chips)
            {
                var show = _wanted[chip] && (drop == 0 || drop > dropping);
                chip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (!show) continue;
                chip.Measure(new Windows.Foundation.Size(double.PositiveInfinity, IslandSizes.HeaderHeight));
                width += chip.DesiredSize.Width;
                shown++;
            }
            if (width + Chips.Spacing * Math.Max(shown - 1, 0) <= _side) return;
        }
    }

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
