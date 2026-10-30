using ISLe8.Core.Settings;
using ISLe8.Core.Theming;

namespace ISLe8.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "isle8-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData(Core.Weather.TemperatureUnit.Fahrenheit)]
    [InlineData(Core.Weather.TemperatureUnit.Celsius)]
    public void TheWeatherUnitStaysAfterARestart(Core.Weather.TemperatureUnit unit)
    {
        var store = new SettingsStore(FilePath);
        var settings = store.Load();
        settings.WeatherUnit = unit;
        store.Save(settings);

        Assert.Equal(unit, new SettingsStore(FilePath).Load().WeatherUnit);
    }

    [Fact]
    public void DefaultsToEmberWhenThereIsNoFile()
    {
        var settings = new SettingsStore(FilePath).Load();

        Assert.Equal("ember", settings.Theme.Id);
        Assert.Equal("system", settings.Language);
        Assert.Equal(OpenMode.Hover, settings.OpenMode);
        Assert.Equal(0, settings.OpenDelay);
        Assert.Equal(0.35, settings.CloseDelay);
    }

    [Fact]
    public void AVersionOneFileTakesTheNewDefaultsAndDropsItsMilliseconds()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "version": 1, "themeId": "midnight", "hoverDelayMs": 150, "closeDelayMs": 250 }""");
        var store = new SettingsStore(FilePath);

        var settings = store.Load();
        store.Save(settings);

        Assert.Equal(0, settings.OpenDelay);
        Assert.Equal(0.35, settings.CloseDelay);
        Assert.Equal("midnight", settings.ThemeId);
        Assert.Equal(AppSettings.CurrentVersion, settings.Version);
        var written = File.ReadAllText(FilePath);
        Assert.DoesNotContain("DelayMs", written);
        Assert.Contains("\"openDelay\"", written);
    }

    [Fact]
    public void AVersionOneDelayChosenByHandIsKept()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "version": 1, "hoverDelayMs": 400, "closeDelayMs": 800 }""");

        var settings = new SettingsStore(FilePath).Load();

        Assert.Equal(0.4, settings.OpenDelay);
        Assert.Equal(0.8, settings.CloseDelay);
    }

    [Fact]
    public void OpenModeReadsInAnyCaseAndSurvivesAnUnknownValue()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, """{ "openMode": "CLICK" }""");
        Assert.Equal(OpenMode.Click, new SettingsStore(FilePath).Load().OpenMode);

        File.WriteAllText(FilePath, """{ "openMode": "wave" }""");
        Assert.Equal(OpenMode.Hover, new SettingsStore(FilePath).Load().OpenMode);
    }

    [Fact]
    public void SavesAndLoads()
    {
        var store = new SettingsStore(FilePath);
        var settings = new AppSettings
        {
            ThemeId = "midnight",
            Language = "en-US",
            Monitor = MonitorChoice.Named,
            MonitorDevice = @"\\.\DISPLAY2",
            CustomThemes = [IslandTheme.NotchBlack with { Id = "mine", Name = "Mine" }],
        };

        store.Save(settings);
        var loaded = store.Load();

        Assert.Equal("midnight", loaded.Theme.Id);
        Assert.Equal("en-US", loaded.Language);
        Assert.Equal(MonitorChoice.Named, loaded.Monitor);
        Assert.Equal(@"\\.\DISPLAY2", loaded.MonitorDevice);
        Assert.Equal("Mine", Assert.Single(loaded.CustomThemes).Name);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void ACustomThemeWins()
    {
        var settings = new AppSettings { ThemeId = "mine", CustomThemes = [IslandTheme.NotchBlack with { Id = "mine", Name = "Mine" }] };

        Assert.Equal("Mine", settings.Theme.Name);
    }

    [Fact]
    public void AnUnknownThemeFallsBackToNotchBlack()
    {
        Assert.Equal("notch-black", new AppSettings { ThemeId = "gone" }.Theme.Id);
    }

    [Fact]
    public void ABrokenFileIsSetAsideAndDefaultsAreUsed()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ this is not json");

        var settings = new SettingsStore(FilePath).Load();

        Assert.Equal("ember", settings.ThemeId);
        Assert.True(File.Exists(FilePath + ".broken"));
    }

    [Fact]
    public void NormalizeClampsHandEditedValues()
    {
        var settings = new AppSettings
        {
            OpenDelay = -5,
            CloseDelay = double.NaN,
            AnimationSpeed = 40,
            Language = "xx",
            Monitor = MonitorChoice.Named,
        };

        settings.Normalize();

        Assert.Equal(0, settings.OpenDelay);
        Assert.Equal(0.35, settings.CloseDelay);
        Assert.Equal(2, settings.AnimationSpeed);
        Assert.Equal("system", settings.Language);
        Assert.Equal(MonitorChoice.Primary, settings.Monitor);
    }
}
