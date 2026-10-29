using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using ISLe8.Core.Agents;

namespace ISLe8.Cli;

/// <summary>
/// The <c>isle8</c> command (CommandLineTool.swift): talks to the running ISLe8 over its pipe and exits.
///
///     isle8 hook claude|codex          agent hook; reads the hook's JSON on stdin
///     isle8 activity start|update|end  --id ID [--title T] [--subtitle S] [--progress 0.4] [--icon E768]
///     isle8 open [home|shelf|clipboard|notes|agents|devices|system]
///     isle8 close
///     isle8 devices
///     isle8 ping
///
/// Hooks always exit 0 and stay silent when ISLe8 isn't running, so an agent simply carries on as
/// if no hook were installed.
/// </summary>
internal static unsafe partial class Program
{
    private static int Main(string[] args)
    {
        var rest = args.Skip(1).ToArray();
        switch (args.FirstOrDefault())
        {
            case "hook": return Hook(rest);
            case "activity": return Activity(rest);
            case "open" or "close": return Island(args[0], rest.FirstOrDefault());
            case "devices": return Devices();
            case "ping": return Ping();
            case null or "help" or "--help" or "-h":
                Console.WriteLine(Text.Usage);
                return 0;
            default:
                Console.Error.WriteLine(Text.Usage);
                return 2;
        }
    }

    // MARK: Commands

    private static int Hook(string[] arguments)
    {
        var agent = arguments.FirstOrDefault() ?? "claude";
        JsonObject? hookEvent;
        try
        {
            using var input = Console.OpenStandardInput();
            using var buffer = new MemoryStream();
            input.CopyTo(buffer);
            var bytes = buffer.ToArray();
            // Agents send plain UTF-8; PowerShell puts a byte order mark in front, and some tools send UTF-16.
            var text = bytes is [0xFF, 0xFE, ..]
                ? Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2)
                : Encoding.UTF8.GetString(bytes).TrimStart('﻿');
            hookEvent = JsonNode.Parse(text) as JsonObject;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException)
        {
            return 0;
        }
        if (hookEvent is null) return 0;
        var name = hookEvent["hook_event_name"]?.GetValue<string>();
        var isApproval = name == "PermissionRequest";
        var request = new JsonObject { ["type"] = "hook", ["agent"] = agent, ["event"] = hookEvent };
        // Once per session or turn is enough to know which app to bring forward.
        if (name is "SessionStart" or "UserPromptSubmit" && HostAppPid() is { } pid) request["app_pid"] = pid;
        // An approval waits for the user; Claude Code and Codex allow hooks 600 s.
        if (Send(request, TimeSpan.FromSeconds(isApproval ? 590 : 3)) is not { } response) return 0;

