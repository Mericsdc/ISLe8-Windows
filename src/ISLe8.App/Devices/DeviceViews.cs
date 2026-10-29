using ISLe8.Core.Devices;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace ISLe8.Devices;

/// <summary>Which part of a device a ring measures (DeviceViews.swift, BatteryBadge.Part).</summary>
internal enum BatteryPart
{
    Main,
    Left,
    Right,
    Case,
    /// <summary>A second battery charging in a base station.</summary>
    Spare,
}

/// <summary>
/// Device pictures. Segoe has glyphs for most kinds; AirPods' buds and case, which the Mac draws
/// with SF Symbols, are drawn here from a few shapes.
/// </summary>
internal static class DeviceIcon
{
    public static string Glyph(DeviceKind kind) => kind switch
    {
        DeviceKind.AirPodsMax or DeviceKind.Beats or DeviceKind.Headphones => "",
        DeviceKind.Speaker => "",
        DeviceKind.Keyboard => "",
        DeviceKind.Mouse => "",
        DeviceKind.Trackpad => "",
        DeviceKind.Gamepad => "",
        DeviceKind.Phone => "",
        _ => "",
    };

    public const string Laptop = "";
    public const string Battery = "";
    public const string Bolt = "";

    /// <summary>The device, or one part of it, at <paramref name="size"/> DIPs.</summary>
    public static FrameworkElement Create(DeviceKind kind, BatteryPart part, double size, Brush brush)
    {
        var pods = kind is DeviceKind.AirPods or DeviceKind.AirPodsPro;
        var pro = kind == DeviceKind.AirPodsPro;
        return part switch
        {
            BatteryPart.Left => Drawing(size, brush, Bud(pro, mirrored: false)),
            BatteryPart.Right => Drawing(size, brush, Bud(pro, mirrored: true)),
            BatteryPart.Case => CaseDrawing(size, brush),
            BatteryPart.Spare => new FontIcon { Glyph = Battery, FontSize = size, Foreground = brush },
            _ when pods => Drawing(size, brush, Bud(pro, mirrored: false, x: -3.5), Bud(pro, mirrored: true, x: 3.5)),
            _ => new FontIcon { Glyph = Glyph(kind), FontSize = size, Foreground = brush },
        };
    }

    /// <summary>A bud in a 24-unit square: the head, and the stem (shorter on AirPods Pro).</summary>
    private static Geometry Bud(bool pro, bool mirrored, double x = 0)
    {
        var head = new EllipseGeometry { Center = new Point(mirrored ? 13.5 : 10.5, 7), RadiusX = 4.2, RadiusY = 4.2 };
        var stem = new RectangleGeometry
        {
            Rect = new Rect(mirrored ? 9.3 : 12.5, 7, 2.2, pro ? 11 : 15),
        };
        // WinUI's RectangleGeometry has no corner radius: a round cap closes the stem.
        var cap = new EllipseGeometry { Center = new Point(mirrored ? 10.4 : 13.6, pro ? 18 : 22), RadiusX = 1.1, RadiusY = 1.1 };
        var group = new GeometryGroup { FillRule = FillRule.Nonzero, Children = { head, stem, cap } };
        if (x != 0) group.Transform = new TranslateTransform { X = x };
        return group;
    }

    private static Viewbox Drawing(double size, Brush brush, params Geometry[] shapes)
    {
        var canvas = new Grid { Width = 24, Height = 24 };
        foreach (var shape in shapes) canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = shape, Fill = brush });
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }

    /// <summary>The charging case: a rounded box, its lid's seam and the light.</summary>
    private static Viewbox CaseDrawing(double size, Brush brush)
    {
        var box = new Rectangle { Width = 18, Height = 14, RadiusX = 4.5, RadiusY = 4.5, Stroke = brush, StrokeThickness = 1.8, Margin = new Thickness(3, 5, 3, 5) };
        var seam = new Rectangle { Width = 18, Height = 1.4, Fill = brush, Margin = new Thickness(3, 9.6, 3, 13), VerticalAlignment = VerticalAlignment.Top };
        var light = new Ellipse { Width = 2, Height = 2, Fill = brush, Margin = new Thickness(11, 13, 11, 9), VerticalAlignment = VerticalAlignment.Top };
        return new Viewbox { Width = size, Height = size, Child = new Grid { Width = 24, Height = 24, Children = { box, seam, light } } };
    }
}

/// <summary>The rings' colours, as on the System tab: green, orange, red.</summary>
internal static class BatteryColors
{
    public static readonly Color Green = Color.FromArgb(255, 0x30, 0xD1, 0x58);
    public static readonly Color Orange = Color.FromArgb(255, 0xFF, 0x9F, 0x0A);
    public static readonly Color Red = Color.FromArgb(255, 0xFF, 0x45, 0x3A);

