using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ISLe8.Core.Usage;

namespace ISLe8.Usage;

/// <summary>
/// Finding, checking and starting the vendor CLIs (UsageSpawn.swift). The Mac sandboxes them with
/// Seatbelt; Windows has none, so every CLI runs in its own Job Object that kills it and whatever
/// it started, with an allowlisted environment (no token or key ISLe8 itself might carry), a private
/// empty working folder, no window (and on a desktop nobody sees, UsageChild), below-normal priority
/// and a hard deadline. Only files in folders no other account can write to are ever run.
/// </summary>
internal static partial class UsageCli
{
    public enum Found
    {
        Ok,
        Missing,
        Untrusted,
    }

    /// <summary>A CLI that prints more than this is not answering a quota question.</summary>
    private const int MaxOutput = 4 << 20;

    public static string Home { get; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>A private, empty working folder: no CLI ever runs inside a project and picks up its config.</summary>
    public static string WorkDir { get; } = CreateWorkDir();

    private static string CreateWorkDir()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lrxe8-isle8-usage-cwd");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Where the CLIs install themselves on Windows, then the user's own PATH folders inside the
    /// profile or Program Files. Each is checked before anything in it runs.
    /// </summary>
    private static IEnumerable<string> SearchPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] fixedFolders =
        [
            Path.Combine(Home, ".local", "bin"), Path.Combine(local, "agy", "bin"), Path.Combine(roaming, "npm"),
            Path.Combine(Home, ".bun", "bin"), Path.Combine(local, "Volta", "bin"), Path.Combine(local, "pnpm"),
            Path.Combine(Home, ".claude", "local"),
        ];
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var fromPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(folder => folder.StartsWith(Home, StringComparison.OrdinalIgnoreCase) || folder.StartsWith(programs, StringComparison.OrdinalIgnoreCase));
        return fixedFolders.Concat(fromPath).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The executable for a vendor's CLI: a native .exe, or the one an npm shim (.cmd) starts; failing those, the
    /// one a desktop app keeps for itself.
    /// </summary>
    public static (Found Found, string? Path) Find(UsageVendor vendor)
    {
        var name = UsageVendors.Cli(vendor);
        foreach (var folder in SearchPath())
        {
            string? target = null;
            var exe = Path.Combine(folder, name + ".exe");
            var shim = Path.Combine(folder, name + ".cmd");
            if (File.Exists(exe)) target = exe;
            else if (File.Exists(shim)) target = FromShim(vendor, shim);
            if (target is null) continue;
            return Trusted(target) ? (Found.Ok, target) : (Found.Untrusted, target);
        }
        foreach (var target in Bundled(vendor))
        {
            return Trusted(target) ? (Found.Ok, target) : (Found.Untrusted, target);
        }
        return (Found.Missing, null);
    }

