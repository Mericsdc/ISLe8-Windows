using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.Settings;
using ISLe8.Core.Updates;
using Microsoft.UI.Dispatching;

namespace ISLe8.Shell;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Available,
    Downloading,
    Installing,
    Failed,
}

/// <summary>
/// Updates from the project's GitHub releases (Settings › General › Updates; Windows only). It asks
/// GitHub once a day, and when the user presses Update it downloads the file for this kind of ISLe8
/// (the installed app's package, or the zip), checks its SHA-256, and hands over to a small script
/// that waits for ISLe8 to quit, puts the new files where the old ones were (keeping the old ones
/// until the new ones are in), registers the package again for the installed app, and starts ISLe8.
/// Nothing is downloaded or run without the button.
/// </summary>
internal sealed partial class Updater
{
    private static readonly HttpClient Http = CreateClient();

    private readonly AppSettings _settings;
    private readonly Action _save;
    private readonly DispatcherQueue _queue;
    private readonly Action _quit;
    private DispatcherQueueTimer? _daily;

    public Updater(AppSettings settings, Action save, DispatcherQueue queue, Action quit)
    {
        _settings = settings;
        _save = save;
        _queue = queue;
        _quit = quit;
    }

    public static Version Current => typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public UpdateState State { get; private set; }

    /// <summary>The newer release, while one is known.</summary>
    public ReleaseInfo? Release { get; private set; }

    /// <summary>The download's progress, 0–1.</summary>
    public double Progress { get; private set; }

    /// <summary>Why the last check or update failed, for the settings card.</summary>
    public string? Problem { get; private set; }

    /// <summary><see cref="State"/>, <see cref="Progress"/> or <see cref="Release"/> changed, on the UI thread.</summary>
    public event Action? Changed;

    /// <summary>The automatic check: shortly after starting, then once a day, while Settings says so.</summary>
    public void Start()
    {
        ReportLastAttempt();
        _daily = _queue.CreateTimer();
        _daily.Interval = TimeSpan.FromHours(1);
        _daily.IsRepeating = true;
        _daily.Tick += (_, _) => CheckIfDue();
        _daily.Start();
        // Not at the very start: the usage CLIs and the first layout come first.
        var soon = _queue.CreateTimer();
        soon.Interval = TimeSpan.FromSeconds(30);
        soon.IsRepeating = false;
        soon.Tick += (_, _) => CheckIfDue();
        soon.Start();
    }

