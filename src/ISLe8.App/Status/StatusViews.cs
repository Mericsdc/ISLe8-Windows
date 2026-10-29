using System.Numerics;
using ISLe8.Core.Devices;
using ISLe8.Core.Geometry;
using ISLe8.Core.Motion;
using ISLe8.Core.Status;
using ISLe8.Devices;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace ISLe8.Status;

/// <summary>
/// The symbols the battery, network and HUD views share, from Segoe Fluent Icons. Segoe's battery
/// glyphs sit smaller in their box than SF Symbols' do, so they are drawn a size up.
/// </summary>
internal static class StatusGlyphs
{
    public const string Muted = "";
    public const string SpeakerLow = "";
    public const string SpeakerMedium = "";
    public const string SpeakerHigh = "";
    public const string SunMin = "";
    public const string SunMax = "";
    public const string BatteryCharging = "";
    public const string Cable = "";
    public const string Bolt = "";

    /// <summary>Segoe's battery glyphs are this much smaller than SF Symbols' at the same size.</summary>
    public const double BatteryScale = 1.3;

    /// <summary>battery.0percent, .25, .50, .75, .100percent: Segoe has tenths; these are the nearest.</summary>
    private static readonly string[] BatteryQuarters = ["", "", "", "", ""];

    public static string Battery(int percent) => BatteryQuarters[BatteryLook.Quarters(percent)];

    public static string Speaker(SpeakerLevel level) => level switch
    {
        SpeakerLevel.Muted => Muted,
        SpeakerLevel.Low => SpeakerLow,
        SpeakerLevel.Medium => SpeakerMedium,
        _ => SpeakerHigh,
    };

    /// <summary>The Mac's green, orange and red (BatteryColors), white, and the bolt's yellow.</summary>
    public static readonly Color Yellow = Color.FromArgb(255, 0xFF, 0xD6, 0x0A);

    public static SolidColorBrush Brush(StatusTint tint) => tint switch
    {
        StatusTint.Green => new SolidColorBrush(BatteryColors.Green),
        StatusTint.Orange => new SolidColorBrush(BatteryColors.Orange),
        StatusTint.Red => new SolidColorBrush(BatteryColors.Red),
        _ => Ui.White(0xFF),
    };

    public static void Tabular(TextBlock text) => Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
}

/// <summary>
/// Volume or brightness in the ears (SystemViews.swift, HUDAlertView): the icon on the left of the
/// pill's middle, a level bar and its percentage on the right.
/// </summary>
internal sealed class HudEars
{
    /// <summary>The right ear: 6 in, the bar, 6, the 34-wide percentage, 12 to the edge.</summary>
    private const double BarWidth = IslandSizes.HudEarWidth - 6 - 6 - 34 - 12;

    private readonly FontIcon _icon = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ui.White(0xFF), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _fill = new() { Height = 5, CornerRadius = new CornerRadius(2.5), Background = Ui.White(0xFF), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly TextBlock _percent = Ui.Text(11, semibold: true);
    private readonly Storyboard _grow = new();
    private readonly DoubleAnimation _width = new()
    {
        Duration = TimeSpan.FromMilliseconds(120),
        EnableDependentAnimation = true,
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
    };

    public HudEars()
    {
        _percent.Width = 34;
        _percent.TextAlignment = TextAlignment.Right;
        _percent.VerticalAlignment = VerticalAlignment.Center;
        StatusGlyphs.Tabular(_percent);
        Storyboard.SetTarget(_width, _fill);
        Storyboard.SetTargetProperty(_width, "Width");
        _grow.Children.Add(_width);

        var track = new Grid
        {
            Width = BarWidth,
            Height = 5,
            CornerRadius = new CornerRadius(2.5),
            Background = Ui.White(0x33),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _fill },
        };
        var level = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Padding = new Thickness(6, 0, 12, 0),
            Children = { track, _percent },
        };
        Root = new Grid
        {
            Height = IslandSizes.HeaderHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(IslandSizes.EarShoulder, 0, IslandSizes.EarShoulder, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(IslandSizes.HudEarWidth) },
                new ColumnDefinition(),
                new ColumnDefinition { Width = new GridLength(IslandSizes.HudEarWidth) },
            },
            Children = { _icon, level },
        };
        Grid.SetColumn(level, 2);
    }

    public Grid Root { get; }

    /// <summary>A new HUD starts at its level; one already showing eases the bar there.</summary>
    public void Show(HudValue value, bool animate)
    {
        _icon.Glyph = value.Kind == HudKind.Brightness
            ? HudLook.IsDim(value.Level) ? StatusGlyphs.SunMin : StatusGlyphs.SunMax
            : StatusGlyphs.Speaker(HudLook.Speaker(value.Level, value.IsMuted));
        _percent.Text = L.Format("Devices_Percent", HudLook.Percent(value.Shown));
        var width = BarWidth * value.Shown;
        _grow.Stop();
        if (animate)
        {
            _width.To = width;
            _grow.Begin();
        }
        else
        {
            _fill.Width = width;
        }
    }
}

