using ISLe8.Core.Diagnostics;
using ISLe8.Core.Settings;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace ISLe8.Shell;

/// <summary>
/// Settings › General › Open at login. The package has a startup task (Package.appxmanifest), which
/// Windows lists under Settings › Apps › Startup; ISLe8 run from its folder writes a value under the
/// user's <c>Run</c> key, so no administrator rights are needed. Windows' own record is the only one,
/// so the switch always shows what Windows will really do.
/// </summary>
internal static class StartAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>The task's id in Package.appxmanifest.</summary>
    private const string TaskId = "ISLe8Startup";

    private static string Executable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ISLe8.exe");

    public static async Task<bool> IsOnAsync()
    {
        if (!AppPackage.IsInstalled) return LoginItem.PointsAt(Read(), Executable);
        try
        {
            return IsOn((await StartupTask.GetAsync(TaskId)).State);
        }
        catch (Exception e)
        {
            Log.Error(e, "open at login");
            return false;
        }
    }

    /// <summary>Turns it on or off; returns whether it is on now. Someone who turned the task off in Windows' Startup list has to turn it on there.</summary>
    public static async Task<bool> SetAsync(bool on)
    {
        if (!AppPackage.IsInstalled)
        {
            SetRunKey(on);
            return LoginItem.PointsAt(Read(), Executable);
        }
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (!on)
            {
                task.Disable();
                Log.Info("open at login: False");
                return false;
            }
            var state = await task.RequestEnableAsync();
            Log.Info($"open at login: {state}");
            if (state == StartupTaskState.DisabledByUser) await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps"));
            return IsOn(state);
        }
        catch (Exception e)
        {
            Log.Error(e, "open at login");
            return false;
        }
    }

    /// <summary>
    /// At start. The zip: the folder may have moved since the entry was written. The installed app, just moved
    /// to from the zip (Updater.MoveToPackageAsync): the zip opened at login, so its startup task takes over.
    /// </summary>
    public static void Repair()
    {
        if (!AppPackage.IsInstalled)
        {
            if (LoginItem.ShouldRepair(Read(), Executable, File.Exists)) SetRunKey(true);
            return;
        }
        var fromZip = Path.Combine(AppPaths.DataFolder, "login-from-zip");
        if (!File.Exists(fromZip)) return;
        try
        {
            File.Delete(fromZip);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error(e, "open at login");
        }
        _ = SetAsync(true);
    }

    private static bool IsOn(StartupTaskState state) => state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    private static void SetRunKey(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue(LoginItem.Name, LoginItem.Command(Executable));
            else key.DeleteValue(LoginItem.Name, throwOnMissingValue: false);
            Log.Info($"open at login: {on}");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            Log.Error(e, "open at login");
        }
    }

    private static string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(LoginItem.Name) as string;
    }
}
