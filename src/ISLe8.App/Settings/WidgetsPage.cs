using ISLe8.Localization;
using ISLe8.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ISLe8.Settings;

/// <summary>
/// Settings › Widgets, after the Mac's (SettingsView.swift, WidgetSettings): calendar, weather and
/// the timer. The Mac's steppers are number boxes with spin buttons here.
/// </summary>
internal static class WidgetsPage
{
    public static FrameworkElement Build(SettingsContext context)
    {
        var settings = context.Settings;
        var page = new StackPanel { MaxWidth = 860 };
        page.Children.Add(SettingsUi.PageTitle(L.Get("Settings_Widgets")));

        AddCalendar(page, context);
        AddWeather(page, context);

        page.Children.Add(SettingsUi.SectionHeader(L.Get("Home_Timer")));
        page.Children.Add(SettingsUi.Card(context.Palette, L.Get("Settings_TimerSound"), SettingsUi.Switch(settings.TimerSound, on =>
        {
            settings.TimerSound = on;
            context.Save();
        })));
        page.Children.Add(Stepper(context, "Settings_PomodoroWork", settings.PomodoroWorkMinutes, 5, 90, 5, minutes => settings.PomodoroWorkMinutes = minutes));
        page.Children.Add(Stepper(context, "Settings_PomodoroBreak", settings.PomodoroBreakMinutes, 1, 30, 1, minutes => settings.PomodoroBreakMinutes = minutes));
        return page;
    }

    /// <summary>
    /// The Mac's "Erişim" is EventKit permission; here the calendars are ICS links, so the section
    /// lists them, takes a new one, and says whether any is connected.
    /// </summary>
    private static void AddCalendar(StackPanel page, SettingsContext context)
    {
        var settings = context.Settings;
        var calendar = context.Island.Calendar;
        page.Children.Add(SettingsUi.SectionHeader(L.Get("Home_Calendar")));

        var access = new TextBlock { Foreground = context.Palette.SecondaryText, VerticalAlignment = VerticalAlignment.Center };
        page.Children.Add(SettingsUi.Card(context.Palette, L.Get("Settings_Access"), access));

        var feeds = new StackPanel();
        page.Children.Add(feeds);
        void Changed()
        {
            context.Save();
            _ = calendar.Refresh();
            Fill();
        }
        void Fill()
        {
            access.Text = L.Get(settings.CalendarFeeds.Count > 0 ? "Settings_Connected" : "Settings_NotConnected");
            feeds.Children.Clear();
            foreach (var feed in settings.CalendarFeeds.ToList())
            {
                var remove = new Button { Content = new FontIcon { Glyph = Glyphs.Remove, FontSize = 12 }, Width = 32, Height = 32, Padding = new Thickness(0) };
                ToolTipService.SetToolTip(remove, L.Get("Settings_Remove"));
                remove.Click += (_, _) =>
                {
                    settings.CalendarFeeds.Remove(feed);
                    Changed();
                };
                var text = new TextBlock { Text = feed, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                feeds.Children.Add(SettingsUi.Card(context.Palette, text, remove));
            }
        }
        Fill();

        var link = new TextBox { PlaceholderText = "https://…/calendar.ics", MinWidth = 320 };
        var add = new Button { Content = L.Get("Settings_AddFeed") };
        void Add()
        {
            var feed = link.Text.Trim();
            settings.CalendarFeeds.Add(feed);
            settings.Normalize();
            link.Text = "";
            Changed();
        }
        add.Click += (_, _) => Add();
        link.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) Add();
        };
        page.Children.Add(SettingsUi.Card(
            context.Palette,
            L.Get("Settings_CalendarFeeds"),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { link, add } },
            L.Get("Settings_CalendarFeedsNote")));

        page.Children.Add(SettingsUi.Card(context.Palette, L.Get("Settings_MeetingHeadsUp"), SettingsUi.Switch(settings.MeetingHeadsUp, on =>
        {
            settings.MeetingHeadsUp = on;
            context.Save();
            calendar.Recompute();
        })));
    }

    /// <summary>The city (or the time zone's), a search for another, and the unit.</summary>
    private static void AddWeather(StackPanel page, SettingsContext context)
    {
        var settings = context.Settings;
        var weather = context.Island.Weather;
        page.Children.Add(SettingsUi.SectionHeader(L.Get("Home_Weather")));

        var city = new TextBlock { Foreground = context.Palette.SecondaryText, VerticalAlignment = VerticalAlignment.Center };
        void ShowCity() => city.Text = settings.WeatherCity.Length == 0
            ? L.Format("Settings_CityFromZone", weather.Report?.Place ?? weather.TimeZonePlace)
            : settings.WeatherCity;
        ShowCity();
        page.Children.Add(SettingsUi.Card(context.Palette, L.Get("Settings_City"), city));

        var query = new TextBox { PlaceholderText = L.Get("Settings_CitySearch"), MinWidth = 220 };
        var search = new Button { Content = L.Get("Settings_Search") };
        var back = new Button { Content = L.Get("Settings_UseTimeZone") };
        var results = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 4) };
        void ShowBack() => back.Visibility = settings.WeatherCity.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ShowBack();

        async void Search()
        {
            results.Children.Clear();
            foreach (var place in await weather.Search(query.Text))
            {
                var row = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children = { new TextBlock { Text = place.Name }, new TextBlock { Text = place.Detail, Foreground = context.Palette.SecondaryText } },
                    },
                };
                row.Click += async (_, _) =>
                {
                    results.Children.Clear();
                    query.Text = "";
                    var choosing = weather.Choose(place);
                    context.Save();
                    ShowCity();
                    ShowBack();
                    await choosing;
                };
                results.Children.Add(row);
            }
        }
        search.Click += (_, _) => Search();
        query.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) Search();
        };
        back.Click += async (_, _) =>
        {
            var going = weather.UseTimeZone();
            context.Save();
            ShowBack();
            await going;
            ShowCity();
        };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { query, search, back } };
        page.Children.Add(SettingsUi.Card(context.Palette, new TextBlock(), controls));
        page.Children.Add(results);

        page.Children.Add(SettingsUi.Card(context.Palette, L.Get("Settings_Unit"), SettingsUi.Choices(["°C", "°F"], (int)settings.WeatherUnit, index =>
        {
            settings.WeatherUnit = (Core.Weather.TemperatureUnit)index;
            context.Save();
            _ = weather.Refresh();
        })));
    }

    /// <summary>A card whose title says the value ("Pomodoro odak: 25 dk"), with a spinning number box.</summary>
    internal static Border Stepper(SettingsContext context, string key, int value, int minimum, int maximum, int step, Action<int> set)
    {
        var title = new TextBlock { Text = L.Format(key, value), TextWrapping = TextWrapping.Wrap };
        var box = new NumberBox
        {
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            SmallChange = step,
            LargeChange = step,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
            MinWidth = 120,
        };
        box.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) return;
            var stepped = Math.Clamp((int)Math.Round(e.NewValue / step) * step, minimum, maximum);
            if (stepped != (int)e.NewValue)
            {
                box.Value = stepped;
                return;
            }
            set(stepped);
            title.Text = L.Format(key, stepped);
            context.Save();
        };
        return SettingsUi.Card(context.Palette, title, box);
    }
}