    public static SolidColorBrush For(int level) => new(DeviceRules.Tint(level) switch
    {
        BatteryTint.Red => Red,
        BatteryTint.Orange => Orange,
        _ => Green,
    });

    public static string Percent(int level) => L.Format("Devices_Percent", level);

    /// <summary>One line per device for the chip's tooltip: "AirPods · Sol %80 · Sağ %70 · Kutu %90".</summary>
    public static string Summary(WirelessDevice device)
    {
        var parts = new List<string> { device.Name };
        if (device.Main is { } main) parts.Add(Percent(main));
        if (device.Left is { } left) parts.Add(L.Format("Devices_LeftLevel", left));
        if (device.Right is { } right) parts.Add(L.Format("Devices_RightLevel", right));
        if (device.Case is { } caseLevel) parts.Add(L.Format("Devices_CaseLevel", caseLevel));
        if (device.Spare is { } spare) parts.Add(L.Format("Devices_SpareLevel", spare));
        return string.Join(" · ", parts);
    }
}

/// <summary>An arc from the top, clockwise, for <paramref name="fraction"/> of a ring.</summary>
internal static class Rings
{
    public static Grid Ring(double size, double stroke, int level, Color track, FrameworkElement? inside)
    {
        var arc = new Microsoft.UI.Xaml.Shapes.Path
        {
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Stroke = BatteryColors.For(level),
            Data = Arc(size, stroke, level / 100.0),
        };
        var ring = new Grid
        {
            Width = size,
            Height = size,
            Children = { new Ellipse { Width = size, Height = size, Stroke = new SolidColorBrush(track), StrokeThickness = stroke }, arc },
        };
        if (inside is not null)
        {
            inside.HorizontalAlignment = HorizontalAlignment.Center;
            inside.VerticalAlignment = VerticalAlignment.Center;
            ring.Children.Add(inside);
        }
        return ring;
    }

    private static Geometry? Arc(double size, double stroke, double fraction)
    {
        if (fraction <= 0.001) return null;
        var radius = (size - stroke) / 2;
        var center = size / 2;
        if (fraction >= 0.999) return new EllipseGeometry { Center = new Point(center, center), RadiusX = radius, RadiusY = radius };
        var angle = fraction * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle)),
            Size = new Size(radius, radius),
            IsLargeArc = fraction > 0.5,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}

/// <summary>A small ring with what it measures inside ("L", "R", the case) and the percentage beside it (DeviceViews.swift, BatteryBadge).</summary>
internal static class BatteryBadge
{
    public static FrameworkElement Create(DeviceKind kind, BatteryPart part, int level, double size = 18)
    {
        FrameworkElement? label = part switch
        {
            BatteryPart.Left or BatteryPart.Right => Letter(part == BatteryPart.Left ? "L" : "R", size),
            BatteryPart.Case or BatteryPart.Spare => DeviceIcon.Create(kind, part, size * 0.62, Ui.White(0xCC)),
            _ => null,
        };
        var percent = Ui.Text(11, alpha: 0xBF);
        percent.FontWeight = FontWeights.Medium;
        percent.Text = BatteryColors.Percent(level);
        percent.VerticalAlignment = VerticalAlignment.Center;
        if (level <= DeviceRules.LowLevel) percent.Foreground = new SolidColorBrush(BatteryColors.Red);
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(percent, FontNumeralAlignment.Tabular);
        var badge = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Rings.Ring(size, 2.5, level, Color.FromArgb(0x1F, 255, 255, 255), label), percent },
        };
        AutomationProperties.SetName(badge, $"{PartName(part)} {BatteryColors.Percent(level)}".Trim());
        return badge;
    }

    /// <summary>Every level a device reports: both buds and the case, or its one battery (DeviceViews.swift, DeviceLevels).</summary>
    public static StackPanel Levels(WirelessDevice device, double size = 18)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (device.Main is { } main) row.Children.Add(Create(device.Kind, BatteryPart.Main, main, size));
        if (device.Left is { } left) row.Children.Add(Create(device.Kind, BatteryPart.Left, left, size));
        if (device.Right is { } right) row.Children.Add(Create(device.Kind, BatteryPart.Right, right, size));
        if (device.Case is { } caseLevel) row.Children.Add(Create(device.Kind, BatteryPart.Case, caseLevel, size));
        if (device.Spare is { } spare) row.Children.Add(Create(device.Kind, BatteryPart.Spare, spare, size));
        return row;
    }

    public static string PartName(BatteryPart part) => part switch
    {
        BatteryPart.Left => L.Get("Devices_Left"),
        BatteryPart.Right => L.Get("Devices_Right"),
        BatteryPart.Case => L.Get("Devices_Case"),
        BatteryPart.Spare => L.Get("Devices_Spare"),
        _ => "",
    };

    private static TextBlock Letter(string text, double size)
    {
        var letter = Ui.Text(size * 0.42, alpha: 0xCC, semibold: true);
        letter.FontWeight = FontWeights.Bold;
        letter.Text = text;
        return letter;
    }
}

