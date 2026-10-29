using ISLe8.Core.Theming;
using ISLe8.Core.Usage;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace ISLe8.Usage;

/// <summary>The services' marks (simple-icons, CC0) and the words for windows and states (UsageModel.swift, UsageViews.swift).</summary>
internal static class UsageLook
{
    private static readonly Dictionary<UsageVendor, string> Marks = new()
    {
        [UsageVendor.Claude] = "m4.7144 15.9555 4.7174-2.6471.079-.2307-.079-.1275h-.2307l-.7893-.0486-2.6956-.0729-2.3375-.0971-2.2646-.1214-.5707-.1215-.5343-.7042.0546-.3522.4797-.3218.686.0608 1.5179.1032 2.2767.1578 1.6514.0972 2.4468.255h.3886l.0546-.1579-.1336-.0971-.1032-.0972L6.973 9.8356l-2.55-1.6879-1.3356-.9714-.7225-.4918-.3643-.4614-.1578-1.0078.6557-.7225.8803.0607.2246.0607.8925.686 1.9064 1.4754 2.4893 1.8336.3643.3035.1457-.1032.0182-.0728-.164-.2733-1.3539-2.4467-1.445-2.4893-.6435-1.032-.17-.6194c-.0607-.255-.1032-.4674-.1032-.7285L6.287.1335 6.6997 0l.9957.1336.419.3642.6192 1.4147 1.0018 2.2282 1.5543 3.0296.4553.8985.2429.8318.091.255h.1579v-.1457l.1275-1.706.2368-2.0947.2307-2.6957.0789-.7589.3764-.9107.7468-.4918.5828.2793.4797.686-.0668.4433-.2853 1.8517-.5586 2.9021-.3643 1.9429h.2125l.2429-.2429.9835-1.3053 1.6514-2.0643.7286-.8196.85-.9046.5464-.4311h1.0321l.759 1.1293-.34 1.1657-1.0625 1.3478-.8804 1.1414-1.2628 1.7-.7893 1.36.0729.1093.1882-.0183 2.8535-.607 1.5421-.2794 1.8396-.3157.8318.3886.091.3946-.3278.8075-1.967.4857-2.3072.4614-3.4364.8136-.0425.0304.0486.0607 1.5482.1457.6618.0364h1.621l3.0175.2247.7892.522.4736.6376-.079.4857-1.2142.6193-1.6393-.3886-3.825-.9107-1.3113-.3279h-.1822v.1093l1.0929 1.0686 2.0035 1.8092 2.5075 2.3314.1275.5768-.3218.4554-.34-.0486-2.2039-1.6575-.85-.7468-1.9246-1.621h-.1275v.17l.4432.6496 2.3436 3.5214.1214 1.0807-.17.3521-.6071.2125-.6679-.1214-1.3721-1.9246L14.38 17.959l-1.1414-1.9428-.1397.079-.674 7.2552-.3156.3703-.7286.2793-.6071-.4614-.3218-.7468.3218-1.4753.3886-1.9246.3157-1.53.2853-1.9004.17-.6314-.0121-.0425-.1397.0182-1.4328 1.9672-2.1796 2.9446-1.7243 1.8456-.4128.164-.7164-.3704.0667-.6618.4008-.5889 2.386-3.0357 1.4389-1.882.929-1.0868-.0062-.1579h-.0546l-6.3385 4.1164-1.1293.1457-.4857-.4554.0608-.7467.2307-.2429 1.9064-1.3114Z",
        [UsageVendor.Gemini] = "M11.04 19.32Q12 21.51 12 24q0-2.49.93-4.68.96-2.19 2.58-3.81t3.81-2.55Q21.51 12 24 12q-2.49 0-4.68-.93a12.3 12.3 0 0 1-3.81-2.58 12.3 12.3 0 0 1-2.58-3.81Q12 2.49 12 0q0 2.49-.96 4.68-.93 2.19-2.55 3.81a12.3 12.3 0 0 1-3.81 2.58Q2.49 12 0 12q2.49 0 4.68.96 2.19.93 3.81 2.55t2.55 3.81",
        [UsageVendor.Codex] = "M22.2819 9.8211a5.9847 5.9847 0 0 0-.5157-4.9108 6.0462 6.0462 0 0 0-6.5098-2.9A6.0651 6.0651 0 0 0 4.9807 4.1818a5.9847 5.9847 0 0 0-3.9977 2.9 6.0462 6.0462 0 0 0 .7427 7.0966 5.98 5.98 0 0 0 .511 4.9107 6.051 6.051 0 0 0 6.5146 2.9001A5.9847 5.9847 0 0 0 13.2599 24a6.0557 6.0557 0 0 0 5.7718-4.2058 5.9894 5.9894 0 0 0 3.9977-2.9001 6.0557 6.0557 0 0 0-.7475-7.0729zm-9.022 12.6081a4.4755 4.4755 0 0 1-2.8764-1.0408l.1419-.0804 4.7783-2.7582a.7948.7948 0 0 0 .3927-.6813v-6.7369l2.02 1.1686a.071.071 0 0 1 .038.052v5.5826a4.504 4.504 0 0 1-4.4945 4.4944zm-9.6607-4.1254a4.4708 4.4708 0 0 1-.5346-3.0137l.142.0852 4.783 2.7582a.7712.7712 0 0 0 .7806 0l5.8428-3.3685v2.3324a.0804.0804 0 0 1-.0332.0615L9.74 19.9502a4.4992 4.4992 0 0 1-6.1408-1.6464zM2.3408 7.8956a4.485 4.485 0 0 1 2.3655-1.9728V11.6a.7664.7664 0 0 0 .3879.6765l5.8144 3.3543-2.0201 1.1685a.0757.0757 0 0 1-.071 0l-4.8303-2.7865A4.504 4.504 0 0 1 2.3408 7.872zm16.5963 3.8558L13.1038 8.364 15.1192 7.2a.0757.0757 0 0 1 .071 0l4.8303 2.7913a4.4944 4.4944 0 0 1-.6765 8.1042v-5.6772a.79.79 0 0 0-.407-.667zm2.0107-3.0231l-.142-.0852-4.7735-2.7818a.7759.7759 0 0 0-.7854 0L9.409 9.2297V6.8974a.0662.0662 0 0 1 .0284-.0615l4.8303-2.7866a4.4992 4.4992 0 0 1 6.6802 4.66zM8.3065 12.863l-2.02-1.1638a.0804.0804 0 0 1-.038-.0567V6.0742a4.4992 4.4992 0 0 1 7.3757-3.4537l-.142.0805L8.704 5.459a.7948.7948 0 0 0-.3927.6813zm1.0976-2.3654l2.602-1.4998 2.6069 1.4998v2.9994l-2.5974 1.4997-2.6067-1.4997Z",
    };

