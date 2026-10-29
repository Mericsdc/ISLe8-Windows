using System.Diagnostics;
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
/// empty working folder, no window, below-normal priority and a hard deadline. Only files in folders
/// no other account can write to are ever run.
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

    /// <summary>The executable for a vendor's CLI: a native .exe, or the one an npm shim (.cmd) starts.</summary>
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
        return (Found.Missing, null);
    }

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
    private static void Prepare(ProcessStartInfo info, string binary, IReadOnlyDictionary<string, string>? extra)
    {
        string[] keep =
        [
            "USERPROFILE", "USERNAME", "USERDOMAIN", "HOMEDRIVE", "HOMEPATH", "APPDATA", "LOCALAPPDATA", "SystemRoot", "SystemDrive", "windir",
            "ComSpec", "PATHEXT", "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles",
            "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS",
        ];
        var mine = Environment.GetEnvironmentVariables();
        info.Environment.Clear();
        foreach (var key in keep)
        {
            if (mine[key] is string value) info.Environment[key] = value;
        }
        info.Environment["PATH"] = string.Join(';', Path.GetDirectoryName(binary), Environment.GetFolderPath(Environment.SpecialFolder.System), Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        info.Environment["TEMP"] = info.Environment["TMP"] = WorkDir;
        info.Environment["NO_COLOR"] = "1";
        info.Environment["LANG"] = "en_US.UTF-8";
        foreach (var (key, value) in extra ?? new Dictionary<string, string>()) info.Environment[key] = value;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.WorkingDirectory = WorkDir;
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
    }

    /// <summary>Runs to completion under a deadline. Null if it couldn't start, timed out or was cancelled.</summary>
    public static async Task<Output?> RunAsync(string binary, IEnumerable<string> arguments, TimeSpan timeout, IReadOnlyDictionary<string, string>? extra, CancellationToken cancel)
    {
        var info = new ProcessStartInfo(binary);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        Prepare(info, binary, extra);
        using var job = Job.Create();
        using var process = Start(info, job);
        if (process is null) return null;
        process.StandardInput.Close();
        _ = process.StandardError.ReadToEndAsync(cancel);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(timeout);
        try
        {
            var text = await ReadAllAsync(process.StandardOutput, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            return new Output(text, process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            return null;
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
        var info = new ProcessStartInfo(binary);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        Prepare(info, binary, null);
        using var job = Job.Create();
        using var process = Start(info, job);
        if (process is null) return null;
        _ = process.StandardError.ReadToEndAsync(cancel);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(timeout);
        async Task Send(JsonObject message)
        {
            await process.StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), deadline.Token);
            await process.StandardInput.FlushAsync(deadline.Token);
        }
        async Task<JsonObject?> Reply(int id)
        {
            while (await process.StandardOutput.ReadLineAsync(deadline.Token) is { } line)
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
        catch (Exception e) when (e is OperationCanceledException or IOException)
        {
            return null;
        }
        finally
        {
            Kill(process);
        }
    }

    private static Process? Start(ProcessStartInfo info, Job job)
    {
        try
        {
            var process = Process.Start(info);
            if (process is null) return null;
            job.Assign(process);
            try
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
            }
            return process;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>A Job Object that kills every process in it when its handle closes: nothing a CLI starts outlives its question.</summary>
    private sealed partial class Job : IDisposable
    {
        private readonly nint _handle;

        private Job(nint handle) => _handle = handle;

        public static Job Create()
        {
            var handle = CreateJobObjectW(0, null);
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = 0x2000 /* KILL_ON_JOB_CLOSE */ } };
            unsafe
            {
                SetInformationJobObject(handle, 9 /* JobObjectExtendedLimitInformation */, &limits, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            }
            return new Job(handle);
        }

        public void Assign(Process process) => AssignProcessToJobObject(_handle, process.Handle);

        public void Dispose()
        {
            if (_handle != 0) CloseHandle(_handle);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial nint CreateJobObjectW(nint attributes, string? name);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static unsafe partial bool SetInformationJobObject(nint job, int infoClass, void* info, uint length);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AssignProcessToJobObject(nint job, nint process);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);
    }
}
