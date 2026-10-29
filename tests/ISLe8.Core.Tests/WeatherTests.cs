using System.Globalization;
using System.Net;
using ISLe8.Core.Settings;
using ISLe8.Core.Weather;

namespace ISLe8.Core.Tests;

/// <summary>Weather from Open-Meteo (PORTING.md §3.5).</summary>
public class WeatherTests
{
    private const string Forecast = """
        {
          "current": { "temperature_2m": 17.6, "apparent_temperature": 16.2, "relative_humidity_2m": 71.4,
                       "wind_speed_10m": 12.3, "wind_direction_10m": 44, "uv_index": 2.6, "weather_code": 2, "is_day": 1 },
          "hourly": { "time": ["2026-10-29T14:00", "2026-10-29T15:00", "2026-10-29T16:00"],
                      "temperature_2m": [17.6, 18.1, 17.0], "weather_code": [2, 3, 61], "is_day": [1, 1, 1] },
          "daily": { "time": ["2026-10-29", "2026-10-30", "2026-10-31"], "weather_code": [2, 80, 0],
                     "temperature_2m_max": [19.2, 18.0, 21.4], "temperature_2m_min": [12.1, 11.5, 13.0],
                     "precipitation_probability_max": [10, 70, null] }
        }
        """;

    private const string Istanbul = """{ "results": [ { "name": "İstanbul", "latitude": 41.01384, "longitude": 28.94966, "country": "Türkiye", "admin1": "İstanbul" } ] }""";

    private static readonly WeatherPlace Place = new("İstanbul", "", 41.01384, 28.94966);
    private static readonly DateTimeOffset Noon = new(2026, 10, 29, 14, 5, 0, TimeSpan.FromHours(3));

    [Fact]
    public void AReportReadsNowTheHoursAfterItAndTheDaysAfterToday()
    {
        var report = OpenMeteo.ParseForecast(Forecast, Place, Noon);

        Assert.Equal((17.6, 2, true, 71, 16.2), (report.Temperature, report.Code, report.IsDay, report.Humidity, report.FeelsLike));
        Assert.Equal((19.2, 12.1, 2, 10), (report.High, report.Low, report.TodayCode, report.TodayRainChance!.Value));
        Assert.Equal(["15:00", "16:00"], report.Hours.Select(hour => hour.Label));
        Assert.Equal([new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 31)], report.Days.Select(day => day.Date));
        Assert.Equal(70, report.Days[0].RainChance);
        Assert.Null(report.Days[1].RainChance);
        Assert.Equal("İstanbul", report.Place);
    }

    [Fact]
    public void TheCompactForecastStartsWithTodayAndTomorrow()
    {
        var forecast = OpenMeteo.Forecast(OpenMeteo.ParseForecast(Forecast, Place, Noon));

        Assert.Equal([ForecastLabel.Today, ForecastLabel.Tomorrow, ForecastLabel.Weekday], forecast.Select(day => day.Label));
        Assert.Equal(new DateOnly(2026, 10, 29), forecast[0].Date);
    }

    [Fact]
    public void TheRequestUsesTwoDecimalsWithAPointEvenInTurkish()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        try
        {
            var url = OpenMeteo.ForecastUrl(Place, TemperatureUnit.Celsius).ToString();
            Assert.Contains("latitude=41.01&longitude=28.95", url);
            Assert.Contains("forecast_days=10", url);
            Assert.Contains("wind_speed_unit=kmh", url);
            Assert.Contains("temperature_unit=fahrenheit", OpenMeteo.ForecastUrl(Place, TemperatureUnit.Fahrenheit).ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("Turkey Standard Time", "Istanbul")]
    [InlineData("Pacific Standard Time", "Los Angeles")]
    [InlineData("Europe/Berlin", "Berlin")]
    public void WithoutACityTheTimeZoneGivesOne(string zone, string city)
    {
        Assert.Equal(city, TimeZoneCity.From(zone));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(44, 1)]
    [InlineData(200, 4)]
    [InlineData(350, 0)]
    [InlineData(-90, 6)]
    public void TheWindComesFromOneOfEightPoints(double degrees, int point)
    {
        Assert.Equal(point, WindDirection.Point(degrees));
    }

    [Theory]
    [InlineData(0, true, WeatherCode.Symbol.Sun, "Weather_Clear")]
    [InlineData(0, false, WeatherCode.Symbol.MoonStars, "Weather_Clear")]
    [InlineData(3, true, WeatherCode.Symbol.Cloud, "Weather_Overcast")]
    [InlineData(81, true, WeatherCode.Symbol.HeavyRain, "Weather_Showers")]
    [InlineData(99, true, WeatherCode.Symbol.Storm, "Weather_Hail")]
    public void CodesReadAsOnTheMac(int code, bool isDay, WeatherCode.Symbol symbol, string key)
    {
        Assert.Equal(symbol, WeatherCode.SymbolFor(code, isDay));
        Assert.Equal(key, WeatherCode.DescriptionKey(code));
    }

    [Fact]
    public async Task AFreshReportIsKeptForHalfAnHourThenFetchedAgain()
    {
        var handler = new Replies(Istanbul, Forecast);
        var now = Noon;
        var service = new WeatherService(new AppSettings(), new HttpClient(handler), () => now, "Turkey Standard Time");

        await service.RefreshIfStale();
        Assert.NotNull(service.Report);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("name=Istanbul", handler.Requests[0]);

        now += TimeSpan.FromMinutes(29);
        await service.RefreshIfStale();
        Assert.Equal(2, handler.Requests.Count);

        now += TimeSpan.FromMinutes(2);
        await service.RefreshIfStale();
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task AChosenCityNeedsNoLookupAndAFailureSaysSo()
    {
        var settings = new AppSettings { WeatherCity = "Ankara", WeatherLatitude = 39.92, WeatherLongitude = 32.85 };
        var handler = new Replies(Forecast);
        var service = new WeatherService(settings, new HttpClient(handler), () => Noon, "Turkey Standard Time");

        await service.Refresh();
        Assert.Single(handler.Requests);
        Assert.Contains("latitude=39.92", handler.Requests[0]);
        Assert.Equal("Ankara", service.Report!.Place);

        handler.Fail = true;
        await service.Refresh();
        Assert.True(service.Failed);
        Assert.False(service.IsLoading);
    }

    /// <summary>Answers with the given bodies in turn, the last one again after that.</summary>
    private sealed class Replies(params string[] bodies) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            var body = bodies[Math.Min(Requests.Count - 1, bodies.Length - 1) % bodies.Length];
            // A lookup and a forecast alternate when the time zone gives the place.
            if (bodies.Length == 2) body = request.RequestUri!.Host.StartsWith("geocoding") ? bodies[0] : bodies[1];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
