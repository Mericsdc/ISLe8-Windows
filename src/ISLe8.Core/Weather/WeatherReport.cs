using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISLe8.Core.Weather;

/// <summary>A place weather can be shown for.</summary>
public sealed record WeatherPlace(string Name, string Detail, double Latitude, double Longitude);

[JsonConverter(typeof(Theming.LenientEnumConverter<TemperatureUnit>))]
public enum TemperatureUnit
{
    Celsius,
    Fahrenheit,
}

/// <summary>Now, the next five hours and the next nine days (WeatherModule.swift, WeatherReport).</summary>
public sealed record WeatherReport
{
    public sealed record Hour(string Label, double Temperature, int Code, bool IsDay);

    public sealed record Day(DateOnly Date, int Code, double High, double Low, int? RainChance);

    public required double Temperature { get; init; }
    public required int Code { get; init; }
    public required bool IsDay { get; init; }
    public required double High { get; init; }
    public required double Low { get; init; }

    /// <summary>Today's weather as a whole, for the forecast row; <see cref="Code"/> is the sky right now.</summary>
    public required int TodayCode { get; init; }
    public int? TodayRainChance { get; init; }
    public required DateOnly Today { get; init; }
    public required string Place { get; init; }
    public required DateTimeOffset FetchedAt { get; init; }
    public required double FeelsLike { get; init; }
    public required int Humidity { get; init; }

    /// <summary>km/h, or mph with Fahrenheit.</summary>
    public required double WindSpeed { get; init; }

    /// <summary>Where the wind comes from, in degrees.</summary>
    public required double WindDirection { get; init; }
    public required double UvIndex { get; init; }
    public required IReadOnlyList<Hour> Hours { get; init; }
    public required IReadOnlyList<Day> Days { get; init; }
}

/// <summary>One day of the compact strip's forecast: today, tomorrow, then the days after.</summary>
public sealed record ForecastDay(DateOnly Date, ForecastLabel Label, int Code, double High, double Low, int? RainChance);

public enum ForecastLabel
{
    Today,
    Tomorrow,
    Weekday,
}

/// <summary>Open-Meteo: free, no key, no account (WeatherModule.swift).</summary>
public static class OpenMeteo
{
    public static Uri ForecastUrl(WeatherPlace place, TemperatureUnit unit)
    {
        var fahrenheit = unit == TemperatureUnit.Fahrenheit;
        // Two decimals (about a kilometre) is plenty for weather; invariant, or Turkish writes a comma.
        var query = string.Join('&',
            $"latitude={place.Latitude.ToString("F2", CultureInfo.InvariantCulture)}",
            $"longitude={place.Longitude.ToString("F2", CultureInfo.InvariantCulture)}",
            "current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,wind_direction_10m,uv_index,weather_code,is_day",
            "hourly=temperature_2m,weather_code,is_day",
            "forecast_hours=6",
            "daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max",
            "forecast_days=10",
            $"wind_speed_unit={(fahrenheit ? "mph" : "kmh")}",
            "timezone=auto",
            $"temperature_unit={(fahrenheit ? "fahrenheit" : "celsius")}");
        return new Uri("https://api.open-meteo.com/v1/forecast?" + query);
    }

    public static Uri GeocodeUrl(string name, int count) =>
        new($"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name)}&count={count}&language=tr");

