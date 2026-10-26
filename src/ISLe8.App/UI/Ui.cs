using ISLe8.Core.Theming;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace ISLe8.UI;

/// <summary>Segoe Fluent Icons (Windows 11), falling back to Segoe MDL2 Assets on Windows 10.</summary>
internal static class Glyphs
{
    public const string Play = "";
    public const string Pause = "";
    public const string Previous = "";
    public const string Next = "";
    public const string Shuffle = "";
    public const string RepeatAll = "";
    public const string RepeatOne = "";
    public const string Music = "";
    public const string Pin = "";
    public const string Palette = "";
    public const string Display = "";
    public const string Language = "";
    public const string Quit = "";
}

internal static class ThemeColors
{
    public static Color ToColor(this ThemeColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    public static SolidColorBrush ToBrush(this ThemeColor color) => new(color.ToColor());
}

/// <summary>Small builders for the island's controls; the UI is written in C# rather than XAML.</summary>
internal static class Ui
{
    /// <summary>The theme's typeface, or null for Segoe UI Variable.</summary>
    public static FontFamily? Font { get; set; }

    public static FontFamily? FontFor(IslandTypeface typeface) => typeface switch
    {
        IslandTypeface.Monospaced => new FontFamily("Cascadia Mono, Consolas"),
        IslandTypeface.Serif => new FontFamily("Georgia"),
        // Windows has no rounded system face; the display cut of Segoe UI Variable is the softest.
        IslandTypeface.Rounded => new FontFamily("Segoe UI Variable Display"),
        _ => null,
    };

    public static SolidColorBrush White(byte alpha) => new(Color.FromArgb(alpha, 255, 255, 255));

    public static SolidColorBrush Clear() => new(Colors.Transparent);

    public static TextBlock Text(double size, byte alpha = 255, bool semibold = false)
    {
        var text = new TextBlock
        {
            FontSize = size,
            FontWeight = semibold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = White(alpha),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            MaxLines = 1,
        };
        if (Font is { } font) text.FontFamily = font;
        return text;
    }

    /// <summary>A round, borderless button holding one icon.</summary>
    public static Button IconButton(string glyph, double size, double iconSize, string tooltip, Action click, out FontIcon icon)
    {
        icon = new FontIcon { Glyph = glyph, FontSize = iconSize };
        var button = new Button
        {
            Content = icon,
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(size / 2),
            BorderThickness = new Thickness(0),
            Background = Clear(),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        // Lightweight styling: only the brushes the default template looks up.
        button.Resources["ButtonBackgroundPointerOver"] = White(0x1F);
        button.Resources["ButtonBackgroundPressed"] = White(0x14);
        button.Resources["ButtonBackgroundDisabled"] = Clear();
        button.Resources["ButtonBorderBrushPointerOver"] = Clear();
        button.Resources["ButtonBorderBrushPressed"] = Clear();
        button.Resources["ButtonBorderBrushDisabled"] = Clear();
        button.Resources["ButtonForegroundPointerOver"] = White(0xFF);
        button.Resources["ButtonForegroundPressed"] = White(0xCC);
        button.Resources["ButtonForegroundDisabled"] = White(0x40);
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => click();
        return button;
    }
}