    private void CheckIfDue()
    {
        if (_settings.CheckForUpdates && State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing)
            && UpdateRules.IsDue(_settings.LastUpdateCheck, DateTimeOffset.Now))
        {
            _ = CheckAsync();
        }
    }

    public async Task CheckAsync()
    {
        if (State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing) return;
        Set(UpdateState.Checking);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UpdateRules.LatestRelease);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var release = UpdateRules.Parse(await response.Content.ReadAsStringAsync());
            _settings.LastUpdateCheck = DateTimeOffset.Now;
            _save();
            if (release is not null && UpdateRules.IsNewer(release, Current))
            {
                Release = release;
                Log.Info($"update: {release.Tag} is out (running {Current.ToString(3)})");
                Set(UpdateState.Available);
            }
            else
            {
                Release = null;
                Set(UpdateState.UpToDate);
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Error(e, "update check");
            Fail(e.Message);
        }
    }

    /// <summary>The Update button: download, check, hand over and quit.</summary>
    public async Task InstallAsync()
    {
        if (Release is not { } release || State is UpdateState.Downloading or UpdateState.Installing) return;
        var packaged = AppPackage.IsInstalled;
        var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        if (UpdateRules.AssetFor(release, rid, packaged) is not { } asset || !UpdateRules.IsTrusted(asset.Url))
        {
            Fail(Localization.L.Format("Update_NoFile", release.Tag));
            return;
        }
        var target = packaged ? Windows.ApplicationModel.Package.Current.InstalledLocation.Path : AppContext.BaseDirectory.TrimEnd('\\');
        var folder = Path.Combine(AppPaths.DataFolder, "updates");
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            var zip = Path.Combine(folder, asset.Name);
            Set(UpdateState.Downloading, progress: 0);
            await DownloadAsync(asset, zip);
            if (asset.Sha256 is { } expected)
            {
                await using var file = File.OpenRead(zip);
                var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file));
                if (actual != expected) throw new IOException(Localization.L.Get("Update_BadFile"));
            }
            Set(UpdateState.Installing);
            var unpacked = Path.Combine(folder, "new");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, unpacked));
            // The zips hold one ISLe8 folder.
            var source = Directory.Exists(Path.Combine(unpacked, "ISLe8")) ? Path.Combine(unpacked, "ISLe8") : unpacked;
            if (!File.Exists(Path.Combine(source, "ISLe8.exe"))) throw new IOException(Localization.L.Get("Update_BadFile"));
            var script = Path.Combine(folder, "apply.ps1");
            await File.WriteAllTextAsync(script, Script, System.Text.Encoding.UTF8);
            var family = packaged ? Windows.ApplicationModel.Package.Current.Id.FamilyName : "";
            var arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -Wait {Environment.ProcessId} "
                + $"-Source \"{source}\" -Target \"{target}\" -Family \"{family}\" -Log \"{Path.Combine(AppPaths.DataFolder, "update.log")}\"";
            Log.Info($"update: {release.Tag} downloaded, handing over to apply.ps1 for {target}");
            Spawn(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), arguments, outsidePackage: packaged);
            _quit();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Log.Error(e, "update");
            Fail(e.Message);
        }
    }

    private async Task DownloadAsync(ReleaseAsset asset, string path)
    {
        using var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        var shown = 0.0;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            var progress = total > 0 ? (double)done / total : 0;
            if (progress - shown >= 0.02)
            {
                shown = progress;
                Set(UpdateState.Downloading, progress);
            }
        }
    }

    /// <summary>A failed hand-over leaves its reason in update.log: shown once, after ISLe8 starts again.</summary>
    private void ReportLastAttempt()
    {
        var log = Path.Combine(AppPaths.DataFolder, "update.log");
        try
        {
            if (!File.Exists(log)) return;
            var text = File.ReadAllText(log).Trim();
            File.Delete(log);
            if (text.Length == 0) return;
            Log.Info($"update: the last attempt said: {text}");
            Problem = text.Split('\n')[^1].Trim();
            State = UpdateState.Failed;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Fail(string problem)
    {
        Problem = problem;
        Set(UpdateState.Failed);
    }

    private void Set(UpdateState state, double progress = 0) => _queue.TryEnqueue(() =>
    {
        State = state;
        Progress = progress;
        if (state != UpdateState.Failed) Problem = null;
        Changed?.Invoke();
    });

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ISLe8-Windows/{Current.ToString(3)}");
        return client;
    }

    /// <summary>
    /// Waits for ISLe8 to quit, swaps the folder (the old one stays as "-old" until the new one is in),
    /// registers the installed app's package again (an update in place, so its settings and permissions
    /// stay), and starts ISLe8. On any failure the old folder goes back, and the reason goes to the log.
    /// </summary>
    private const string Script = """
        param([int]$Wait, [string]$Source, [string]$Target, [string]$Family, [string]$Log)
        $ErrorActionPreference = 'Stop'
        try { Wait-Process -Id $Wait -Timeout 30 -ErrorAction SilentlyContinue } catch { }
        Start-Sleep -Milliseconds 500
        $old = "$Target-old"
        $moved = $false
        try {
            if (Test-Path $old) { Remove-Item $old -Recurse -Force }
            for ($try = 1; ; $try++) {
                try { Rename-Item $Target (Split-Path $old -Leaf); break }
                catch { if ($try -ge 10) { throw } else { Start-Sleep -Seconds 1 } }
            }
            $moved = $true
            Move-Item $Source $Target
            if ($Family) { Add-AppxPackage -Register (Join-Path $Target 'AppxManifest.xml') -ForceApplicationShutdown }
            Remove-Item $old -Recurse -Force -ErrorAction SilentlyContinue
        } catch {
            Add-Content $Log $_.Exception.Message
            if ($moved) {
                if (Test-Path $Target) { Remove-Item $Target -Recurse -Force -ErrorAction SilentlyContinue }
                Rename-Item $old (Split-Path $Target -Leaf) -ErrorAction SilentlyContinue
                if ($Family) { Add-AppxPackage -Register (Join-Path $Target 'AppxManifest.xml') -ForceApplicationShutdown -ErrorAction SilentlyContinue }
            }
        }
        if ($Family) { Start-Process "shell:AppsFolder\$Family!App" } else { Start-Process (Join-Path $Target 'ISLe8.exe') }
        """;

    // MARK: Starting the script outside the package

    /// <summary>
    /// The installed app's children run inside its package, and re-registering the package would stop
    /// them with it; the script is started outside it (PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY).
    /// </summary>
    private static unsafe void Spawn(string exe, string arguments, bool outsidePackage)
    {
        if (!outsidePackage)
        {
            using var _ = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true });
            return;
        }
        nuint size = 0;
        InitializeProcThreadAttributeList(null, 1, 0, &size);
        var list = (void*)Marshal.AllocHGlobal((nint)size);
        try
        {
            if (!InitializeProcThreadAttributeList(list, 1, 0, &size)) throw new System.ComponentModel.Win32Exception();
            uint policy = 0x04; // PROCESS_CREATION_DESKTOP_APP_BREAKAWAY_OVERRIDE
            if (!UpdateProcThreadAttribute(list, 0, 0x00020012 /* PROC_THREAD_ATTRIBUTE_DESKTOP_APP_POLICY */, &policy, sizeof(uint), null, null))
            {
                throw new System.ComponentModel.Win32Exception();
            }
            var startup = new StartupInfoEx { AttributeList = list };
            startup.Info.Size = (uint)sizeof(StartupInfoEx);
            var command = $"\"{exe}\" {arguments}\0".ToCharArray();
            fixed (char* line = command)
            {
                ProcessInformation process;
                const uint flags = 0x00080000 /* EXTENDED_STARTUPINFO_PRESENT */ | 0x08000000 /* CREATE_NO_WINDOW */;
                if (!CreateProcessW(null, line, null, null, false, flags, null, null, &startup, &process)) throw new System.ComponentModel.Win32Exception();
                CloseHandle(process.Process);
                CloseHandle(process.Thread);
            }
            DeleteProcThreadAttributeList(list);
        }
        finally
        {
            Marshal.FreeHGlobal((nint)list);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public uint Size;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, Reserved2;
        public nint Reserved3, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct StartupInfoEx
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

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool InitializeProcThreadAttributeList(void* list, int count, uint flags, nuint* size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool UpdateProcThreadAttribute(void* list, uint flags, nuint attribute, void* value, nuint size, void* previous, nuint* returned);

    [LibraryImport("kernel32.dll")]
    private static unsafe partial void DeleteProcThreadAttributeList(void* list);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateProcessW(string? application, char* commandLine, void* processAttributes, void* threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, void* environment, string? directory, StartupInfoEx* startup, ProcessInformation* information);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