/// <summary>
/// Charging, unplugged, or a cable in or out (SystemViews.swift, StatusEarsView): the island widens
/// beside its middle with the symbol on the left and the figure on the right, the way an iPhone
/// shows it is charging. Both slide out from under the middle with a little bounce, and back in
/// before the island folds.
/// </summary>
internal sealed class StatusEars
{
    private const float Hidden = 0.6f * (float)IslandSizes.StatusEarWidth;

    private readonly Compositor _compositor;
    private readonly FontIcon _icon = new() { FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _text = new()
    {
        FontSize = 13,
        FontWeight = FontWeights.Bold,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
    };
    private readonly Grid _leading;
    private readonly Grid _trailing;
    private readonly bool _animations = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

    public StatusEars()
    {
        if (Ui.Font is { } font) _text.FontFamily = font;
        StatusGlyphs.Tabular(_text);
        _leading = Ear(_icon);
        _trailing = Ear(_text);
        Grid.SetColumn(_trailing, 2);
        Root = new Grid
        {
            Height = IslandSizes.HeaderHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(IslandSizes.EarShoulder, 0, IslandSizes.EarShoulder, 0),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(IslandSizes.StatusEarWidth) },
                new ColumnDefinition(),
                new ColumnDefinition { Width = new GridLength(IslandSizes.StatusEarWidth) },
            },
            Children = { _leading, _trailing },
        };
        _compositor = ElementCompositionPreview.GetElementVisual(Root).Compositor;
    }

    public Grid Root { get; }

    /// <summary>Fills the ears and starts them under the middle, small and clear, then springs them out.</summary>
    public void Show(string glyph, double glyphSize, string text, string label, StatusTint tint)
    {
        var brush = StatusGlyphs.Brush(tint);
        _icon.Glyph = glyph;
        _icon.FontSize = glyphSize;
        _icon.Foreground = brush;
        _text.Text = text;
        _text.Foreground = brush;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Root, $"{label} {text}");
        foreach (var (ear, inward) in new[] { (_leading, Hidden), (_trailing, -Hidden) })
        {
            var visual = Visual(ear);
            visual.StopAnimation("Translation");
            visual.StopAnimation("Scale");
            visual.StopAnimation("Opacity");
            visual.Properties.InsertVector3("Translation", new Vector3(inward, 0, 0));
            visual.Scale = new Vector3(0.4f, 0.4f, 1);
            visual.Opacity = 0;
            Move(visual, Vector3.Zero, 1, 1, outward: true);
        }
        Bounce();
    }

    /// <summary>The symbol pulses once as it arrives (symbolEffect(.bounce)): a quick squash, a little over, back.</summary>
    private void Bounce()
    {
        if (!_animations) return;
        var icon = ElementCompositionPreview.GetElementVisual(_icon);
        icon.StartAnimation("CenterPoint", _compositor.CreateExpressionAnimation("Vector3(this.Target.Size.X / 2, this.Target.Size.Y / 2, 0)"));
        var bounce = _compositor.CreateVector3KeyFrameAnimation();
        bounce.InsertKeyFrame(0.3f, new Vector3(0.86f, 0.86f, 1));
        bounce.InsertKeyFrame(0.65f, new Vector3(1.12f, 1.12f, 1));
        bounce.InsertKeyFrame(1, Vector3.One);
        bounce.Duration = TimeSpan.FromMilliseconds(450);
        bounce.DelayTime = TimeSpan.FromMilliseconds(320);
        icon.StartAnimation("Scale", bounce);
    }

    /// <summary>Just before the island folds: both slide back under the middle (0.22 s, easing in).</summary>
    public void Retract()
    {
        Move(Visual(_leading), new Vector3(Hidden, 0, 0), 0.4f, 0, outward: false);
        Move(Visual(_trailing), new Vector3(-Hidden, 0, 0), 0.4f, 0, outward: false);
    }

    /// <summary>The text for a cable taken out, as the link settles (Wi-Fi a moment after offline).</summary>
    public void SetText(string glyph, string text)
    {
        _icon.Glyph = glyph;
        _text.Text = text;
    }

    private void Move(Visual visual, Vector3 translation, float scale, float opacity, bool outward)
    {
        if (outward && _animations)
        {
            // spring(response: 0.45, dampingFraction: 0.6).delay(0.05)
            var spring = new SpringSpec(0.45, 0.6);
            var delay = TimeSpan.FromMilliseconds(50);
            var move = _compositor.CreateSpringVector3Animation();
            move.FinalValue = translation;
            move.DampingRatio = (float)spring.DampingFraction;
            move.Period = spring.CompositionPeriod;
            move.DelayTime = delay;
            visual.StartAnimation("Translation", move);
            var grow = _compositor.CreateSpringVector3Animation();
            grow.FinalValue = new Vector3(scale, scale, 1);
            grow.DampingRatio = (float)spring.DampingFraction;
            grow.Period = spring.CompositionPeriod;
            grow.DelayTime = delay;
            visual.StartAnimation("Scale", grow);
            var fade = _compositor.CreateSpringScalarAnimation();
            fade.FinalValue = opacity;
            fade.DampingRatio = (float)spring.DampingFraction;
            fade.Period = spring.CompositionPeriod;
            fade.DelayTime = delay;
            visual.StartAnimation("Opacity", fade);
            return;
        }
        // In: ease-in 0.22 s; with animation effects off, 0.15 s out and 0.12 s in.
        var easing = outward
            ? _compositor.CreateCubicBezierEasingFunction(new Vector2(0, 0), new Vector2(0.58f, 1))
            : _compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(1, 1));
        var duration = TimeSpan.FromMilliseconds(outward ? 150 : _animations ? 220 : 120);
        var slide = _compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(1, translation, easing);
        slide.Duration = duration;
        visual.StartAnimation("Translation", slide);
        var size = _compositor.CreateVector3KeyFrameAnimation();
        size.InsertKeyFrame(1, new Vector3(scale, scale, 1), easing);
        size.Duration = duration;
        visual.StartAnimation("Scale", size);
        var clear = _compositor.CreateScalarKeyFrameAnimation();
        clear.InsertKeyFrame(1, opacity, easing);
        clear.Duration = duration;
        visual.StartAnimation("Opacity", clear);
    }

    private static Grid Ear(FrameworkElement content)
    {
        var ear = new Grid { Width = IslandSizes.StatusEarWidth, Height = IslandSizes.HeaderHeight, Children = { content } };
        ElementCompositionPreview.SetIsTranslationEnabled(ear, true);
        return ear;
    }

    /// <summary>The ear's visual, scaling about its own middle.</summary>
    private static Visual Visual(FrameworkElement ear)
    {
        var visual = ElementCompositionPreview.GetElementVisual(ear);
        visual.CenterPoint = new Vector3((float)IslandSizes.StatusEarWidth / 2, (float)IslandSizes.HeaderHeight / 2, 0);
        return visual;
    }
}

