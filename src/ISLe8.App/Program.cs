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

        // One island per session. Starting it again opens the running island's settings (App.OnLaunched).
        var island = AppInstance.FindOrRegisterForKey("ISLe8.Island");
        if (!island.IsCurrent)
        {
            RedirectTo(island);
            return 0;
        }

        Log.Info($"start {typeof(Program).Assembly.GetName().Version} on {Environment.OSVersion.VersionString}");
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        return 0;
    }

    /// <summary>
    /// Hands this launch to the island that is already running. Its settings window may come to the
    /// front only because this process, the one you just started, lets it.
    /// </summary>
    private static void RedirectTo(AppInstance island)
    {
        try
        {
            Interop.Win32.AllowSetForegroundWindow(island.ProcessId);
            var launch = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Off this STA thread, which has no message loop to wait on.
            Task.Run(() => island.RedirectActivationToAsync(launch).AsTask()).Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception e)
        {
            Log.Error(e, "redirect");
        }
    }
}
