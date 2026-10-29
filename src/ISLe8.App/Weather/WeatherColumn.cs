using ISLe8.Core.Settings;
using ISLe8.Core.Theming;
using ISLe8.Core.Weather;
using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ISLe8.Weather;

/// <summary>
/// Home's weather column (WeatherViews.swift, WeatherColumn). Wide: now in detail (feels like,
/// humidity, wind, UV), the next five hours and the next five days. Compact: now, beside five days
/// at a time (an arrow shows the next five); a tap on a day puts its details in a line underneath.
/// Minimal: the place, now, and today's high and low. The Mac's multicolour symbols are Windows'
/// colour emoji here.
/// </summary>
internal sealed class WeatherColumn
{
    private readonly WeatherService _weather;
    private readonly AppSettings _settings;
    private readonly HomeStyle _style;
    private readonly Border _host = new();
    private int _forecastPage;
    private DateOnly? _pickedDay;

    public WeatherColumn(WeatherService weather, AppSettings settings, IslandTheme theme, HomeStyle style)
    {
        _weather = weather;
        _settings = settings;
        _style = style;
        if (style == HomeStyle.Dashboard)
        {
            _host.Padding = new Thickness(12);
            Root = Tiles.Tile(theme, _host, new Thickness(0));
        }
        else
        {
            Root = _host;
        }
        weather.Changed += Build;
        Build();
    }

    public FrameworkElement Root { get; }

    /// <summary>Home is shown: the report refreshes if it is older than 30 minutes.</summary>
    public void SetActive(bool active)
    {
        if (active) _ = _weather.RefreshIfStale();
    }

    public void Detach() => _weather.Changed -= Build;

