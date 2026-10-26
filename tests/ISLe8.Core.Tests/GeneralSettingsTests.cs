using System.Text.Json;
using ISLe8.Core.Geometry;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Tests;

/// <summary>Settings › General: which screen, open at login, backing up.</summary>
public class GeneralSettingsTests
{
    private static readonly DisplaySummary[] Displays =
    [
        new(@"\\.\DISPLAY1", IsPrimary: false),
        new(@"\\.\DISPLAY2", IsPrimary: true),
        new(@"\\.\DISPLAY3", IsPrimary: false),
    ];

    // MARK: Screen

    [Fact]
    public void TheMainScreenIsTheDefault()
    {
        Assert.Equal(1, DisplayChoice.Pick(Displays, new AppSettings()));
    }

    [Fact]
    public void ANamedScreenIsUsedWhileItIsConnected()
    {
        var settings = new AppSettings { Monitor = MonitorChoice.Named, MonitorDevice = @"\\.\DISPLAY3" };

        Assert.Equal(2, DisplayChoice.Pick(Displays, settings));
        Assert.Equal(1, DisplayChoice.Pick(Displays[..2], settings));
    }

    [Fact]
    public void TheActiveScreenFollowsTheLastWindowYouUsed()
    {
        var settings = new AppSettings { Monitor = MonitorChoice.Active };

        Assert.Equal(0, DisplayChoice.Pick(Displays, settings, active: 0));
        Assert.Equal(2, DisplayChoice.Pick(Displays, settings, active: 2));
        // Nothing used yet, or a screen that has since gone: the main one.
        Assert.Equal(1, DisplayChoice.Pick(Displays, settings, active: -1));
        Assert.Equal(1, DisplayChoice.Pick(Displays, settings, active: 7));
    }

    [Fact]
    public void WithoutAMainScreenTheFirstWins()
    {
        Assert.Equal(0, DisplayChoice.Pick([new("a", false), new("b", false)], new AppSettings()));
        Assert.Equal(-1, DisplayChoice.Pick([], new AppSettings()));
    }

    [Fact]
    public void ActiveIsReadFromTheFile()
    {
        Assert.Equal(MonitorChoice.Active, SettingsStore.Import("""{ "monitor": "active" }""").Monitor);
    }

    // MARK: Open at login

    [Fact]
    public void TheLoginCommandQuotesThePath()
    {
        Assert.Equal("\"C:\\Program Files\\ISLe8\\ISLe8.exe\"", LoginItem.Command(@"C:\Program Files\ISLe8\ISLe8.exe"));
    }

    [Theory]
    [InlineData("\"C:\\Tools\\ISLe8\\ISLe8.exe\"", true)]
    [InlineData("\"c:\\tools\\isle8\\ISLE8.EXE\" --quiet", true)]
    [InlineData("C:\\Tools\\ISLe8\\ISLe8.exe", true)]
    [InlineData("\"D:\\Old\\ISLe8\\ISLe8.exe\"", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ALoginEntryMustStartThisCopy(string? command, bool expected)
    {
        Assert.Equal(expected, LoginItem.PointsAt(command, @"C:\Tools\ISLe8\ISLe8.exe"));
    }

    [Fact]
    public void AnEntryFromAMovedCopyIsRepairedButAnotherCopysIsNot()
    {
        const string here = @"C:\Tools\ISLe8\ISLe8.exe";

        Assert.True(LoginItem.ShouldRepair("\"D:\\Old\\ISLe8\\ISLe8.exe\"", here, exists: _ => false));
        Assert.False(LoginItem.ShouldRepair("\"D:\\Other\\ISLe8\\ISLe8.exe\"", here, exists: _ => true));
        Assert.False(LoginItem.ShouldRepair("\"C:\\Tools\\ISLe8\\ISLe8.exe\"", here, exists: _ => false));
        Assert.False(LoginItem.ShouldRepair(null, here, exists: _ => false));
    }

    // MARK: Back up

    [Fact]
    public void ABackupComesBackTheSame()
    {
        var settings = new AppSettings
        {
            ThemeId = "graphite",
            OpenMode = OpenMode.Click,
            OpenDelay = 0.2,
            CloseDelay = 0.8,
            Monitor = MonitorChoice.Active,
            Language = "en-US",
        };

        var restored = SettingsStore.Import(SettingsStore.Export(settings));

        Assert.Equal(SettingsStore.Export(settings), SettingsStore.Export(restored));
    }

    [Fact]
    public void AnImportedBackupIsBroughtIntoRange()
    {
        var settings = SettingsStore.Import("""{ "openDelay": 9, "closeDelay": -1, "language": "fr" }""");

        Assert.Equal(1, settings.OpenDelay);
        Assert.Equal(0, settings.CloseDelay);
        Assert.Equal("system", settings.Language);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[1, 2]")]
    [InlineData("\"settings\"")]
    public void SomethingElseIsRefused(string text)
    {
        Assert.ThrowsAny<JsonException>(() => SettingsStore.Import(text));
    }
}
