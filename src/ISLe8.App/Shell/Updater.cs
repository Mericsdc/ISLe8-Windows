using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.Settings;
using ISLe8.Core.Updates;
using ISLe8.UI;
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
/// (started by Task Scheduler for the installed app, outside its package) that waits for ISLe8 to quit, puts the new files where the old ones were (keeping the old ones
/// until the new ones are in), registers the package again for the installed app, and starts ISLe8.
/// Nothing is downloaded or run without the button.
/// </summary>
internal sealed class Updater
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
        _daily.OnTick(() => CheckIfDue());
        _daily.Start();
        // Not at the very start: the usage CLIs and the first layout come first.
        var soon = _queue.CreateTimer();
        soon.Interval = TimeSpan.FromSeconds(30);
        soon.IsRepeating = false;
        soon.OnTick(() =>
        {
            ClearDownloads();
            CheckIfDue();
        });
        soon.Start();
    }

    /// <summary>The last update's download and unpacked files, once ISLe8 runs from where they were copied to.</summary>
    private void ClearDownloads()
    {
        var folder = Path.Combine(AppPaths.DataFolder, "updates");
        if (State is UpdateState.Downloading or UpdateState.Installing || !Directory.Exists(folder)) return;
        _ = Task.Run(() =>
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Tried again next start, and before the next download.
            }
        });
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
            var id = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var log = Path.Combine(AppPaths.DataFolder, "update.log");
            if (!packaged)
            {
                // The new ISLe8 finishes the job itself (UpdateFinisher), working in the updates folder, not the one it replaces.
                var plan = Path.Combine(folder, "finish.txt");
                await File.WriteAllLinesAsync(plan, [id, source, target, log]);
                Log.Info($"update: {release.Tag} downloaded, handing over to the new ISLe8 for {target}");
                using var finisher = Process.Start(new ProcessStartInfo(Path.Combine(source, "ISLe8.exe"))
                {
                    ArgumentList = { UpdateFinisher.Argument, plan },
                    UseShellExecute = false,
                    WorkingDirectory = folder,
                });
                _quit();
                return;
            }
            var script = Path.Combine(folder, "apply.ps1");
            await File.WriteAllTextAsync(script, Script, System.Text.Encoding.UTF8);
            var family = Windows.ApplicationModel.Package.Current.Id.FamilyName;
            // One line per value, read back as they are: no quoting to get wrong.
            await File.WriteAllLinesAsync(Path.Combine(folder, "apply.txt"), [id, source, target, family, log]);
            Log.Info($"update: {release.Tag} downloaded, handing over to apply.ps1 for {target}");
            HandOver(script, packaged);
            _quit();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Log.Error(e, "update");
            Fail(e.Message);
        }
    }

    /// <summary>
    /// Windows lets an unsigned package in only with Developer Mode on (Settings › System › For developers),
    /// which is what <see cref="MoveToPackageAsync"/> needs.
    /// </summary>
    public static bool DeveloperModeOn
    {
        get
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
            return key?.GetValue("AllowDevelopmentWithoutDevLicense") is int on && on != 0;
        }
    }

    /// <summary>
    /// Settings › Notifications, on the zip's ISLe8 (Windows only): the latest release's package is downloaded and
    /// checked like an update, unpacked to stay in the data folder, and registered by a script once this ISLe8
    /// quits; the installed ISLe8 then starts, with the same settings and history (they live in the data folder
    /// for both). The zip's own login entry goes, so only the installed one starts at login. On failure the zip's
    /// ISLe8 starts again and says why.
    /// </summary>
    public async Task MoveToPackageAsync()
    {
        if (AppPackage.IsInstalled || State is UpdateState.Checking or UpdateState.Downloading or UpdateState.Installing) return;
        Set(UpdateState.Checking);
        var folder = Path.Combine(AppPaths.DataFolder, "updates");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UpdateRules.LatestRelease);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
            if (UpdateRules.Parse(await response.Content.ReadAsStringAsync()) is not { } release
                || UpdateRules.AssetFor(release, rid, packaged: true) is not { } asset || !UpdateRules.IsTrusted(asset.Url))
            {
                Fail(Localization.L.Format("Update_NoFile", "latest"));
                return;
            }
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            var zip = Path.Combine(folder, asset.Name);
            Set(UpdateState.Downloading, progress: 0);
            await DownloadAsync(asset, zip);
            if (asset.Sha256 is { } expected)
            {
                await using var file = File.OpenRead(zip);
                if (Convert.ToHexStringLower(await SHA256.HashDataAsync(file)) != expected) throw new IOException(Localization.L.Get("Update_BadFile"));
            }
            Set(UpdateState.Installing);
            var unpacked = Path.Combine(folder, "new");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, unpacked));
            var source = Directory.Exists(Path.Combine(unpacked, "ISLe8")) ? Path.Combine(unpacked, "ISLe8") : unpacked;
            if (!File.Exists(Path.Combine(source, "AppxManifest.xml"))) throw new IOException(Localization.L.Get("Update_BadFile"));
            var script = Path.Combine(folder, "move.ps1");
            await File.WriteAllTextAsync(script, MoveScript, System.Text.Encoding.UTF8);
            await File.WriteAllLinesAsync(Path.Combine(folder, "move.txt"),
                [Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), source, Path.Combine(AppPaths.DataFolder, "package"),
                 Path.Combine(AppPaths.DataFolder, "update.log"), Path.Combine(AppContext.BaseDirectory, "ISLe8.exe"), LoginItem.Name]);
            Log.Info($"package: {release.Tag} downloaded, handing over to move.ps1");
            HandOver(script, packaged: false);
            _quit();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or System.ComponentModel.Win32Exception)
        {
            Log.Error(e, "package");
            Fail(e.Message);
        }
    }

    /// <summary>Waits for the zip's ISLe8 to quit, puts the package in place, registers it and starts it; on failure, the zip's ISLe8 again.</summary>
    private const string MoveScript = """
        $ErrorActionPreference = 'Stop'
        $Wait, $Source, $Target, $Log, $Zip, $RunName = Get-Content (Join-Path $PSScriptRoot 'move.txt') -Encoding UTF8
        try { Wait-Process -Id ([int]$Wait) -Timeout 30 -ErrorAction SilentlyContinue } catch { }
        Start-Sleep -Milliseconds 500
        try {
            if (Test-Path $Target) { Remove-Item $Target -Recurse -Force }
            Move-Item $Source $Target
            Add-AppxPackage -Register (Join-Path $Target 'AppxManifest.xml')
        } catch {
            Add-Content $Log $_.Exception.Message
            Start-Process $Zip -WorkingDirectory (Split-Path $Zip)
            exit
        }
        # Installed: whatever happens next, the zip's ISLe8 isn't wanted again. Open at login goes over to the
        # package's startup task, which only the installed ISLe8 can turn on (StartAtLogin.Repair).
        $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        if (Get-ItemProperty $run -Name $RunName -ErrorAction SilentlyContinue) {
            Set-Content (Join-Path (Split-Path $Log) 'login-from-zip') '' -ErrorAction SilentlyContinue
            Remove-ItemProperty $run -Name $RunName -ErrorAction SilentlyContinue
        }
        $family = (Get-AppxPackage com.lrxe8.isle8 | Sort-Object Version -Descending | Select-Object -First 1).PackageFamilyName
        if ($family) { Start-Process "shell:AppsFolder\$family!App" } else { Add-Content $Log 'ISLe8 is installed, but Windows did not list it' }
        """;

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

    private void Set(UpdateState state, double progress = 0) => _queue.Post(() =>
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
        $ErrorActionPreference = 'Stop'
        $Wait, $Source, $Target, $Family, $Log = Get-Content (Join-Path $PSScriptRoot 'apply.txt') -Encoding UTF8
        $trace = Join-Path $PSScriptRoot 'apply.log'
        # Started by Task Scheduler for the installed app: the task has done its job.
        if ($Family) { schtasks.exe /Delete /TN 'ISLe8 update' /F *> $null }
        function Trace([string]$step) { Add-Content $trace "$(Get-Date -Format 'HH:mm:ss.fff') $step" }
        Add-Type -Namespace Isle8 -Name Identity -MemberDefinition '[DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder name);'
        $length = 0
        Trace "started; package identity: $([Isle8.Identity]::GetCurrentPackageFullName([ref]$length, $null) -ne 15700)"
        try { Wait-Process -Id ([int]$Wait) -Timeout 30 -ErrorAction SilentlyContinue } catch { }
        Trace "ISLe8 gone"
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
            Trace "files in place"
            if ($Family) { Add-AppxPackage -Register (Join-Path $Target 'AppxManifest.xml') -ForceApplicationShutdown }
            Trace "registered"
            Remove-Item $old -Recurse -Force -ErrorAction SilentlyContinue
        } catch {
            Trace "failed: $($_.Exception.Message)"
            Add-Content $Log $_.Exception.Message
            if ($moved) {
                if (Test-Path $Target) { Remove-Item $Target -Recurse -Force -ErrorAction SilentlyContinue }
                Rename-Item $old (Split-Path $Target -Leaf) -ErrorAction SilentlyContinue
                if ($Family) { Add-AppxPackage -Register (Join-Path $Target 'AppxManifest.xml') -ForceApplicationShutdown -ErrorAction SilentlyContinue }
            }
        }
        if ($Family) { Start-Process "shell:AppsFolder\$Family!App" } else { Start-Process (Join-Path $Target 'ISLe8.exe') -WorkingDirectory $Target }
        """;

    // MARK: Starting the script outside the package

    /// <summary>
    /// The installed app's children run with its package identity, and registering the package again
    /// stops every process that has it, the script too, halfway (seen: the files in place, nothing
    /// registered). So for the installed app the script is started by Task Scheduler, as the user,
    /// outside the package; the zip's ISLe8 starts it directly, working in the updates folder. Up to 0.2.7 it
    /// inherited ISLe8's own, which is the zip folder when Explorer starts ISLe8, and that folder can't be
    /// renamed while a process works in it: every zip update failed and the old ISLe8 came back.
    /// </summary>
    private static void HandOver(string script, bool packaged)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var command = $"\"{powershell}\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"";
        if (!packaged)
        {
            using var _ = Process.Start(new ProcessStartInfo(powershell, $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(script) });
            return;
        }
        Schtasks($"/Create /TN \"ISLe8 update\" /TR \"{command.Replace("\"", "\\\"")}\" /SC ONCE /ST 00:00 /F /RL LIMITED");
        Schtasks("/Run /TN \"ISLe8 update\"");
    }

    private static void Schtasks(string arguments)
    {
        var exe = Path.Combine(Environment.SystemDirectory, "schtasks.exe");
        using var process = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true })
            ?? throw new IOException("schtasks");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(15000);
        if (process.ExitCode != 0) throw new IOException($"schtasks {process.ExitCode}: {error.Trim()}");
    }
}