    public static WeatherReport ParseForecast(string json, WeatherPlace place, DateTimeOffset fetchedAt)
    {
        var response = JsonSerializer.Deserialize(json, OpenMeteoJson.Default.ForecastResponse)
            ?? throw new JsonException("empty forecast");
        var current = response.Current ?? throw new JsonException("no current weather");
        var daily = response.Daily ?? throw new JsonException("no daily forecast");

        // The hourly list starts at the current hour; the island shows the five after it.
        var hours = new List<WeatherReport.Hour>();
        if (response.Hourly is { } hourly)
        {
            for (var i = 1; i < hourly.Time.Length && i < hourly.Temperature.Length && i < hourly.Code.Length && i < hourly.IsDay.Length; i++)
            {
                var time = hourly.Time[i];
                hours.Add(new(time.Length >= 5 ? time[^5..] : time, hourly.Temperature[i], hourly.Code[i], hourly.IsDay[i] == 1));
            }
        }

        var days = new List<WeatherReport.Day>();
        for (var i = 1; i < daily.Time.Length && i < daily.High.Length && i < daily.Low.Length && i < daily.Code.Length; i++)
        {
            if (!DateOnly.TryParseExact(daily.Time[i], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            days.Add(new(date, daily.Code[i], daily.High[i], daily.Low[i], Rain(daily, i)));
        }

        DateOnly.TryParseExact(daily.Time.FirstOrDefault() ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var today);
        return new WeatherReport
        {
            Temperature = current.Temperature,
            Code = current.Code,
            IsDay = current.IsDay == 1,
            High = daily.High.FirstOrDefault(current.Temperature),
            Low = daily.Low.FirstOrDefault(current.Temperature),
            TodayCode = daily.Code.FirstOrDefault(current.Code),
            TodayRainChance = Rain(daily, 0),
            Today = today == default ? DateOnly.FromDateTime(fetchedAt.LocalDateTime) : today,
            Place = place.Name,
            FetchedAt = fetchedAt,
            FeelsLike = current.FeelsLike ?? current.Temperature,
            Humidity = (int)Math.Round(current.Humidity ?? 0),
            WindSpeed = current.WindSpeed ?? 0,
            WindDirection = current.WindDirection ?? 0,
            UvIndex = current.UvIndex ?? 0,
            Hours = hours,
            Days = days,
        };
    }

    public static IReadOnlyList<WeatherPlace> ParseGeocode(string json)
    {
        var response = JsonSerializer.Deserialize(json, OpenMeteoJson.Default.GeocodingResponse);
        return (response?.Results ?? [])
            .Select(r => new WeatherPlace(r.Name, string.Join(", ", new[] { r.Admin1, r.Country }.Where(part => !string.IsNullOrEmpty(part))), r.Latitude, r.Longitude))
            .ToList();
    }

    /// <summary>Today, tomorrow, then the days after: ten in all, five to a page.</summary>
    public static IReadOnlyList<ForecastDay> Forecast(WeatherReport report)
    {
        var days = new List<ForecastDay> { new(report.Today, ForecastLabel.Today, report.TodayCode, report.High, report.Low, report.TodayRainChance) };
        days.AddRange(report.Days.Take(9).Select((day, index) =>
            new ForecastDay(day.Date, index == 0 ? ForecastLabel.Tomorrow : ForecastLabel.Weekday, day.Code, day.High, day.Low, day.RainChance)));
        return days;
    }

    private static int? Rain(ForecastResponse.DailyPart daily, int index) =>
        daily.Rain is { } rain && index < rain.Length ? rain[index] : null;
}

/// <summary>The city in a Windows or IANA time zone: "Turkey Standard Time" → "Europe/Istanbul" → "Istanbul".</summary>
public static class TimeZoneCity
{
    public static string From(string timeZoneId)
    {
        var iana = timeZoneId.Contains('/') ? timeZoneId
            : TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var converted) ? converted
            : "Europe/Istanbul";
        var city = iana.Split('/')[^1];
        return city.Replace('_', ' ');
    }
}

/// <summary>The eight compass points, by index: K, KD, D, GD, G, GB, B, KB (N, NE, E, … in English).</summary>
public static class WindDirection
{
    public static int Point(double degrees) => (int)Math.Floor((((degrees % 360) + 360) % 360 + 22.5) / 45) % 8;
}

/// <summary>WMO weather codes, as Open-Meteo reports them (WeatherModule.swift, WeatherCode).</summary>
public static class WeatherCode
{
    public enum Symbol
    {
        Sun,
        Moon,
        MoonStars,
        CloudSun,
        CloudMoon,
        Cloud,
        Fog,
        Drizzle,
        Sleet,
        Rain,
        HeavyRain,
        Snow,
        Storm,
    }