    private static readonly Color Orange = Color.FromArgb(255, 0xFF, 0x9F, 0x0A);
    private static readonly Color Red = Color.FromArgb(255, 0xFF, 0x45, 0x3A);

    /// <summary>The mark in a 24-unit square, filled; null if its path can't be read.</summary>
    public static FrameworkElement? Mark(UsageVendor vendor, double size, Brush fill)
    {
        try
        {
            var geometry = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), Marks[vendor]);
            return new Viewbox { Width = size, Height = size, Child = new Grid { Width = 24, Height = 24, Children = { new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Fill = fill } } } };
        }
        catch (Exception e) when (e is ArgumentException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Orange from 70 %, red from 90 %, the accent below (UsageViews.swift, UsageRing).</summary>
    public static Brush Tint(double? used, IslandTheme theme) => (used ?? 0) switch
    {
        >= 90 => new SolidColorBrush(Red),
        >= 70 => new SolidColorBrush(Orange),
        _ => theme.Accent.ToBrush(),
    };

    public static Grid Ring(UsageVendor vendor, double? used, double size, IslandTheme theme)
    {
        const double stroke = 3;
        var ring = new Grid { Width = size, Height = size };
        ring.Children.Add(new Ellipse { Width = size, Height = size, Stroke = Ui.White(0x1A), StrokeThickness = stroke });
        ring.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Stroke = Tint(used, theme),
            StrokeThickness = stroke,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Arc(size, stroke, (used ?? 0) / 100),
        });
        if (Mark(vendor, size * 0.38, Ui.White(0xD9)) is { } mark)
        {
            mark.HorizontalAlignment = HorizontalAlignment.Center;
            mark.VerticalAlignment = VerticalAlignment.Center;
            ring.Children.Add(mark);
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

    public static string Label(UsageWindow window) => window.Kind switch
    {
        UsageWindowKind.FiveHour => L.Get("Usage_FiveHour"),
        UsageWindowKind.Week => L.Get("Usage_Weekly"),
        UsageWindowKind.ModelWeek => L.Format("Usage_ModelWeekly", window.Name),
        UsageWindowKind.Minutes => L.Format("Usage_Minutes", window.Minutes),
        UsageWindowKind.Quota => L.Get("Usage_Quota"),
        _ => window.Name,
    };

    /// <summary>"%14" in Turkish, "14%" in English.</summary>
    public static string Percent(double? value) => value is null ? "—" : L.Format("Devices_Percent", UsageWindow.PercentText(value));

    /// <summary>A window whose reset time has passed no longer counts.</summary>
    public static double? Current(UsageWindow? window) => window is null || window.ResetPassed(DateTimeOffset.Now) ? null : window.Used;

    /// <summary>"bugün 09:00", "yarın 09:00", else the day (UsageFetch.relativeDay).</summary>
    public static string Day(DateTimeOffset date)
    {
        var local = date.ToLocalTime();
        var time = local.ToString("HH:mm", L.Culture);
        var today = DateTime.Today;
        if (local.Date == today) return L.Format("Usage_Today", time);
        if (local.Date == today.AddDays(1)) return L.Format("Usage_Tomorrow", time);
        return local.ToString("ddd d MMM", L.Culture);
    }

    public static string LoginHint(UsageVendor vendor) => L.Get(vendor switch
    {
        UsageVendor.Codex => "Usage_LoginCodex",
        UsageVendor.Claude => "Usage_LoginClaude",
        _ => "Usage_LoginGemini",
    });

    /// <summary>Why there are no numbers, in our own words; never anything the CLI said.</summary>
    public static string Problem(UsageSnapshot snapshot) => (snapshot.Vendor, snapshot.Error) switch
    {
        (_, UsageError.Untrusted) => L.Format("Usage_Untrusted", UsageVendors.Cli(snapshot.Vendor)),
        (_, UsageError.Waiting) => L.Get("Usage_Waiting"),
        (_, UsageError.ServerFallback) => L.Get("Usage_ServerFallback"),
        (UsageVendor.Claude, UsageError.Timeout) => L.Get("Usage_ClaudeTimeout"),
        (UsageVendor.Claude, UsageError.NoData) => L.Format("Usage_ClaudeNoData", snapshot.ErrorCode),
        (UsageVendor.Claude, UsageError.NoQuotaLines) => L.Get("Usage_ClaudeNoLines"),
        (UsageVendor.Gemini, UsageError.Timeout) => L.Get("Usage_GeminiTimeout"),
        (UsageVendor.Gemini, UsageError.NoData) => L.Format("Usage_GeminiNoData", snapshot.ErrorCode),
        (UsageVendor.Gemini, UsageError.NoGemini) => L.Get("Usage_GeminiNoGemini"),
        (UsageVendor.Codex, _) => L.Get("Usage_CodexFailed"),
        _ => L.Get("Usage_Unreadable"),
    };
}