    /// <summary>
    /// The CLI inside a desktop app, newest first: Claude's Code tab keeps Claude Code under
    /// <c>%LOCALAPPDATA%\Claude-Data\&lt;package&gt;\claude-code\&lt;version&gt;\&lt;build&gt;</c> (before that,
    /// <c>%APPDATA%\Claude\claude-code\&lt;version&gt;</c>); the Codex app keeps codex.exe under
    /// <c>%LOCALAPPDATA%\OpenAI\Codex\bin\&lt;build&gt;</c>, and Codex's own installer under
    /// <c>%USERPROFILE%\.codex\packages\standalone\current\bin</c>. Antigravity's app has no agy of its own.
    /// </summary>
    private static IEnumerable<string> Bundled(UsageVendor vendor)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        IEnumerable<string> candidates = vendor switch
        {
            UsageVendor.Claude => Newest(Below(Path.Combine(local, "Claude-Data"), "*", "claude-code", "*", "*", "claude.exe")
                .Concat(Below(Path.Combine(roaming, "Claude", "claude-code"), "*", "claude.exe"))),
            UsageVendor.Codex => new[] { Path.Combine(Home, ".codex", "packages", "standalone", "current", "bin", "codex.exe") }
                .Where(File.Exists)
                .Concat(Newest(Below(Path.Combine(local, "OpenAI", "Codex", "bin"), "*", "codex.exe"))),
            _ => [],
        };
        return candidates;
    }

    /// <summary>The files at <paramref name="root"/>\<paramref name="steps"/>, where a "*" step is any folder.</summary>
    private static IEnumerable<string> Below(string root, params string[] steps)
    {
        IEnumerable<string> found = [root];
        foreach (var step in steps.SkipLast(1))
        {
            found = found.SelectMany(folder =>
            {
                if (step != "*") return Directory.Exists(Path.Combine(folder, step)) ? [Path.Combine(folder, step)] : (IEnumerable<string>)[];
                try
                {
                    return Directory.Exists(folder) ? Directory.EnumerateDirectories(folder).ToList() : [];
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return [];
                }
            });
        }
        return found.Select(folder => Path.Combine(folder, steps[^1])).Where(File.Exists);
    }

    /// <summary>The most recently written first: an app updating its CLI leaves the old build beside the new one for a while.</summary>
    private static IEnumerable<string> Newest(IEnumerable<string> files) => files.OrderByDescending(File.GetLastWriteTimeUtc);

    /// <summary>
    /// The native program behind an npm shim, so neither cmd.exe nor node is in the way: Claude Code's
    /// package carries claude.exe; Codex's codex.js starts the binary of its platform package.
    /// </summary>
    private static string? FromShim(UsageVendor vendor, string shim)
    {
        var folder = Path.GetDirectoryName(shim)!;
        var text = File.ReadAllText(shim);
        // The shim names node.exe first ("IF EXIST %dp0%\node.exe"); the program is the last path it starts.
        var match = ShimTarget().Matches(text).LastOrDefault(found => !found.Groups[1].Value.EndsWith("node.exe", StringComparison.OrdinalIgnoreCase));
        if (match is null) return null;
        var target = Path.GetFullPath(Path.Combine(folder, match.Groups[1].Value.TrimStart('\\')));
        if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return File.Exists(target) ? target : null;
        if (vendor != UsageVendor.Codex) return null;
        // <scope>\codex\bin\codex.js → <scope>\codex\node_modules\@openai\codex-win32-<arch>\vendor\<triple>\bin\codex.exe (or beside the package).
        var package = Path.GetDirectoryName(Path.GetDirectoryName(target)!)!;
        var (arch, triple) = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? ("arm64", "aarch64-pc-windows-msvc") : ("x64", "x86_64-pc-windows-msvc");
        foreach (var platform in new[] { Path.Combine(package, "node_modules", "@openai", $"codex-win32-{arch}"), Path.Combine(Path.GetDirectoryName(package)!, $"codex-win32-{arch}") })
        {
            var binary = Path.Combine(platform, "vendor", triple, "bin", "codex.exe");
            if (File.Exists(binary)) return binary;
        }
        return null;
    }

    [GeneratedRegex("\"%dp0%\\\\([^\"]+\\.(?:exe|js))\"", RegexOptions.IgnoreCase)]
    private static partial Regex ShimTarget();

    // MARK: Trust

    private static readonly SecurityIdentifier[] Broad =
    [
        new(WellKnownSidType.WorldSid, null), new(WellKnownSidType.BuiltinUsersSid, null), new(WellKnownSidType.AuthenticatedUserSid, null),
        new(WellKnownSidType.InteractiveSid, null), new(WellKnownSidType.BuiltinGuestsSid, null),
    ];

    private static readonly SecurityIdentifier[] Owners =
    [
        new(WellKnownSidType.BuiltinAdministratorsSid, null), new(WellKnownSidType.LocalSystemSid, null),
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"), // TrustedInstaller
    ];

    /// <summary>
    /// Whether another account could replace what is about to run (UsageCLI.trusted): the file, its
    /// folder (where a planted DLL would load) and every folder above must be owned by this user or
    /// the system, and grant no broad group the right to write, delete or change them.
    /// </summary>
    public static bool Trusted(string path)
    {
        try
        {
            var me = WindowsIdentity.GetCurrent().User;
            if (!SafeNode(new FileInfo(path).GetAccessControl(), me, FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete)) return false;
            var folder = new DirectoryInfo(Path.GetDirectoryName(path)!);
            if (!SafeNode(folder.GetAccessControl(), me, FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete)) return false;
            for (var above = folder.Parent; above is not null; above = above.Parent)
            {
                if (!SafeNode(above.GetAccessControl(), me, FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete, ownerMatters: false)) return false;
            }
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool SafeNode(FileSystemSecurity security, SecurityIdentifier? me, FileSystemRights dangerous, bool ownerMatters = true)
    {
        dangerous |= FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        if (ownerMatters && security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner && owner != me && !Owners.Contains(owner)) return false;
        foreach (FileSystemAccessRule rule in security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)))
        {
            // Rules for what is created inside later don't apply to the folder itself.
            if (rule.AccessControlType != AccessControlType.Allow || rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly)) continue;
            if (rule.IdentityReference is SecurityIdentifier sid && Broad.Contains(sid) && (rule.FileSystemRights & dangerous) != 0) return false;
        }
        return true;
    }

    // MARK: Running

    public sealed record Output(string Text, int Code);

    /// <summary>The environment is an allowlist, not ISLe8's minus a denylist; the CLI's own folder goes first on PATH.</summary>
    private static Dictionary<string, string> ChildEnvironment(string binary, IReadOnlyDictionary<string, string>? extra)
    {
        string[] keep =
        [
            "USERPROFILE", "USERNAME", "USERDOMAIN", "HOMEDRIVE", "HOMEPATH", "APPDATA", "LOCALAPPDATA", "SystemRoot", "SystemDrive", "windir",
            "ComSpec", "PATHEXT", "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles",
            "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS",
        ];
        var mine = System.Environment.GetEnvironmentVariables();
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keep)
        {
            if (mine[key] is string value) environment[key] = value;
        }
        environment["PATH"] = string.Join(';', Path.GetDirectoryName(binary), System.Environment.GetFolderPath(System.Environment.SpecialFolder.System),
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows));
        environment["TEMP"] = environment["TMP"] = WorkDir;
        environment["NO_COLOR"] = "1";
        environment["LANG"] = "en_US.UTF-8";
        foreach (var (key, value) in extra ?? new Dictionary<string, string>()) environment[key] = value;
        return environment;
    }

    /// <summary>Runs to completion under a deadline. Null if it couldn't start, timed out or was cancelled.</summary>
    public static async Task<Output?> RunAsync(string binary, IEnumerable<string> arguments, TimeSpan timeout, IReadOnlyDictionary<string, string>? extra, CancellationToken cancel)
    {
        using var child = UsageChild.Start(binary, arguments, ChildEnvironment(binary, extra), WorkDir);
        if (child is null) return null;
        child.Input.Close();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(timeout);
        // A read waiting on a pipe doesn't hear a cancellation; ending the CLI closes the pipe.
        using var stop = deadline.Token.Register(child.Kill);
        _ = child.Error.ReadToEndAsync(CancellationToken.None);
        try
        {
            var text = await ReadAllAsync(child.Output, deadline.Token);
            await child.WaitForExitAsync().WaitAsync(deadline.Token);
            return deadline.IsCancellationRequested ? null : new Output(text, child.ExitCode);
        }
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
            return null;
        }
        finally
        {
            child.Kill();
        }
    }

    private static async Task<string> ReadAllAsync(StreamReader reader, CancellationToken cancel)
    {
        var text = new StringBuilder();
        var buffer = new char[16384];
        while (text.Length < MaxOutput)
        {
            var count = await reader.ReadAsync(buffer, cancel);
            if (count == 0) break;
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    /// <summary>
    /// One JSON-RPC exchange over stdio (Codex's app-server): initialize, a notification, one call;
    /// then the process is killed. Null on any failure; the failure text is never kept.
    /// </summary>
    public static async Task<JsonObject?> RpcAsync(string binary, IEnumerable<string> arguments, JsonObject initialize, string notify, string method, TimeSpan timeout, CancellationToken cancel)
    {
        using var child = UsageChild.Start(binary, arguments, ChildEnvironment(binary, null), WorkDir);
        if (child is null) return null;
        _ = child.Error.ReadToEndAsync(CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(timeout);
        using var stop = deadline.Token.Register(child.Kill);
        async Task Send(JsonObject message)
        {
            await child.Input.WriteLineAsync(message.ToJsonString().AsMemory(), deadline.Token);
            await child.Input.FlushAsync(deadline.Token);
        }
        async Task<JsonObject?> Reply(int id)
        {
            while (await child.Output.ReadLineAsync(deadline.Token) is { } line)
            {
                try
                {
                    if (JsonNode.Parse(line) is JsonObject answer && answer["id"] is JsonValue value && value.TryGetValue<int>(out var got) && got == id) return answer;
                }
                catch (System.Text.Json.JsonException)
                {
                }
            }
            return null;
        }
        try
        {
            await Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize", ["params"] = initialize });
            if ((await Reply(1))?["result"] is null) return null;
            await Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = notify, ["params"] = new JsonObject() });
            await Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 2, ["method"] = method, ["params"] = new JsonObject() });
            return (await Reply(2))?["result"] as JsonObject;
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or ObjectDisposedException)
        {
            return null;
        }
        finally
        {
            child.Kill();
        }
    }
}
