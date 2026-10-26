using ISLe8.Core.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace ISLe8;

internal static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\LRXe8\ISLe8. Inside the MSIX, Windows redirects it to the package's own folder.</summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LRXe8", "ISLe8");

    public static string Settings => Path.Combine(DataFolder, "settings.json");
    public static string Log => Path.Combine(DataFolder, "isle8.log");
}

public static class Program
{
    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Log.FilePath = AppPaths.Log;

        // One island per session: a second launch leaves quietly.
        if (!AppInstance.FindOrRegisterForKey("ISLe8.Island").IsCurrent) return 0;

        Log.Info($"start {typeof(Program).Assembly.GetName().Version} on {Environment.OSVersion.VersionString}");
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
    }
}
