using System.Diagnostics;
using ISLe8.Core.Settings;
using ISLe8.Core.SystemInfo;
using ISLe8.Core.Theming;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace ISLe8.SystemTab;

/// <summary>
/// The System tab (SystemStatsViews.swift, SystemTab): CPU, memory, system drive and network as
/// gauges, and beside them the processes using the most CPU. Figures are taken only while this tab
/// is on screen, and change without animating, so an update is a single frame.
/// </summary>
internal sealed class SystemTabView
{
    private static readonly Color Green = Color.FromArgb(255, 0x30, 0xD1, 0x58);
    private static readonly Color Orange = Color.FromArgb(255, 0xFF, 0x9F, 0x0A);
    private static readonly Color Red = Color.FromArgb(255, 0xFF, 0x45, 0x3A);

    private readonly SystemSampler _sampler;
    private readonly AppSettings _settings;
    private readonly Gauge _cpu;
    private readonly Gauge _memory;
    private readonly Gauge _disk;
    private readonly FontIcon _networkIcon = new() { FontSize = 19 };
    private readonly Ellipse _networkBadge = new() { Width = 52, Height = 52 };
    private readonly TextBlock _networkName = Ui.Text(14, semibold: true);
    private readonly TextBlock _download = Small(0x80);
    private readonly TextBlock _upload = Small(0x80);
    private readonly Border _networkCard;
    private readonly StackPanel _processes = new() { Spacing = 6 };
    private readonly ColumnDefinition _listColumn = new() { Width = new GridLength(220) };