/// <summary>
/// The Devices tab (DeviceViews.swift, DevicesTab): a card per battery-powered thing, this computer
/// first, every battery a ring with the device inside and its level below, like the batteries widget
/// on iPhone. Cards share a row by how many rings they hold.
/// </summary>
internal sealed class DevicesTabView
{
    private readonly DeviceService _devices;
    private readonly IslandTheme _theme;
    private readonly StackPanel _rows = new() { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _empty = Ui.Text(11, alpha: 0x73);
    private bool _active;
    private bool _stale = true;

    public DevicesTabView(DeviceService devices, IslandTheme theme)
    {
        _devices = devices;
        _theme = theme;
        _empty.Text = L.Get("Devices_Empty");
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.MaxLines = 2;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        var stack = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { _rows, _empty } };
        Root = new Grid { Background = Ui.Clear(), Children = { stack } };
        Root.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5 || Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 0.5) Build();
        };
        _devices.Changed += OnChanged;
    }

    public Grid Root { get; }

    /// <summary>Drawn only while on screen; what changed meanwhile is drawn when it shows.</summary>
    public void SetActive(bool active)
    {
        _active = active;
        if (active && _stale) Build();
    }

    public void Detach() => _devices.Changed -= OnChanged;

    private void OnChanged()
    {
        _stale = true;
        if (_active) Build();
    }

    private sealed record Cell(DeviceKind Kind, BatteryPart Part, string Glyph, int Level, bool IsCharging, string Label);

    private sealed record Card(string Title, string? Subtitle, IReadOnlyList<Cell> Cells);

    private void Build()
    {
        var width = Root.ActualWidth;
        var height = Root.ActualHeight;
        if (width <= 0 || height <= 0) return;
        _stale = false;
        var cards = Cards();
        var rows = DeviceRules.Rows(cards, card => card.Cells.Count);
        var (rowHeight, ringSize) = DeviceRules.Fit(rows.Count, height);
        _rows.Children.Clear();
        foreach (var row in rows)
        {
            var rings = row.Sum(card => card.Cells.Count);
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Height = rowHeight };
            foreach (var card in row)
            {
                var view = CardView(card, ringSize);
                view.Width = DeviceRules.CardWidth(width, row.Count, rings, card.Cells.Count);
                line.Children.Add(view);
            }
            _rows.Children.Add(line);
        }
        _empty.Visibility = _devices.Devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<Card> Cards()
    {
        var cards = new List<Card>();
        if (_devices.Battery is { } battery)
        {
            var status = L.Get(battery.IsCharging ? "Devices_Charging" : battery.IsPluggedIn ? "Devices_PluggedIn" : "Devices_OnBattery");
            var title = L.Get("Devices_ThisPc");
            cards.Add(new Card(title, status, [new Cell(DeviceKind.Other, BatteryPart.Main, DeviceIcon.Laptop, battery.Percent, battery.IsCharging, title)]));
        }
        foreach (var device in _devices.Devices)
        {
            var cells = new List<Cell>();
            if (device.Main is { } main) cells.Add(new Cell(device.Kind, BatteryPart.Main, DeviceIcon.Glyph(device.Kind), main, device.IsCharging, device.ShortName));
            if (device.Left is { } left) cells.Add(new Cell(device.Kind, BatteryPart.Left, "", left, false, L.Get("Devices_Left")));
            if (device.Right is { } right) cells.Add(new Cell(device.Kind, BatteryPart.Right, "", right, false, L.Get("Devices_Right")));
            if (device.Case is { } caseLevel) cells.Add(new Cell(device.Kind, BatteryPart.Case, "", caseLevel, false, L.Get("Devices_Case")));
            if (device.Spare is { } spare) cells.Add(new Cell(device.Kind, BatteryPart.Spare, "", spare, false, L.Get("Devices_Spare")));
            cards.Add(new Card(device.ShortName, device.IsCharging ? L.Get("Devices_Charging") : null, cells));
        }
        return cards;
    }

    private Border CardView(Card card, double ringSize)
    {
        var cells = new StackPanel { Orientation = Orientation.Horizontal, Spacing = ringSize * 0.32, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var cell in card.Cells)
        {
            var icon = cell.Glyph == DeviceIcon.Laptop
                ? new FontIcon { Glyph = DeviceIcon.Laptop, FontSize = ringSize * 0.36, Foreground = Ui.White(0xE6) }
                : DeviceIcon.Create(cell.Kind, cell.Part, ringSize * 0.4, Ui.White(0xE6));
            if (icon is FontIcon glyph) glyph.FontSize = ringSize * 0.36;
            var percent = Ui.Text(13, semibold: true);
            percent.Text = BatteryColors.Percent(cell.Level);
            Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(percent, FontNumeralAlignment.Tabular);
            var level = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            if (cell.IsCharging)
            {
                level.Children.Add(new FontIcon { Glyph = DeviceIcon.Bolt, FontSize = 9, Foreground = new SolidColorBrush(BatteryColors.Green), VerticalAlignment = VerticalAlignment.Center });
            }
            level.Children.Add(percent);
            var view = new StackPanel
            {
                Spacing = 6,
                Children = { Rings.Ring(ringSize, ringSize * 0.085, cell.Level, Color.FromArgb(0x1A, 255, 255, 255), icon), level },
            };
            AutomationProperties.SetName(view, $"{cell.Label} {BatteryColors.Percent(cell.Level)}");
            cells.Children.Add(view);
        }
        var title = Ui.Text(11, semibold: true);
        title.Text = card.Title;
        title.HorizontalAlignment = HorizontalAlignment.Center;
        var caption = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center, Children = { title } };
        if (card.Subtitle is { } subtitle)
        {
            var text = Ui.Text(10, alpha: 0x80);
            text.Text = subtitle;
            text.HorizontalAlignment = HorizontalAlignment.Center;
            caption.Children.Add(text);
        }
        var content = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Children = { cells, caption } };
        return Tiles.Tile(_theme, content, new Thickness(8, 0, 8, 0));
    }
}

