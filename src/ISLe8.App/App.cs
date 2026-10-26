using System.Diagnostics;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.Settings;
using ISLe8.Island;
using ISLe8.Settings;
using ISLe8.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;
using Microsoft.Windows.AppLifecycle;

namespace ISLe8;

/// <summary>
/// The WinUI application. There is no App.xaml: the control styles are merged in code, and this
/// class answers XAML's type lookups by passing them to the WinUI controls' own metadata.
/// </summary>
public partial class App : Application, IXamlMetadataProvider
{
    private readonly XamlControlsXamlMetaDataProvider _metadata = new();
    private IslandController? _island;
    private SettingsWindow? _settingsWindow;
    private TrayIcon? _tray;

    public App()
    {
        // The island is always dark, whatever Windows' app mode is. The settings window sets its own.
        RequestedTheme = ApplicationTheme.Dark;
        UnhandledException += (_, e) =>
        {
            Log.Error(e.Exception, "unhandled");
            // An accessory that stays up beats one that vanishes; the log says what happened.
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        var store = new SettingsStore(AppPaths.Settings);
        _island = new IslandController(store);
        _island.Start();
        _settingsWindow = new SettingsWindow(_island, store);

        _tray = new TrayIcon(_island.WindowHandle, Monitors.PrimaryDpi);
        _island.AddWindowMessageHandler(_tray.HandleMessage);
        _tray.SettingsRequested += OpenSettings;
        _tray.QuitRequested += Quit;
        _tray.Show();

        StartAtLogin.Repair();

        // Starting ISLe8 again while it runs opens its settings, as on the Mac (Program.Main redirects).
        var queue = DispatcherQueue.GetForCurrentThread();
        AppInstance.GetCurrent().Activated += (_, _) => queue.TryEnqueue(OpenSettings);
    }

    public void OpenSettings() => _settingsWindow?.Show();

    public void Quit()
    {
        Log.Info("quit");
        Close();
        Exit();
    }

    /// <summary>A new language or imported settings: they are read once at start, so ISLe8 starts again.</summary>
    public void Restart()
    {
        Log.Info("restart");
        Close();
        // Returns only if Windows could not restart it.
        var failure = AppInstance.Restart("");
        Log.Info($"restart through Windows failed ({failure}); starting a new copy");
        AppInstance.GetCurrent().UnregisterKey();
        if (Environment.ProcessPath is { } path) Process.Start(new ProcessStartInfo(path) { UseShellExecute = false });
        Exit();
    }

    /// <summary>The tray icon goes first, or a dead one lingers in the notification area until the pointer passes it.</summary>
    private void Close()
    {
        _tray?.Dispose();
        _tray = null;
        _island?.Dispose();
        _island = null;
    }

    public IXamlType GetXamlType(Type type) => _metadata.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => _metadata.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _metadata.GetXmlnsDefinitions();
}
