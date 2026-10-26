using ISLe8.Core.Diagnostics;
using ISLe8.Core.Settings;
using ISLe8.Island;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;

namespace ISLe8;

/// <summary>
/// The WinUI application. There is no App.xaml: the control styles are merged in code, and this
/// class answers XAML's type lookups by passing them to the WinUI controls' own metadata.
/// </summary>
public partial class App : Application, IXamlMetadataProvider
{
    private readonly XamlControlsXamlMetaDataProvider _metadata = new();
    private IslandController? _island;

    public App()
    {
        // The island is always dark, whatever Windows' app mode is.
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
        _island = new IslandController(new SettingsStore(AppPaths.Settings));
        _island.Start();
    }

    public void Quit()
    {
        Log.Info("quit");
        _island?.Dispose();
        _island = null;
        Exit();
    }

    public IXamlType GetXamlType(Type type) => _metadata.GetXamlType(type);

    public IXamlType GetXamlType(string fullName) => _metadata.GetXamlType(fullName);

    public XmlnsDefinition[] GetXmlnsDefinitions() => _metadata.GetXmlnsDefinitions();
}
