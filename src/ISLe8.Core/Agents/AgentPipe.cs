namespace ISLe8.Core.Agents;

/// <summary>
/// The local channel between <c>isle8</c> and ISLe8 (PORTING.md §1): a named pipe of one user in one
/// session, opened by that user only. On the Mac it is a Unix socket in Application Support.
/// Shared by the app and the command (linked into ISLe8.Cli).
/// </summary>
public static class AgentPipe
{
    public static string Name(string userSid, int sessionId) => $"isle8-{userSid}-{sessionId}";

    /// <summary>A request may carry a whole file a tool writes; anything bigger is not read.</summary>
    public const int MaxRequestBytes = 16 * 1024 * 1024;
}