/// <summary>The battery running low or nearly out (SystemViews.swift, BatteryAlertView).</summary>
internal sealed class BatteryBanner
{
    private readonly FontIcon _icon = new() { FontSize = 17 * StatusGlyphs.BatteryScale, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = Ui.Text(12, semibold: true);
    private readonly TextBlock _detail = Ui.Text(11, alpha: 0x99);

    public BatteryBanner()
    {
        var icon = new Grid { Width = 30, Height = 30, Children = { _icon } };
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _detail } };
        var row = new Grid
        {
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            Children = { icon, text },
        };
        Grid.SetColumn(text, 1);
        Root = row;
    }

    public FrameworkElement Root { get; }

    public void Show(BatteryEvent battery)
    {
        _icon.Glyph = battery.Kind switch
        {
            BatteryEventKind.PluggedIn => StatusGlyphs.BatteryCharging,
            BatteryEventKind.Unplugged => StatusGlyphs.Battery(75),
            BatteryEventKind.Low => StatusGlyphs.Battery(25),
            _ => StatusGlyphs.Battery(0),
        };
        _icon.Foreground = StatusGlyphs.Brush(BatteryLook.Event(battery.Kind));
        _title.Text = L.Get(battery.Kind switch
        {
            BatteryEventKind.PluggedIn => "Devices_Charging",
            BatteryEventKind.Unplugged => "Devices_OnBattery",
            BatteryEventKind.Low => "Battery_Low",
            _ => "Battery_Critical",
        });
        _detail.Text = battery.IsStatus ? L.Format("Devices_Percent", battery.Percent) : L.Format("Battery_Left", battery.Percent);
    }
}

