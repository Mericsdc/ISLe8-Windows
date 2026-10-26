namespace ISLe8.Core.Diagnostics;

/// <summary>
/// A small text log in the app's data folder, so a problem on a Windows PC can be read afterwards.
/// It holds errors and lifecycle lines only: never song titles, clipboard text or notification content.
/// </summary>
public static class Log
{
    private const long MaxBytes = 256 * 1024;
    private static readonly Lock Gate = new();

    public static string? FilePath { get; set; }

    public static void Info(string message) => Write(message);

    public static void Error(Exception error, string context) =>
        Write($"[{context}] {error.GetType().Name}: {error.Message}{Environment.NewLine}{error.StackTrace}");

    private static void Write(string message)
    {
        if (FilePath is not { } path) return;
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var file = new FileInfo(path);
                if (file.Exists && file.Length > MaxBytes) File.Move(path, path + ".old", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
