using System.Diagnostics;
using ISLe8.Core.Diagnostics;

namespace ISLe8.Shell;

/// <summary>
/// The second half of a zip update, done by the new ISLe8.exe itself, started from the downloaded folder with
/// <see cref="Argument"/> before any window: it waits for the old ISLe8 to quit, sets the old folder aside, copies
/// the new files in and starts ISLe8 from its folder; on failure the old folder goes back and starts again.
/// Up to 0.2.7 a hidden PowerShell script with -ExecutionPolicy Bypass did this, and Microsoft Defender took the
/// pattern (an unsigned program fetching an exe and swapping it in through a hidden script) for a trojan
/// (Trojan:Win32/Bearfoos.A!ml): it removed the new ISLe8.exe and the update ended with no ISLe8 at all.
/// The installed app still goes through the script: registering a package needs Add-AppxPackage.
/// </summary>
internal static class UpdateFinisher
{
    public const string Argument = "--finish-update";

    /// <summary>The plan file's lines: the old ISLe8's process id, the new folder, the folder it replaces, the log for Settings.</summary>
    public static int Run(string plan)
    {
        var lines = File.ReadAllLines(plan);
        var (wait, source, target, log) = (int.Parse(lines[0], System.Globalization.CultureInfo.InvariantCulture), lines[1], lines[2], lines[3]);
        var trace = Path.Combine(Path.GetDirectoryName(plan)!, "apply.log");
        void Trace(string step) => File.AppendAllText(trace, $"{DateTime.Now:HH:mm:ss.fff} {step}{Environment.NewLine}");
        Trace("started");
        try
        {
            using var old = Process.GetProcessById(wait);
            old.WaitForExit(TimeSpan.FromSeconds(30));
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        Trace("ISLe8 gone");
        Thread.Sleep(500);
        var aside = target + "-old";
        var moved = false;
        try
        {
            if (Directory.Exists(aside)) Directory.Delete(aside, recursive: true);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.Move(target, aside);
                    break;
                }
                catch (IOException) when (attempt < 10)
                {
                    Thread.Sleep(1000);
                }
            }
            moved = true;
            Copy(source, target);
            Trace("files in place");
            try
            {
                Directory.Delete(aside, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Removed with the next update.
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace($"failed: {e.Message}");
            Log.Error(e, "update");
            File.AppendAllText(log, e.Message + Environment.NewLine);
            if (moved)
            {
                try
                {
                    if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                    Directory.Move(aside, target);
                }
                catch (Exception back) when (back is IOException or UnauthorizedAccessException)
                {
                    Trace($"could not put the old folder back: {back.Message}");
                }
            }
        }
        using var _ = Process.Start(new ProcessStartInfo(Path.Combine(target, "ISLe8.exe")) { UseShellExecute = false, WorkingDirectory = target });
        Trace("started ISLe8");
        return 0;
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var folder in Directory.EnumerateDirectories(from)) Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
    }
}