    public static Symbol SymbolFor(int code, bool isDay) => code switch
    {
        0 => isDay ? Symbol.Sun : Symbol.MoonStars,
        1 => isDay ? Symbol.Sun : Symbol.Moon,
        2 => isDay ? Symbol.CloudSun : Symbol.CloudMoon,
        3 => Symbol.Cloud,
        45 or 48 => Symbol.Fog,
        51 or 53 or 55 => Symbol.Drizzle,
        56 or 57 or 66 or 67 => Symbol.Sleet,
        61 or 63 => Symbol.Rain,
        65 or 80 or 81 or 82 => Symbol.HeavyRain,
        71 or 73 or 75 or 77 or 85 or 86 => Symbol.Snow,
        95 or 96 or 99 => Symbol.Storm,
        _ => Symbol.Cloud,
    };

    /// <summary>The Mac's description, as a string key in the app; null for an unknown code ("—").</summary>
    public static string? DescriptionKey(int code) => code switch
    {
        0 => "Weather_Clear",
        1 => "Weather_MostlyClear",
        2 => "Weather_PartlyCloudy",
        // "Çok bulutlu", not "Kapalı": "Kapalı" means "off" in the settings (PORTING.md §3.5).
        3 => "Weather_Overcast",
        45 or 48 => "Weather_Fog",
        51 or 53 or 55 => "Weather_Drizzle",
        56 or 57 => "Weather_FreezingDrizzle",
        61 or 63 => "Weather_Rain",
        65 => "Weather_HeavyRain",
        66 or 67 => "Weather_FreezingRain",
        71 or 73 or 75 or 77 => "Weather_Snow",
        80 or 81 or 82 => "Weather_Showers",
        85 or 86 => "Weather_SnowShowers",
        95 => "Weather_Thunderstorm",
        96 or 99 => "Weather_Hail",
        _ => null,
    };
}

// MARK: JSON

public sealed class ForecastResponse
{
    public sealed class CurrentPart
    {
        [JsonPropertyName("temperature_2m")] public double Temperature { get; set; }
        [JsonPropertyName("apparent_temperature")] public double? FeelsLike { get; set; }
        [JsonPropertyName("relative_humidity_2m")] public double? Humidity { get; set; }
        [JsonPropertyName("wind_speed_10m")] public double? WindSpeed { get; set; }
        [JsonPropertyName("wind_direction_10m")] public double? WindDirection { get; set; }
        [JsonPropertyName("uv_index")] public double? UvIndex { get; set; }
        [JsonPropertyName("weather_code")] public int Code { get; set; }
        [JsonPropertyName("is_day")] public int IsDay { get; set; }
    }

    public sealed class HourlyPart
    {
        [JsonPropertyName("time")] public string[] Time { get; set; } = [];
        [JsonPropertyName("temperature_2m")] public double[] Temperature { get; set; } = [];
        [JsonPropertyName("weather_code")] public int[] Code { get; set; } = [];
        [JsonPropertyName("is_day")] public int[] IsDay { get; set; } = [];
    }

    public sealed class DailyPart
    {
        [JsonPropertyName("time")] public string[] Time { get; set; } = [];
        [JsonPropertyName("weather_code")] public int[] Code { get; set; } = [];
        [JsonPropertyName("temperature_2m_max")] public double[] High { get; set; } = [];
        [JsonPropertyName("temperature_2m_min")] public double[] Low { get; set; } = [];
        [JsonPropertyName("precipitation_probability_max")] public int?[]? Rain { get; set; }
    }

    [JsonPropertyName("current")] public CurrentPart? Current { get; set; }
    [JsonPropertyName("hourly")] public HourlyPart? Hourly { get; set; }
    [JsonPropertyName("daily")] public DailyPart? Daily { get; set; }
}

public sealed class GeocodingResponse
{
    public sealed class Result
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("latitude")] public double Latitude { get; set; }
        [JsonPropertyName("longitude")] public double Longitude { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("admin1")] public string? Admin1 { get; set; }
    }

    [JsonPropertyName("results")] public Result[]? Results { get; set; }
}

/// <summary>Source-generated, so parsing works under Native AOT.</summary>
[JsonSerializable(typeof(ForecastResponse))]
[JsonSerializable(typeof(GeocodingResponse))]
internal sealed partial class OpenMeteoJson : JsonSerializerContext;
