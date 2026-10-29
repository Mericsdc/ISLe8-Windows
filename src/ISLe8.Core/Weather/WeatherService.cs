using ISLe8.Core.Diagnostics;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Weather;

/// <summary>
/// The current report (WeatherModule.swift). Without a chosen city the one in the computer's time zone
/// is used, so no location permission is ever needed. Nothing runs in the background: the report
/// refreshes when Home is shown and it is older than 30 minutes, or the place or unit changed.
/// </summary>
public sealed class WeatherService(AppSettings settings, HttpClient http, Func<DateTimeOffset> now, string timeZoneId)
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);

    private int _request;
    /// <summary>The place and unit the current report was fetched for; a different one means fetch again.</summary>
    private string _reportSource = "";

    public WeatherReport? Report { get; private set; }
    public bool IsLoading { get; private set; }
    public bool Failed { get; private set; }

    /// <summary>The city the time zone gives, for Settings ("Saat dilimine göre (İstanbul)").</summary>
    public string TimeZonePlace => TimeZoneCity.From(timeZoneId);

    public event Action? Changed;

    private string Source => $"{(settings.WeatherCity.Length == 0 ? TimeZonePlace : settings.WeatherCity)}|{settings.WeatherUnit}";

    public Task RefreshIfStale()
    {
        if (IsLoading) return Task.CompletedTask;
        if (Report is { } report && now() - report.FetchedAt < MaxAge && _reportSource == Source) return Task.CompletedTask;
        return Refresh();
    }

    public async Task Refresh()
    {
        var request = ++_request;
        var source = Source;
        IsLoading = true;
        Changed?.Invoke();
        try
        {
            var place = await ResolvePlace();
            var json = await http.GetStringAsync(OpenMeteo.ForecastUrl(place, settings.WeatherUnit));
            if (request != _request) return;
            Report = OpenMeteo.ParseForecast(json, place, now());
            _reportSource = source;
            Failed = false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            if (request != _request) return;
            Log.Error(e, "weather");
            Failed = true;
        }
        finally
        {
            if (request == _request)
            {
                IsLoading = false;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Settings › Widgets: a place from the search; the report follows.</summary>
    public Task Choose(WeatherPlace place)
    {
        settings.WeatherCity = place.Name;
        settings.WeatherLatitude = place.Latitude;
        settings.WeatherLongitude = place.Longitude;
        return Refresh();
    }

    /// <summary>Back to the time zone's city.</summary>
    public Task UseTimeZone()
    {
        settings.WeatherCity = "";
        settings.WeatherLatitude = 0;
        settings.WeatherLongitude = 0;
        return Refresh();
    }

    /// <summary>Up to five matching places, named in Turkish where Open-Meteo knows the name.</summary>
    public async Task<IReadOnlyList<WeatherPlace>> Search(string query)
    {
        if (query.Trim().Length < 2) return [];
        try
        {
            return OpenMeteo.ParseGeocode(await http.GetStringAsync(OpenMeteo.GeocodeUrl(query.Trim(), 5)));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Error(e, "weather search");
            return [];
        }
    }

    private async Task<WeatherPlace> ResolvePlace()
    {
        if (settings.WeatherCity.Length > 0 && (settings.WeatherLatitude != 0 || settings.WeatherLongitude != 0))
        {
            return new WeatherPlace(settings.WeatherCity, "", settings.WeatherLatitude, settings.WeatherLongitude);
        }
        var json = await http.GetStringAsync(OpenMeteo.GeocodeUrl(TimeZonePlace, 1));
        return OpenMeteo.ParseGeocode(json).FirstOrDefault() ?? throw new InvalidOperationException($"no place for {TimeZonePlace}");
    }
}