    public SystemTabView(SystemSampler sampler, AppSettings settings, IslandTheme theme)
    {
        _sampler = sampler;
        _settings = settings;
        _cpu = new Gauge(theme, Glyphs.Cpu, L.Get("System_Cpu"));
        _memory = new Gauge(theme, Glyphs.MemoryChip, L.Get("System_Memory"));
        _disk = new Gauge(theme, Glyphs.Drive, L.Get("System_Disk"));

        _networkName.HorizontalAlignment = HorizontalAlignment.Center;
        var network = new StackPanel
        {
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Grid { Width = 52, Height = 52, HorizontalAlignment = HorizontalAlignment.Center, Children = { _networkBadge, _networkIcon } },
                _networkName,
                new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center, Children = { Arrow(Glyphs.Download, _download), Arrow(Glyphs.Upload, _upload) } },
            },
        };
        _networkCard = Tiles.Tile(theme, network, new Thickness(6, 0, 6, 0));

        var gauges = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition(), new ColumnDefinition() } };
        FrameworkElement[] cards = [_cpu.Root, _memory.Root, _disk.Root, _networkCard];
        for (var i = 0; i < cards.Length; i++)
        {
            Grid.SetColumn(cards[i], i);
            gauges.Children.Add(cards[i]);
        }

        var title = Ui.Text(11, alpha: 0x8C, semibold: true);
        title.Text = L.Get("System_TopCpu");
        title.VerticalAlignment = VerticalAlignment.Center;
        var open = Ui.IconButton(Glyphs.OpenOut, 18, 10, L.Get("System_OpenTaskManager"), OpenTaskManager, out var openIcon);
        openIcon.Foreground = Ui.White(0x99);
        var heading = new Grid { ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        heading.Children.Add(title);
        Grid.SetColumn(open, 1);
        heading.Children.Add(open);
        var list = new StackPanel { Spacing = 6, Children = { heading, _processes } };
        var listCard = Tiles.Tile(theme, list, new Thickness(10));

        Root = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(), _listColumn } };
        Root.Children.Add(gauges);
        Grid.SetColumn(listCard, 1);
        Root.Children.Add(listCard);

        sampler.Changed += Show;
        Show();
    }

    public Grid Root { get; }

    /// <summary>Samples only while the tab is on screen.</summary>
    public void SetActive(bool active)
    {
        _listColumn.Width = new GridLength(_settings.HomeStyle == HomeStyle.Dashboard ? 220 : 230);
        _sampler.Run(active);
    }

    public void Detach()
    {
        _sampler.Changed -= Show;
        _sampler.Run(false);
    }

    private void Show()
    {
        var sample = _sampler.Sample;
        var culture = L.Culture;
        if (sample is null)
        {
            _cpu.Show(null, "—", null);
            _memory.Show(null, "—", null);
            _disk.Show(null, "—", null);
        }
        else
        {
            _cpu.Show(sample.Cpu, SystemMath.Percent(sample.Cpu, culture), L.Format("System_Cores", sample.Cores));
            var memory = (double)sample.MemoryUsed / Math.Max(sample.MemoryTotal, 1);
            _memory.Show(memory, SystemMath.Percent(memory, culture),
                $"{SystemMath.Bytes(sample.MemoryUsed, memory: true, culture)} / {SystemMath.Bytes(sample.MemoryTotal, memory: true, culture)}");
            double? disk = sample.DiskTotal > 0 ? 1 - (double)sample.DiskFree / sample.DiskTotal : null;
            _disk.Show(disk, disk is { } used ? SystemMath.Percent(used, culture) : "—",
                L.Format("System_Free", SystemMath.Bytes((ulong)Math.Max(sample.DiskFree, 0), memory: false, culture)));
        }

        var link = _sampler.Link;
        var tint = link switch { NetworkLink.Wired => Green, NetworkLink.Offline => Red, _ => Color.FromArgb(255, 255, 255, 255) };
        _networkIcon.Glyph = link switch
        {
            NetworkLink.Wired => Glyphs.Ethernet,
            NetworkLink.WiFi => Glyphs.Wifi,
            NetworkLink.Offline => Glyphs.WifiOff,
            _ => Glyphs.Network,
        };
        _networkIcon.Foreground = new SolidColorBrush(tint);
        _networkBadge.Fill = new SolidColorBrush(Color.FromArgb(0x2E, tint.R, tint.G, tint.B));
        _networkName.Text = link switch
        {
            NetworkLink.Wired => "LAN",
            NetworkLink.WiFi => "Wi-Fi",
            NetworkLink.Cellular => L.Get("System_Cellular"),
            NetworkLink.Offline => L.Get("System_Offline"),
            _ => L.Get("System_Network"),
        };
        ToolTipService.SetToolTip(_networkCard, link == NetworkLink.Wired ? L.Get("System_Wired") : null);
        _download.Text = Rate(sample?.Download ?? 0);
        _upload.Text = Rate(sample?.Upload ?? 0);

        _processes.Children.Clear();
        if (sample is not { Top.Count: > 0 })
        {
            var measuring = Ui.Text(11, alpha: 0x66);
            measuring.Text = L.Get("System_Measuring");
            _processes.Children.Add(measuring);
            return;
        }
        foreach (var process in sample.Top)
        {
            var name = Small(0xFF);
            name.Text = process.Name;
            var cpu = Small(0xD9);
            cpu.Text = L.Format("Weather_Percent", (int)Math.Round(process.Cpu, MidpointRounding.AwayFromZero));
            if (process.Cpu >= 50) cpu.Foreground = new SolidColorBrush(Orange);
            cpu.TextAlignment = TextAlignment.Right;
            var memory = Small(0x80);
            memory.Text = SystemMath.Bytes(process.Memory, memory: true, culture);
            memory.TextAlignment = TextAlignment.Right;
            var row = new Grid
            {
                ColumnSpacing = 6,
                ColumnDefinitions =
                {
                    new ColumnDefinition(),
                    new ColumnDefinition { Width = new GridLength(38) },
                    new ColumnDefinition { Width = new GridLength(58) },
                },
            };
            row.Children.Add(name);
            Grid.SetColumn(cpu, 1);
            row.Children.Add(cpu);
            Grid.SetColumn(memory, 2);
            row.Children.Add(memory);
            _processes.Children.Add(row);
        }
    }

    /// <summary>"1,2 MB/sn".</summary>
    private static string Rate(double bytesPerSecond) =>
        L.Format("System_Rate", SystemMath.Bytes((ulong)Math.Round(Math.Max(bytesPerSecond, 0)), memory: false, L.Culture));

    /// <summary>The Mac opens Activity Monitor; Task Manager is its Windows counterpart.</summary>
    private static void OpenTaskManager()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Core.Diagnostics.Log.Error(e, "task manager");
        }
    }

    private static TextBlock Small(byte alpha)
    {
        var text = Ui.Text(10.5, alpha: alpha);
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
        return text;
    }

    private static StackPanel Arrow(string glyph, TextBlock text)
    {
        text.FontSize = 10;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { new FontIcon { Glyph = glyph, FontSize = 9, Foreground = Ui.White(0x80) }, text },
        };
    }

    /// <summary>A ring that fills as use grows: green, orange from 60 %, red from 85 % (StatCard).</summary>
    private sealed class Gauge
    {
        private const double Size = 52;
        private const double Stroke = 4.5;
        private readonly Microsoft.UI.Xaml.Shapes.Path _arc = new() { StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        private readonly TextBlock _value = Ui.Text(14, semibold: true);
        private readonly TextBlock _detail = Small(0x80);

        public Gauge(IslandTheme theme, string glyph, string title)
        {
            var track = new Ellipse { Width = Size, Height = Size, Stroke = Ui.White(0x1A), StrokeThickness = Stroke };
            var icon = new FontIcon { Glyph = glyph, FontSize = 19, Foreground = Ui.White(0xE6) };
            var ring = new Grid { Width = Size, Height = Size, HorizontalAlignment = HorizontalAlignment.Center, Children = { track, _arc, icon } };
            Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(_value, FontNumeralAlignment.Tabular);
            _value.HorizontalAlignment = HorizontalAlignment.Center;
            var titleText = Ui.Text(11, semibold: true);
            titleText.Text = title;
            titleText.HorizontalAlignment = _detail.HorizontalAlignment = HorizontalAlignment.Center;
            _detail.FontSize = 10;
            var stack = new StackPanel
            {
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { ring, _value, new StackPanel { Spacing = 1, Children = { titleText, _detail } } },
            };
            Root = Tiles.Tile(theme, stack, new Thickness(6, 0, 6, 0));
        }

        public Border Root { get; }

        public void Show(double? fraction, string value, string? detail)
        {
            _value.Text = value;
            _detail.Text = detail ?? "";
            _detail.Visibility = detail is null ? Visibility.Collapsed : Visibility.Visible;
            var f = Math.Clamp(fraction ?? 0, 0, 1);
            var level = SystemMath.Level(f);
            _arc.Stroke = new SolidColorBrush(level switch { GaugeLevel.High => Red, GaugeLevel.Busy => Orange, _ => Green });
            _arc.Data = Arc(f);
        }

        /// <summary>The used share as an arc from the top, clockwise.</summary>
        private static Geometry? Arc(double fraction)
        {
            if (fraction <= 0.001) return null;
            var radius = (Size - Stroke) / 2;
            var center = Size / 2;
            if (fraction >= 0.999) return new EllipseGeometry { Center = new Point(center, center), RadiusX = radius, RadiusY = radius };
            var angle = fraction * 2 * Math.PI;
            var end = new Point(center + radius * Math.Sin(angle), center - radius * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = new Point(center, center - radius), IsClosed = false, IsFilled = false };
            figure.Segments.Add(new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                IsLargeArc = fraction > 0.5,
                SweepDirection = SweepDirection.Clockwise,
            });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }
    }
}