/// <summary>
/// The open island's status chips (HeaderBar.swift): this computer's battery, "LAN" while online by
/// cable, and the power going into the battery while it charges.
/// </summary>
internal sealed class StatusChips
{
    private readonly FontIcon _batteryIcon = new() { FontSize = 11 * StatusGlyphs.BatteryScale, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _batteryText = Chip.Text();
    private readonly TextBlock _wattsText = Chip.Text();

    public StatusChips()
    {
        StatusGlyphs.Tabular(_batteryText);
        StatusGlyphs.Tabular(_wattsText);
        Battery = Chip.Create(4, _batteryIcon, _batteryText);
        var cable = new FontIcon { Glyph = StatusGlyphs.Cable, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(BatteryColors.Green), VerticalAlignment = VerticalAlignment.Center };
        var lan = Chip.Text();
        lan.Text = "LAN";
        Lan = Chip.Create(4, cable, lan);
        ToolTipService.SetToolTip(Lan, L.Get("Status_LanHelp"));
        var bolt = new FontIcon { Glyph = StatusGlyphs.Bolt, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(StatusGlyphs.Yellow), VerticalAlignment = VerticalAlignment.Center };
        Watts = Chip.Create(3, bolt, _wattsText);
        ToolTipService.SetToolTip(Watts, L.Get("Status_WattsHelp"));
    }

    public Border Battery { get; }
    public Border Lan { get; }
    public Border Watts { get; }

    public void ShowBattery(PcBattery battery)
    {
        _batteryIcon.Glyph = battery.IsCharging ? StatusGlyphs.BatteryCharging : StatusGlyphs.Battery(battery.Percent);
        _batteryIcon.Foreground = StatusGlyphs.Brush(BatteryLook.Chip(battery));
        _batteryText.Text = L.Format("Devices_Percent", battery.Percent);
        ToolTipService.SetToolTip(Battery, L.Get(battery.IsCharging ? "Devices_Charging" : battery.IsPluggedIn ? "Devices_PluggedIn" : "Devices_OnBattery"));
    }

    public void ShowWatts(int watts) => _wattsText.Text = $"{watts} W";

    /// <summary>A capsule of an icon and a figure, 24 high (HeaderBar.swift's chips).</summary>
    private static class Chip
    {
        public static TextBlock Text()
        {
            var text = Ui.Text(11, semibold: true);
            text.VerticalAlignment = VerticalAlignment.Center;
            return text;
        }

        public static Border Create(double spacing, FrameworkElement icon, TextBlock text) => new()
        {
            Height = 24,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(12),
            Background = Ui.White(0x12),
            Visibility = Visibility.Collapsed,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing, VerticalAlignment = VerticalAlignment.Center, Children = { icon, text } },
        };
    }
}
