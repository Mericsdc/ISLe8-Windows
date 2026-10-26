namespace ISLe8.Core.Settings;

/// <summary>
/// "Open at login" without a package: the command line ISLe8 writes under the user's
/// <c>Run</c> key, and whether one already there still points at this copy of ISLe8. The folder
/// from the zip can be moved; the entry follows it.
/// </summary>
public static class LoginItem
{
    /// <summary>The value's name under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>.</summary>
    public const string Name = "ISLe8";

    /// <summary>The executable in quotes, so a path with spaces starts the right program.</summary>
    public static string Command(string executable) => $"\"{executable}\"";

    /// <summary>The program <paramref name="command"/> starts, as found in the registry; null for none.</summary>
    public static string? Target(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = command.Trim();
        return text.StartsWith('"') && text.IndexOf('"', 1) is var end and > 0
            ? text[1..end]
            : text.Split(' ', 2)[0];
    }

    /// <summary>Whether <paramref name="command"/> starts <paramref name="executable"/>.</summary>
    public static bool PointsAt(string? command, string executable) =>
        Target(command) is { } target
        && string.Equals(Path.GetFullPath(target), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// An entry left behind by a copy of ISLe8 that has since been moved or deleted is pointed at this
    /// one. An entry for another copy that still exists is left alone.
    /// </summary>
    public static bool ShouldRepair(string? command, string executable, Func<string, bool> exists) =>
        Target(command) is { } target && !PointsAt(command, executable) && !exists(target);
}
