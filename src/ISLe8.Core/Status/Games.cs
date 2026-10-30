namespace ISLe8.Core.Status;

/// <summary>
/// The programs Windows itself took for games: Game Bar writes each one it recognises under
/// <c>HKCU\System\GameConfigStore\Children\*\MatchedExeFullPath</c>. Windows only.
/// </summary>
public static class Games
{
    /// <summary>The recognised paths, compared without case; blank or broken ones left out.</summary>
    public static HashSet<string> Set(IEnumerable<string?> paths)
    {
        var games = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (Normalize(path) is { } normal) games.Add(normal);
        }
        return games;
    }

    public static bool IsGame(string? program, IReadOnlySet<string> games) => Normalize(program) is { } normal && games.Contains(normal);

    private static string? Normalize(string? path)
    {
        var trimmed = path?.Trim().Trim('"');
        if (string.IsNullOrEmpty(trimmed)) return null;
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
