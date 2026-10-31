using System.Runtime.InteropServices;
using System.Text;
using ISLe8.Core.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace ISLe8.Usage;

/// <summary>
/// One vendor CLI asked one question (UsageCli). It starts suspended, so it is in its Job Object before it can
/// start anything, and on a desktop of ISLe8's own that nobody sees, where a window it opens stays. Windows Terminal
/// may not: it opens a new console itself, through COM, on the user's desktop. agy's updater asked for one every
/// quarter of an hour, a terminal window coming and going in the middle of a game; agy now runs with its updates off
/// (UsageService) and alone (<c>Start</c>'s <c>alone</c>). Its standard streams are pipes, the only handles it inherits.
/// </summary>
internal sealed unsafe partial class UsageChild : IDisposable
{
    private static readonly Lazy<string?> Desktop = new(CreateDesktop);
    private readonly nint _process;
    private readonly nint _job;

    private UsageChild(nint process, nint job, nint input, nint output, nint error)
    {
        _process = process;
        _job = job;
        Input = new StreamWriter(new FileStream(new SafeFileHandle(input, ownsHandle: true), FileAccess.Write, 1, isAsync: false), new UTF8Encoding(false));
        Output = new StreamReader(new FileStream(new SafeFileHandle(output, ownsHandle: true), FileAccess.Read, 4096, isAsync: false), Encoding.UTF8);
        Error = new StreamReader(new FileStream(new SafeFileHandle(error, ownsHandle: true), FileAccess.Read, 4096, isAsync: false), Encoding.UTF8);
    }

    public StreamWriter Input { get; }
    public StreamReader Output { get; }
    public StreamReader Error { get; }

    public int ExitCode => GetExitCodeProcess(_process, out var code) ? (int)code : -1;

    /// <summary>
    /// Null when Windows wouldn't start it. <paramref name="alone"/>: Windows refuses it every process it starts, an
    /// updater's included; a console host is one too, so it runs with no console at all.
    /// </summary>
    public static UsageChild? Start(string binary, IEnumerable<string> arguments, IReadOnlyDictionary<string, string> environment, string folder, bool alone = false)
    {
        var pipes = new nint[6];
        var started = false;
        try
        {
            // [0] stdin's read end and [1] its write end, [2]/[3] stdout's, [4]/[5] stderr's; the child's ends inherit.
            var inherit = new SecurityAttributes { Length = (uint)sizeof(SecurityAttributes), InheritHandle = 1 };
            for (var i = 0; i < 6; i += 2)
            {
                if (!CreatePipe(out pipes[i], out pipes[i + 1], &inherit, 0)) return null;
            }
            SetHandleInformation(pipes[1], 1 /* HANDLE_FLAG_INHERIT */, 0);
            SetHandleInformation(pipes[2], 1, 0);
            SetHandleInformation(pipes[4], 1, 0);
            var mine = new[] { pipes[0], pipes[3], pipes[5] };

            nuint size = 0;
            var attributes = alone ? 2u : 1u;
            InitializeProcThreadAttributeList(null, attributes, 0, &size);
            var list = stackalloc byte[(int)size];
            if (!InitializeProcThreadAttributeList(list, attributes, 0, &size)) return null;
            var policy = 1u /* PROCESS_CREATION_CHILD_PROCESS_RESTRICTED */;
            try
            {
                fixed (nint* handles = mine)
                fixed (char* commandLine = CommandLine(binary, arguments) + "\0")
                fixed (char* block = Block(environment))
                fixed (char* desktop = Desktop.Value)
                {
                    // Only the three pipe ends are inherited, whatever else ISLe8 has open.
                    if (!UpdateProcThreadAttribute(list, 0, 0x20002 /* PROC_THREAD_ATTRIBUTE_HANDLE_LIST */, handles, (nuint)(mine.Length * sizeof(nint)), null, null)) return null;
                    if (alone && !UpdateProcThreadAttribute(list, 0, 0x2000E /* PROC_THREAD_ATTRIBUTE_CHILD_PROCESS_POLICY */, &policy, sizeof(uint), null, null)) return null;
                    var startup = new StartupInfoEx
                    {
                        Info = new StartupInfo
                        {
                            Length = (uint)sizeof(StartupInfoEx),
                            Desktop = desktop,
                            Flags = 0x100 /* STARTF_USESTDHANDLES */,
                            StdInput = pipes[0],
                            StdOutput = pipes[3],
                            StdError = pipes[5],
                        },
                        AttributeList = list,
                    };
                    var flags = 0x4 /* CREATE_SUSPENDED */ | (alone ? 0x8u /* DETACHED_PROCESS */ : 0x08000000u /* CREATE_NO_WINDOW */)
                        | 0x400 /* CREATE_UNICODE_ENVIRONMENT */ | 0x80000 /* EXTENDED_STARTUPINFO_PRESENT */ | 0x4000 /* BELOW_NORMAL_PRIORITY_CLASS */;
                    if (!CreateProcessW(null, commandLine, null, null, true, flags, block, folder, &startup, out var info)) return null;
                    var job = Job();
                    if (job == 0 || !AssignProcessToJobObject(job, info.Process)) Log.Info($"usage: no job for {Path.GetFileName(binary)} ({Marshal.GetLastPInvokeError()})");
                    ResumeThread(info.Thread);
                    CloseHandle(info.Thread);
                    started = true;
                    return new UsageChild(info.Process, job, pipes[1], pipes[2], pipes[4]);
                }
            }
            finally
            {
                DeleteProcThreadAttributeList(list);
            }
        }
        finally
        {
            // The child's ends are its own now; on failure, every end goes.
            foreach (var i in started ? [0, 3, 5] : new[] { 0, 1, 2, 3, 4, 5 })
            {
                if (pipes[i] != 0) CloseHandle(pipes[i]);
            }
        }
    }