        if (isApproval && response["decision"]?.GetValue<string>() is "allow" or "deny" && response["decision"]!.GetValue<string>() is var decision)
        {
            var choice = new JsonObject { ["behavior"] = decision };
            if (decision == "deny") choice["message"] = response["message"]?.GetValue<string>() ?? "Denied from ISLe8.";
            var output = new JsonObject
            {
                ["hookSpecificOutput"] = new JsonObject { ["hookEventName"] = "PermissionRequest", ["decision"] = choice },
            };
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(Encoding.UTF8.GetBytes(output.ToJsonString()));
        }
        return 0;
    }

    private static int Activity(string[] arguments)
    {
        if (arguments.FirstOrDefault() is not ("start" or "update" or "end") || arguments[0] is not { } action)
        {
            Console.Error.WriteLine(Text.Usage);
            return 2;
        }
        var request = new JsonObject { ["type"] = "activity", ["action"] = action };
        for (var index = 1; index + 1 < arguments.Length; index += 2)
        {
            var value = arguments[index + 1];
            switch (arguments[index])
            {
                case "--id": request["id"] = value; break;
                case "--title": request["title"] = value; break;
                case "--subtitle": request["subtitle"] = value; break;
                case "--icon": request["icon"] = value; break;
                case "--progress" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var progress):
                    request["progress"] = progress;
                    break;
            }
        }
        if (request["id"] is null)
        {
            Console.Error.WriteLine($"isle8: {Text.IdRequired}");
            return 2;
        }
        return Answered(Send(request, TimeSpan.FromSeconds(3))) ? 0 : 1;
    }

    private static int Island(string action, string? tab)
    {
        var request = new JsonObject { ["type"] = "island", ["action"] = action };
        if (tab is not null) request["tab"] = tab;
        return Answered(Send(request, TimeSpan.FromSeconds(3))) ? 0 : 1;
    }

    private static int Devices()
    {
        var response = Send(new JsonObject { ["type"] = "devices" }, TimeSpan.FromSeconds(3));
        if (!Answered(response)) return 1;
        var lines = (response!["devices"] as JsonArray)?.Select(line => line?.GetValue<string>()).OfType<string>().ToList() ?? [];
        Console.WriteLine(lines.Count == 0 ? Text.NoDevices : string.Join(Environment.NewLine, lines));
        return 0;
    }

    private static int Ping()
    {
        var running = Send(new JsonObject { ["type"] = "ping" }, TimeSpan.FromSeconds(2)) is not null;
        Console.WriteLine(running ? Text.Running : Text.NotRunning);
        return running ? 0 : 1;
    }

    private static bool Answered(JsonObject? response)
    {
        if (response is not null) return true;
        Console.Error.WriteLine($"isle8: {Text.NotRunning}");
        return false;
    }

    // MARK: Pipe client

    /// <summary>Sends one JSON line and waits for one back. Null if ISLe8 isn't there or doesn't answer.</summary>
    private static JsonObject? Send(JsonObject request, TimeSpan timeout)
    {
        var name = AgentPipe.Name(WindowsIdentity.GetCurrent().User?.Value ?? "", Process.GetCurrentProcess().SessionId);
        try
        {
            using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            // ISLe8 always keeps a free end listening; not finding one soon means it isn't running.
            pipe.Connect(150);
            pipe.Write(Encoding.UTF8.GetBytes(request.ToJsonString() + "\n"));
            pipe.Flush();
            using var cancel = new CancellationTokenSource(timeout);
            var response = new MemoryStream();
            var chunk = new byte[4096];
            while (Array.IndexOf(response.GetBuffer(), (byte)'\n', 0, (int)response.Length) < 0)
            {
                var count = pipe.ReadAsync(chunk, cancel.Token).AsTask().GetAwaiter().GetResult();
                if (count == 0) break;
                response.Write(chunk, 0, count);
            }
            var text = Encoding.UTF8.GetString(response.ToArray()).Split('\n')[0];
            return text.Length == 0 ? null : JsonNode.Parse(text) as JsonObject;
        }
        catch (Exception e) when (e is TimeoutException or IOException or OperationCanceledException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // MARK: Host app

    /// <summary>
    /// The nearest ancestor process with a window of its own: the terminal (Windows Terminal, VS Code)
    /// or the Claude app. The console window of a classic console, if none.
    /// </summary>
    private static int? HostAppPid()
    {
        var parents = new Dictionary<int, (int Parent, string Name)>();
        var snapshot = CreateToolhelp32Snapshot(0x2 /* TH32CS_SNAPPROCESS */, 0);
        if (snapshot == -1) return null;
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
            for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
            {
                parents[(int)entry.th32ProcessID] = ((int)entry.th32ParentProcessID, new string(entry.szExeFile));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }
        var windowed = WindowedProcesses();
        var pid = parents.TryGetValue(Environment.ProcessId, out var self) ? self.Parent : 0;
        for (var depth = 0; depth < 24 && pid > 4 && parents.TryGetValue(pid, out var process); depth++)
        {
            // Explorer starts everything; its windows are the desktop's, not the session's.
            if (process.Name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) break;
            if (windowed.Contains(pid)) return pid;
            pid = process.Parent;
        }
        var console = GetConsoleWindow();
        if (console != 0 && IsWindowVisible(console) && GetWindowThreadProcessId(console, out var owner) != 0) return (int)owner;
        return null;
    }

    /// <summary>Processes that own a visible, titled top-level window.</summary>
    private static HashSet<int> WindowedProcesses()
    {
        var found = new HashSet<int>();
        var handle = GCHandle.Alloc(found);
        try
        {
            EnumWindows(&Visit, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }
        return found;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int Visit(nint window, nint state)
    {
        if (IsWindowVisible(window) && GetWindow(window, 4 /* GW_OWNER */) == 0 && GetWindowTextLengthW(window) > 0
            && GetWindowThreadProcessId(window, out var pid) != 0)
        {
            ((HashSet<int>)GCHandle.FromIntPtr(state).Target!).Add((int)pid);
        }
        return 1;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[260];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(nint snapshot, ref PROCESSENTRY32W entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(nint snapshot, ref PROCESSENTRY32W entry);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [LibraryImport("user32.dll")]
    private static partial int EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint state);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindow(nint window, uint command);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(nint window);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint window, out uint processId);
}

/// <summary>The command's words, in Windows' display language: Turkish or English, as on the Mac.</summary>
internal static partial class Text
{
    private static readonly bool Turkish = (GetUserDefaultUILanguage() & 0x3FF) == 0x1F;

    // mac: isle8 — ISLe8'e komut satırından ulaşır. … (--icon takes a Segoe Fluent Icons code point here, not an SF Symbol)
    public static string Usage => Turkish
        ? """
          isle8 — ISLe8'e komut satırından ulaşır.

            isle8 hook claude|codex         Claude Code / Codex hook'u (girdi stdin'den JSON)
            isle8 activity start --id ID --title "Başlık" [--subtitle "…"] [--progress 0.4] [--icon E768]
            isle8 activity update --id ID [--title …] [--subtitle …] [--progress …]
            isle8 activity end --id ID
            isle8 open [home|shelf|clipboard|notes|agents|devices|system]   Adayı açar
            isle8 close                     Adayı kapatır
            isle8 devices                   Bağlı kablosuz cihazlar ve pilleri
            isle8 ping                      ISLe8 çalışıyor mu?
          """
        : """
          isle8 — talks to ISLe8 from the command line.

            isle8 hook claude|codex         Claude Code / Codex hook (JSON input on stdin)
            isle8 activity start --id ID --title "Title" [--subtitle "…"] [--progress 0.4] [--icon E768]
            isle8 activity update --id ID [--title …] [--subtitle …] [--progress …]
            isle8 activity end --id ID
            isle8 open [home|shelf|clipboard|notes|agents|devices|system]   Opens the island
            isle8 close                     Closes the island
            isle8 devices                   Connected wireless devices and their batteries
            isle8 ping                      Is ISLe8 running?
          """;

    // mac: ISLe8 çalışmıyor
    public static string NotRunning => Turkish ? "ISLe8 çalışmıyor" : "ISLe8 isn't running";

    // mac: ISLe8 çalışıyor
    public static string Running => Turkish ? "ISLe8 çalışıyor" : "ISLe8 is running";

    // mac: --id gerekli
    public static string IdRequired => Turkish ? "--id gerekli" : "--id is required";

    // mac: Pil bildiren bağlı kablosuz cihaz yok
    public static string NoDevices => Turkish ? "Pil bildiren bağlı kablosuz cihaz yok" : "No connected wireless device reports a battery";

    [LibraryImport("kernel32.dll")]
    private static partial ushort GetUserDefaultUILanguage();
}