/// <summary>In the header: the connected device whose battery runs out first (DeviceViews.swift, DevicesChip).</summary>
internal sealed class DevicesChip
{
    private readonly DeviceService _devices;
    private readonly Func<bool> _shown;
    private readonly StackPanel _content = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };

    public DevicesChip(DeviceService devices, Func<bool> shown)
    {
        _devices = devices;
        _shown = shown;
        Root = new Border
        {
            Height = 24,
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(12),
            Background = Ui.White(0x12),
            Child = _content,
        };
        _devices.Changed += Refresh;
        Refresh();
    }

    public Border Root { get; }

    public void Detach() => _devices.Changed -= Refresh;

    public void Refresh()
    {
        var device = DeviceRules.ChipDevice(_devices.Devices);
        Root.Visibility = device is not null && _shown() ? Visibility.Visible : Visibility.Collapsed;
        if (device is null) return;
        _content.Children.Clear();
        _content.Children.Add(DeviceIcon.Create(device.Kind, BatteryPart.Main, 12, Ui.White(0xFF)));
        if ((device.Lowest ?? device.Case) is { } level)
        {
            var text = Ui.Text(11, semibold: true);
            text.Text = BatteryColors.Percent(level);
            text.VerticalAlignment = VerticalAlignment.Center;
            if (level <= DeviceRules.LowLevel) text.Foreground = new SolidColorBrush(BatteryColors.Red);
            Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            _content.Children.Add(text);
        }
        ToolTipService.SetToolTip(Root, string.Join(Environment.NewLine, _devices.Devices.Select(BatteryColors.Summary)));
    }
}

/// <summary>The banner when a device connects, or when its battery runs low (DeviceViews.swift, DeviceAlertView).</summary>
internal sealed class DeviceBanner
{
    private readonly Grid _icon = new() { Width = 34, Height = 34 };
    private readonly TextBlock _name = Ui.Text(12, semibold: true);
    private readonly TextBlock _status = Ui.Text(11, alpha: 0x8C);
    private readonly ContentControl _levels = new();

    public DeviceBanner()
    {
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _name, _status } };
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { heading, _levels } };
        var row = new Grid
        {
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            Children = { _icon, text },
        };
        Grid.SetColumn(text, 1);
        Root = row;
    }

    public FrameworkElement Root { get; }

    public void Show(WirelessDevice device, bool isLow)
    {
        var brush = isLow ? new SolidColorBrush(BatteryColors.Red) : Ui.White(0xFF);
        _icon.Children.Clear();
        var icon = DeviceIcon.Create(device.Kind, BatteryPart.Main, 22, brush);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        _icon.Children.Add(icon);
        _name.Text = device.Name;
        _status.Text = L.Get(isLow ? "Devices_Low" : "Devices_Connected");
        _status.Foreground = isLow ? new SolidColorBrush(BatteryColors.Red) : Ui.White(0x8C);
        _levels.Content = BatteryBadge.Levels(device);
    }
}