    /// <summary>Ends whenever the CLI does: by itself, or killed.</summary>
    public Task WaitForExitAsync()
    {
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // A handle of the wait's own, so disposing the child meanwhile doesn't pull it from under the wait.
        var self = GetCurrentProcess();
        if (!DuplicateHandle(self, _process, self, out var copy, 0x00100000 /* SYNCHRONIZE */, false, 0))
        {
            exited.SetResult();
            return exited.Task;
        }
        var handle = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(copy, ownsHandle: true) };
        RegisteredWaitHandle? registration = null;
        registration = ThreadPool.RegisterWaitForSingleObject(handle, (_, _) =>
        {
            registration?.Unregister(null);
            handle.Dispose();
            exited.TrySetResult();
        }, null, Timeout.Infinite, executeOnlyOnce: true);
        return exited.Task;
    }

    /// <summary>The CLI and everything it started. A read waiting on its output then ends: the pipe closes.</summary>
    public void Kill()
    {
        if (_job != 0) TerminateJobObject(_job, 1);
        else TerminateProcess(_process, 1);
    }

    public void Dispose()
    {
        Input.Dispose();
        Output.Dispose();
        Error.Dispose();
        // Whatever is still running in the job goes with it (KILL_ON_JOB_CLOSE).
        if (_job != 0) CloseHandle(_job);
        CloseHandle(_process);
    }

    /// <summary>The program and its arguments as Windows' C runtime reads them back (quotes and the backslashes before them).</summary>
    private static string CommandLine(string binary, IEnumerable<string> arguments)
    {
        var line = new StringBuilder();
        foreach (var argument in arguments.Prepend(binary))
        {
            if (line.Length > 0) line.Append(' ');
            if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
            {
                line.Append(argument);
                continue;
            }
            line.Append('"');
            var slashes = 0;
            foreach (var c in argument)
            {
                if (c == '\\')
                {
                    slashes++;
                    continue;
                }
                line.Append('\\', c == '"' ? 2 * slashes + 1 : slashes);
                line.Append(c);
                slashes = 0;
            }
            line.Append('\\', 2 * slashes).Append('"');
        }
        return line.ToString();
    }

    /// <summary>The environment block: NAME=value pairs in Windows' order, each ending in a null, and one more at the end.</summary>
    private static string Block(IReadOnlyDictionary<string, string> environment)
    {
        var block = new StringBuilder();
        foreach (var (key, value) in environment.OrderBy(pair => pair.Key.ToUpperInvariant(), StringComparer.Ordinal)) block.Append(key).Append('=').Append(value).Append('\0');
        return block.Append('\0').ToString();
    }

    /// <summary>A desktop in ISLe8's window station that is never shown, made once; null to use the user's if Windows says no.</summary>
    private static string? CreateDesktop()
    {
        const string name = "ISLe8-cli";
        if (CreateDesktopW(name, null, null, 0, 0x10000000 /* GENERIC_ALL */, null) == 0)
        {
            Log.Info($"usage: no desktop of its own ({Marshal.GetLastPInvokeError()})");
            return null;
        }
        // The handle stays open as long as ISLe8 runs: the desktop lives while someone has it open.
        var station = stackalloc char[64];
        uint length = 0;
        var stationName = GetUserObjectInformationW(GetProcessWindowStation(), 2 /* UOI_NAME */, station, 128, &length) ? new string(station) : "WinSta0";
        return $"{stationName}\\{name}";
    }

    /// <summary>A Job Object that kills every process in it when its handle closes: nothing a CLI starts outlives its question.</summary>
    private static nint Job()
    {
        var job = CreateJobObjectW(null, null);
        if (job == 0) return 0;
        var limits = new JobLimits { Basic = new JobBasicLimits { Flags = 0x2000 /* KILL_ON_JOB_CLOSE */ } };
        SetInformationJobObject(job, 9 /* JobObjectExtendedLimitInformation */, &limits, (uint)sizeof(JobLimits));
        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public uint Length;
        public nint Descriptor;
        public int InheritHandle;
    }

    /// <summary>STARTUPINFOW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Length;
        public char* Reserved;
        public char* Desktop;
        public char* Title;
        public uint X, Y, Width, Height, Columns, Rows, Fill, Flags;
        public ushort ShowWindow;
        public ushort Reserved2Length;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
    }

    /// <summary>STARTUPINFOEXW.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo Info;
        public void* AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobBasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint Flags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    /// <summary>JOBOBJECT_EXTENDED_LIMIT_INFORMATION.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct JobLimits
    {
        public JobBasicLimits Basic;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreatePipe(out nint read, out nint write, SecurityAttributes* attributes, uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(nint handle, uint mask, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(void* list, uint count, uint flags, nuint* size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(void* list, uint flags, nuint attribute, void* value, nuint size, void* previous, nuint* returned);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(void* list);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateProcessW(string? application, char* commandLine, void* processAttributes, void* threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, char* environment, string folder, StartupInfoEx* startup, out ProcessInformation information);

    [LibraryImport("kernel32.dll")]
    private static partial uint ResumeThread(nint thread);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DuplicateHandle(nint sourceProcess, nint source, nint targetProcess, out nint target, uint access,
        [MarshalAs(UnmanagedType.Bool)] bool inherit, uint options);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetExitCodeProcess(nint process, out uint code);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(nint process, uint code);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateJobObjectW(void* attributes, string? name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(nint job, int infoClass, void* info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, nint process);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateJobObject(nint job, uint code);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateDesktopW(string name, string? device, void* mode, uint flags, uint access, void* attributes);

    [LibraryImport("user32.dll")]
    private static partial nint GetProcessWindowStation();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetUserObjectInformationW(nint handle, int index, void* info, uint length, uint* needed);
}