/// <summary>
/// The limits card (UsageViews.swift, UsageStrip): a ring per service for the window that runs out
/// first, its windows beside it. Side by side under the runs on the dashboard, or <c>stacked</c> in
/// equal rows in the shorter layouts' column.
/// </summary>
internal sealed class UsageStrip
{
    private readonly UsageService _usage;
    private readonly IslandTheme _theme;
    private readonly bool _stacked;
    private readonly Border _card;
    private readonly TextBlock _measured = Ui.Text(10, alpha: 0x66);
    private readonly Grid _refreshSlot = new() { Width = 18, Height = 18 };
    private readonly Panel _vendors;

    public UsageStrip(UsageService usage, IslandTheme theme, bool stacked)
    {
        _usage = usage;
        _theme = theme;
        _stacked = stacked;
        var title = Ui.Text(11, alpha: 0x8C, semibold: true);
        title.Text = L.Get("Usage_Limits");
        _measured.VerticalAlignment = title.VerticalAlignment = VerticalAlignment.Center;
        var header = new Grid
        {
            ColumnSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto } },
            Children = { title, _measured, _refreshSlot },
        };
        Grid.SetColumn(_measured, 1);
        Grid.SetColumn(_refreshSlot, 2);
        _vendors = stacked ? new StackPanel { Spacing = 6 } : new Grid { ColumnSpacing = 10 };
        var content = new StackPanel { Spacing = 8, Children = { header, _vendors } };
        _card = Tiles.Tile(theme, content, new Thickness(stacked ? 10 : 12));
        Root = _card;
    }

    public Border Root { get; }

    /// <summary>False when there is nothing to show (the feature off, or no CLI installed).</summary>
    public bool Build()
    {
        var snapshots = _usage.Snapshots;
        _card.Visibility = snapshots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (snapshots.Count == 0) return false;
        _measured.Text = snapshots.Select(snapshot => snapshot.MeasuredAt).Max() is { } measured ? ClipboardTab.ClipboardTabView.Ago(measured) : "";
        _refreshSlot.Children.Clear();
        if (_usage.IsRefreshing)
        {
            _refreshSlot.Children.Add(new ProgressRing { IsActive = true, Width = 14, Height = 14, MinWidth = 14, MinHeight = 14 });
        }
        else
        {
            var refresh = Ui.IconButton(Glyphs.Refresh, 18, 10, L.Get("Usage_Refresh"), _usage.Refresh, out var glyph);
            glyph.Foreground = Ui.White(0x99);
            _refreshSlot.Children.Add(refresh);
        }
        _vendors.Children.Clear();
        if (_vendors is Grid grid) grid.ColumnDefinitions.Clear();
        foreach (var (snapshot, index) in snapshots.Select((snapshot, index) => (snapshot, index)))
        {
            var row = Vendor(snapshot);
            if (_vendors is Grid columns)
            {
                columns.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(row, index);
            }
            _vendors.Children.Add(row);
        }
        return true;
    }

    /// <summary>One service: its ring, then its windows; stacked, the windows share a line and the reset goes in the tooltip.</summary>
    private Grid Vendor(UsageSnapshot snapshot)
    {
        var name = Ui.Text(12, semibold: true);
        name.Text = UsageVendors.DisplayName(snapshot.Vendor);
        var lines = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name } };
        string? reset = snapshot.Bar is { ResetsAt: { } at } bar && !bar.ResetPassed(DateTimeOffset.Now) ? L.Format("Usage_Resets", UsageLook.Day(at)) : null;
        TextBlock Line(string text, Brush brush, int maxLines = 1)
        {
            var line = Ui.Text(10);
            line.Text = text;
            line.Foreground = brush;
            if (maxLines > 1)
            {
                line.TextWrapping = TextWrapping.Wrap;
                line.MaxLines = maxLines;
            }
            return line;
        }
        Brush Shade(UsageWindow window) => window.IsHot(DateTimeOffset.Now) ? new SolidColorBrush(Color.FromArgb(255, 0xFF, 0x9F, 0x0A)) : Ui.White(0x99);
        switch (snapshot.State)
        {
            case UsageState.Ok when _stacked:
                var windows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                foreach (var window in snapshot.Windows.Take(2)) windows.Children.Add(Line($"{UsageLook.Label(window)} {UsageLook.Percent(UsageLook.Current(window))}", Shade(window)));
                if (reset is not null) ToolTipService.SetToolTip(windows, reset);
                lines.Children.Add(windows);
                break;
            case UsageState.Ok:
                foreach (var window in snapshot.Windows.Take(2)) lines.Children.Add(Line($"{UsageLook.Label(window)} {UsageLook.Percent(UsageLook.Current(window))}", Shade(window)));
                if (reset is not null) lines.Children.Add(Line(reset, Ui.White(0x66)));
                break;
            case UsageState.SignedOut:
                var signedOut = Line(L.Get("Usage_SignedOut"), new SolidColorBrush(Color.FromArgb(255, 0xFF, 0x9F, 0x0A)));
                ToolTipService.SetToolTip(signedOut, UsageLook.LoginHint(snapshot.Vendor));
                lines.Children.Add(signedOut);
                break;
            case UsageState.NoLimits:
                lines.Children.Add(Line(L.Get("Usage_NoLimits"), Ui.White(0x73)));
                break;
            default:
                lines.Children.Add(Line(UsageLook.Problem(snapshot), Ui.White(0x73), maxLines: 2));
                break;
        }
        var ring = UsageLook.Ring(snapshot.Vendor, UsageLook.Current(snapshot.Bar), _stacked ? 30 : 34, _theme);
        ring.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            Children = { ring, lines },
        };
        Grid.SetColumn(lines, 1);
        var product = UsageVendors.Product(snapshot.Vendor);
        ToolTipService.SetToolTip(row, snapshot.Plan.Length == 0 ? product : $"{product} · {snapshot.Plan}");
        return row;
    }
}

/// <summary>The banner when a limit crosses 90 % (UsageViews.swift, UsageAlertView).</summary>
internal sealed class UsageBanner(IslandTheme theme)
{
    public Grid Root { get; } = new();

    public void Show(UsageSnapshot snapshot, UsageWindow window)
    {
        var title = Ui.Text(12, semibold: true);
        title.Text = $"{UsageVendors.DisplayName(snapshot.Vendor)} · {UsageLook.Label(window)} {UsageLook.Percent(window.Used)}";
        var detail = Ui.Text(11, alpha: 0xA6);
        detail.Text = window.ResetsAt is { } reset ? L.Format("Usage_AlmostFullResets", UsageLook.Day(reset)) : L.Get("Usage_AlmostFull");
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title, detail } };
        var ring = UsageLook.Ring(snapshot.Vendor, window.Used, 30, theme);
        ring.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid
        {
            ColumnSpacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition() },
            Children = { ring, text },
        };
        Grid.SetColumn(text, 1);
        Root.Children.Clear();
        Root.Children.Add(row);
    }
}
