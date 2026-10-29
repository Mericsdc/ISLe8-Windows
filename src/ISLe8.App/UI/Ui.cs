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
    public const string Settings = "";
    public const string Pinned = "";
    public const string Home = "";
    public const string Shelf = "";
    public const string Clipboard = "";
    public const string Agents = "";
    public const string Devices = "";
    public const string Layout = "";
    public const string Up = "";
    public const string Down = "";
    public const string Calendar = "";
    public const string Weather = "";
    public const string Timer = "\uE823";
    public const string Stopwatch = "\uE916";
    public const string Focus = "\uEA80";
    public const string Break = "\uEC32";
    public const string Stop = "\uE71A";
    public const string Widgets = "\uECA5";
    public const string Cpu = "\uE950";
    public const string MemoryChip = "\uE964";
    public const string Drive = "\uEDA2";
    public const string Ethernet = "\uE839";
    public const string Wifi = "\uE701";
    public const string WifiOff = "\uEB5E";
    public const string Network = "\uE968";
    public const string Download = "\uE896";
    public const string Upload = "\uE898";
    public const string OpenOut = "\uE8A7";
    public const string Notes = "\uE70B";
    public const string System = "\uEC4A";
    public const string Edit = "\uE70F";
    public const string Delete = "\uE74D";
    public const string Check = "\uE73E";
    public const string Close = "\uE711";
    public const string Video = "\uE714";
    public const string Refresh = "\uE72C";
    public const string Offline = "\uE701";
    public const string Location = "\uE81D";
    public const string Forward = "\uE76C";
    public const string Back = "\uE76B";
    public const string Add = "";
    public const string Remove = "";
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
