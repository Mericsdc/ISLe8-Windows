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

    [Fact]
    public void DefaultsToEmberWhenThereIsNoFile()
    {
        var settings = new SettingsStore(FilePath).Load();

        Assert.Equal("ember", settings.Theme.Id);
        Assert.Equal("system", settings.Language);
        Assert.Equal(150, settings.HoverDelayMs);
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
        var settings = new AppSettings { HoverDelayMs = -5, AnimationSpeed = 40, Language = "xx", Monitor = MonitorChoice.Named };

        settings.Normalize();

        Assert.Equal(0, settings.HoverDelayMs);
        Assert.Equal(4, settings.AnimationSpeed);
        Assert.Equal("system", settings.Language);
        Assert.Equal(MonitorChoice.Primary, settings.Monitor);
    }
}
