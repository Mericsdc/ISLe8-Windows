using ISLe8.Core.Diagnostics;
using ISLe8.Core.Settings;
using Microsoft.Win32;

namespace ISLe8.Shell;

/// <summary>
/// Settings › General › Open at login, for ISLe8 run from its folder: a value under the user's
/// <c>Run</c> key, so no administrator rights are needed. The registry is the only record, so the
/// switch always shows what Windows will really do. (A packaged ISLe8 would use a StartupTask; that
/// comes with the package identity, PORTING.md §3.17.)
/// </summary>
internal static class StartAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string Executable => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ISLe8.exe");

    public static bool IsOn => LoginItem.PointsAt(Read(), Executable);

    public static void Set(bool on)
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

    /// <summary>At start: the folder may have moved since the entry was written.</summary>
    public static void Repair()
    {
        if (LoginItem.ShouldRepair(Read(), Executable, File.Exists)) Set(true);
    }

    private static string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(LoginItem.Name) as string;
    }
}