    private void Build()
    {
        if (_weather.Report is { } report)
        {
            _host.Child = _style switch
            {
                HomeStyle.Compact => Compact(report),
                HomeStyle.Minimal => Minimal(report),
                _ => Dashboard(report),
            };
            return;
        }
        if (_weather.Failed)
        {
            var title = Ui.Text(12, semibold: true);
            title.Text = L.Get("Weather_Failed");
            title.HorizontalAlignment = HorizontalAlignment.Center;
            var retry = Capsule(L.Get("Weather_Retry"), Glyphs.Refresh, () => _ = _weather.Refresh());
            retry.HorizontalAlignment = HorizontalAlignment.Center;
            _host.Child = new StackPanel
            {
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new FontIcon { Glyph = Glyphs.Offline, FontSize = 20, Foreground = Ui.White(0x80) },
                    title,
                    retry,
                },
            };
            return;
        }
        _host.Child = new ProgressRing { IsActive = true, Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    // MARK: Layouts

    /// <summary>Minimal: the place over the icon and temperature, the sky and today's range beside.</summary>
    private FrameworkElement Minimal(WeatherReport report)
    {
        var range = Ui.Text(10, alpha: 0x99);
        range.Text = HighLow(report.High, report.Low);
        var sky = Ui.Text(10, alpha: 0x99);
        sky.Text = Description(report.Code);
        sky.HorizontalAlignment = range.HorizontalAlignment = HorizontalAlignment.Right;
        var side = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { sky, range } };

        var row = new Grid { ColumnSpacing = 6, ColumnDefinitions = { Auto(), Auto(), Star() } };
        row.Children.Add(Emoji(WeatherCode.SymbolFor(report.Code, report.IsDay), 20));
        row.Children.Add(At(Rounded(Degrees(report.Temperature), 24), 1));
        side.Margin = new Thickness(4, 0, 0, 0);
        row.Children.Add(At(side, 2));

        return new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Place(report, 12), row },
        };
    }

    /// <summary>Compact: now (temperature, place, sky), five days beside it, the picked day below.</summary>
    private FrameworkElement Compact(WeatherReport report)
    {
        var forecast = OpenMeteo.Forecast(report);
        var pages = Math.Max((forecast.Count + 4) / 5, 1);
        var page = Math.Min(_forecastPage, pages - 1);
        var picked = forecast.FirstOrDefault(day => day.Date == _pickedDay) ?? forecast.FirstOrDefault();

        var now = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        now.Children.Add(Emoji(WeatherCode.SymbolFor(report.Code, report.IsDay), 24));
        now.Children.Add(Rounded(Degrees(report.Temperature), 30));
        var sky = Ui.Text(11, alpha: 0x8C);
        sky.Text = Description(report.Code);
        var left = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { now, Place(report, 12), sky } };

        var days = new Grid();
        var shown = forecast.Skip(page * 5).Take(5).ToList();
        foreach (var day in shown)
        {
            days.ColumnDefinitions.Add(new ColumnDefinition());
            var cell = DayCell(day, day.Date == picked?.Date);
            Grid.SetColumn(cell, days.ColumnDefinitions.Count - 1);
            days.Children.Add(cell);
        }
        if (pages > 1)
        {
            var more = page + 1 < pages;
            days.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var arrow = Ui.IconButton(more ? Glyphs.Forward : Glyphs.Back, 16, 10, L.Get(more ? "Weather_NextDays" : "Weather_FirstDays"), () =>
            {
                _forecastPage = more ? page + 1 : 0;
                Build();
            }, out var icon);
            icon.Foreground = Ui.White(0x99);
            arrow.Height = 44;
            arrow.CornerRadius = new CornerRadius(4);
            Grid.SetColumn(arrow, days.ColumnDefinitions.Count - 1);
            days.Children.Add(arrow);
        }

        var top = new Grid { ColumnSpacing = 12, ColumnDefinitions = { Auto(), Star() } };
        top.Children.Add(left);
        top.Children.Add(At(days, 1));

        var stack = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { top } };
        if (picked is not null)
        {
            var detail = Ui.Text(10, alpha: 0x99);
            detail.Text = Detail(picked);
            stack.Children.Add(detail);
        }
        return stack;
    }

    private Border DayCell(ForecastDay day, bool picked)
    {
        var label = Ui.Text(10, alpha: 0x8C);
        label.Text = DayLabel(day, wide: false);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        var high = Ui.Text(11, semibold: true);
        high.Text = Degrees(day.High);
        high.HorizontalAlignment = HorizontalAlignment.Center;
        var symbol = Emoji(WeatherCode.SymbolFor(day.Code, isDay: true), 15);
        symbol.HorizontalAlignment = HorizontalAlignment.Center;
        symbol.Height = 18;
        var cell = new Border
        {
            Padding = new Thickness(0, 3, 0, 3),
            CornerRadius = new CornerRadius(7),
            Background = picked ? Ui.White(0x1A) : Ui.Clear(),
            Child = new StackPanel { Spacing = 4, Children = { label, symbol, high } },
        };
        cell.Tapped += (_, _) =>
        {
            _pickedDay = day.Date;
            Build();
        };
        return cell;
    }

    /// <summary>The wide layout: place and sky, now, the details, five hours, five days.</summary>
    private FrameworkElement Dashboard(WeatherReport report)
    {
        var sky = Ui.Text(11, alpha: 0x99);
        sky.Text = Description(report.Code);
        var heading = new StackPanel { Spacing = 1, Children = { Place(report, 14), sky } };

        var feels = Ui.Text(10, alpha: 0x99);
        feels.Text = L.Format("Weather_FeelsLike", Degrees(report.FeelsLike));
        var range = Ui.Text(10, alpha: 0x99);
        range.Text = HighLow(report.High, report.Low);
        feels.HorizontalAlignment = range.HorizontalAlignment = HorizontalAlignment.Right;
        var now = new Grid { ColumnSpacing = 8, ColumnDefinitions = { Auto(), Auto(), Star() } };
        var symbol = Emoji(WeatherCode.SymbolFor(report.Code, report.IsDay), 28);
        symbol.VerticalAlignment = VerticalAlignment.Center;
        now.Children.Add(symbol);
        var temperature = Rounded(Degrees(report.Temperature), 36);
        temperature.VerticalAlignment = VerticalAlignment.Center;
        now.Children.Add(At(temperature, 1));
        now.Children.Add(At(new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { feels, range } }, 2));

        var details = new Grid
        {
            Padding = new Thickness(0, 5, 0, 5),
            ColumnDefinitions = { Star(), Star(), Star(), Star() },
        };
        var unit = _settings.WeatherUnit == TemperatureUnit.Fahrenheit ? "Weather_Mph" : "Weather_Kmh";
        (string Symbol, string Value, string Caption)[] items =
        [
            (Glyph.Humidity, L.Format("Weather_Percent", report.Humidity), L.Get("Weather_Humidity")),
            (Glyph.Wind, L.Format(unit, (int)Math.Round(report.WindSpeed, MidpointRounding.AwayFromZero)), L.Get("Weather_Wind")),
            (Glyph.Direction, L.Get("Wind_" + WindDirection.Point(report.WindDirection)), L.Get("Weather_Direction")),
            (Glyph.Uv, ((int)Math.Round(report.UvIndex, MidpointRounding.AwayFromZero)).ToString(L.Culture), L.Get("Weather_UV")),
        ];
        for (var i = 0; i < items.Length; i++)
        {
            details.Children.Add(At(DetailItem(items[i].Symbol, items[i].Value, items[i].Caption), i));
        }
        var detailBox = new Border { CornerRadius = new CornerRadius(9), Background = Ui.White(0x0D), Child = details };

        var hours = new Grid();
        var hourList = new List<(string Label, int Code, bool IsDay, double Temperature)> { (L.Get("Weather_Now"), report.Code, report.IsDay, report.Temperature) };
        hourList.AddRange(report.Hours.Take(5).Select(hour => (hour.Label, hour.Code, hour.IsDay, hour.Temperature)));
        foreach (var hour in hourList)
        {
            hours.ColumnDefinitions.Add(new ColumnDefinition());
            var label = Ui.Text(9, alpha: 0x8C);
            label.Text = hour.Label;
            var hourSymbol = Emoji(WeatherCode.SymbolFor(hour.Code, hour.IsDay), 13);
            hourSymbol.Height = 15;
            var degrees = Ui.Text(11, semibold: true);
            degrees.Text = Degrees(hour.Temperature);
            foreach (var part in new FrameworkElement[] { label, hourSymbol, degrees }) part.HorizontalAlignment = HorizontalAlignment.Center;
            hours.Children.Add(At(new StackPanel { Spacing = 3, Children = { label, hourSymbol, degrees } }, hours.ColumnDefinitions.Count - 1));
        }

        var days = new StackPanel { Spacing = 3 };
        var forecast = OpenMeteo.Forecast(report).Skip(1).Take(5);
        foreach (var day in forecast)
        {
            var row = new Grid
            {
                ColumnSpacing = 6,
                ColumnDefinitions =
                {
                    Star(),
                    new ColumnDefinition { Width = new GridLength(18) },
                    new ColumnDefinition { Width = new GridLength(28) },
                    new ColumnDefinition { Width = new GridLength(28) },
                    new ColumnDefinition { Width = new GridLength(38) },
                },
            };
            var name = Small(DayLabel(day, wide: true), 0xFF);
            var daySymbol = Emoji(WeatherCode.SymbolFor(day.Code, isDay: true), 10.5);
            daySymbol.HorizontalAlignment = HorizontalAlignment.Center;
            var high = Small(Degrees(day.High), 0xFF);
            var low = Small(Degrees(day.Low), 0x80);
            high.HorizontalAlignment = low.HorizontalAlignment = HorizontalAlignment.Right;
            var rain = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
            var drop = Emoji(WeatherCode.Symbol.Rain, 7, Glyph.Drop);
            drop.VerticalAlignment = VerticalAlignment.Center;
            rain.Children.Add(drop);
            rain.Children.Add(Small(day.RainChance is { } chance ? L.Format("Weather_Percent", chance) : "—", 0x99));
            row.Children.Add(name);
            row.Children.Add(At(daySymbol, 1));
            row.Children.Add(At(high, 2));
            row.Children.Add(At(low, 3));
            row.Children.Add(At(rain, 4));
            days.Children.Add(row);
        }

        return new StackPanel { Spacing = 7, Children = { heading, now, detailBox, hours, days } };
    }

    // MARK: Pieces

    private static FrameworkElement DetailItem(string symbol, string value, string caption)
    {
        var valueText = Ui.Text(10, semibold: true);
        valueText.Text = value;
        var captionText = Ui.Text(8, alpha: 0x73);
        captionText.Text = caption;
        var icon = Emoji(WeatherCode.Symbol.Cloud, 10, symbol);
        icon.Opacity = 0.6;
        icon.VerticalAlignment = VerticalAlignment.Center;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { icon, new StackPanel { Children = { valueText, captionText } } },
        };
    }

    private static StackPanel Place(WeatherReport report, double size)
    {
        var name = Ui.Text(size, semibold: true);
        name.Text = report.Place;
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children =
            {
                name,
                new FontIcon { Glyph = Glyphs.Location, FontSize = size * 0.6, Foreground = Ui.White(0x80), VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }

    /// <summary>"Cmt 31 Eki · Sağanak · Y 18°  D 12° · Yağış %70".</summary>
    private static string Detail(ForecastDay day)
    {
        var name = day.Label == ForecastLabel.Weekday
            ? $"{Format.AbbreviatedDayNames[(int)day.Date.DayOfWeek]} {day.Date.Day} {Format.AbbreviatedMonthNames[day.Date.Month - 1]}"
            : DayLabel(day, wide: false);
        var parts = new List<string> { name, Description(day.Code), HighLow(day.High, day.Low) };
        if (day.RainChance is { } rain) parts.Add(L.Format("Weather_RainChance", rain));
        return string.Join(" · ", parts);
    }

    private static string DayLabel(ForecastDay day, bool wide) => day.Label switch
    {
        ForecastLabel.Today => L.Get("Weather_Today"),
        ForecastLabel.Tomorrow => L.Get("Weather_Tomorrow"),
        _ => wide ? Format.DayNames[(int)day.Date.DayOfWeek] : Format.AbbreviatedDayNames[(int)day.Date.DayOfWeek],
    };

    private static System.Globalization.DateTimeFormatInfo Format => L.Culture.DateTimeFormat;

    private static string Description(int code) => WeatherCode.DescriptionKey(code) is { } key ? L.Get(key) : "—";

    private static string HighLow(double high, double low) => L.Format("Weather_HighLow", Degrees(high), Degrees(low));

    public static string Degrees(double value) => $"{(int)Math.Round(value, MidpointRounding.AwayFromZero)}°";

    private static TextBlock Rounded(string text, double size)
    {
        var block = Ui.Text(size, semibold: true);
        // The Mac's rounded face; Windows has none, its display cut is the softest.
        block.FontFamily = Ui.Font ?? new FontFamily("Segoe UI Variable Display");
        block.TextTrimming = TextTrimming.None;
        block.Text = text;
        return block;
    }

    private static TextBlock Small(string text, byte alpha)
    {
        var block = Ui.Text(10.5, alpha: alpha);
        Microsoft.UI.Xaml.Documents.Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        block.Text = text;
        return block;
    }

    /// <summary>A weather symbol, or <paramref name="text"/> when given, in colour emoji.</summary>
    private static TextBlock Emoji(WeatherCode.Symbol symbol, double size, string? text = null) => new()
    {
        Text = text ?? Glyph.For(symbol),
        FontSize = size,
        FontFamily = new FontFamily("Segoe UI Emoji"),
        IsColorFontEnabled = true,
        TextLineBounds = TextLineBounds.Tight,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Button Capsule(string title, string glyph, Action click)
    {
        var text = Ui.Text(11, semibold: true);
        text.Text = title;
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new FontIcon { Glyph = glyph, FontSize = 11 }, text } },
            Height = 24,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(0),
            Background = Ui.White(0x1A),
        };
        button.Resources["ButtonBackgroundPointerOver"] = Ui.White(0x2E);
        button.Resources["ButtonBackgroundPressed"] = Ui.White(0x24);
        button.Click += (_, _) => click();
        return button;
    }

    private static ColumnDefinition Auto() => new() { Width = GridLength.Auto };
    private static ColumnDefinition Star() => new() { Width = new GridLength(1, GridUnitType.Star) };

    private static FrameworkElement At(FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        return element;
    }

    /// <summary>The Mac's SF Symbols as colour emoji.</summary>
    private static class Glyph
    {
        public const string Humidity = "\U0001F4A7";
        public const string Drop = "\U0001F4A7";
        public const string Wind = "\U0001F4A8";
        public const string Direction = "\U0001F9ED";
        public const string Uv = "☀️";

        public static string For(WeatherCode.Symbol symbol) => symbol switch
        {
            WeatherCode.Symbol.Sun => "☀️",
            WeatherCode.Symbol.Moon or WeatherCode.Symbol.MoonStars => "\U0001F319",
            WeatherCode.Symbol.CloudSun => "⛅",
            WeatherCode.Symbol.Fog => "\U0001F32B️",
            WeatherCode.Symbol.Drizzle => "\U0001F326️",
            WeatherCode.Symbol.Sleet => "\U0001F328️",
            WeatherCode.Symbol.Rain or WeatherCode.Symbol.HeavyRain => "\U0001F327️",
            WeatherCode.Symbol.Snow => "❄️",
            WeatherCode.Symbol.Storm => "⛈️",
            _ => "☁️",
        };
    }
}
