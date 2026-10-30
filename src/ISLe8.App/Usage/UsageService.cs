using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ISLe8.Core.Diagnostics;
using ISLe8.Core.Island;
using ISLe8.Core.Settings;
using ISLe8.Core.Usage;
using ISLe8.UI;
using Microsoft.UI.Dispatching;
using Windows.System.Power;

namespace ISLe8.Usage;

/// <summary>
/// How much of each AI service's usage limit is used, asked through each vendor's official CLI
/// (UsageModule.swift and UsagePolling.swift, from LimitBar LRX). No token, cookie or credential is
/// ever opened: signing in stays inside the CLI. Codex and Antigravity are asked every 5 minutes with
/// the island closed, every minute while it is open, every 15 in saver mode or Windows' energy saver;
/// Claude at most every 20 minutes, and every 30 s at most on a manual refresh. Codex's own session
/// logs are read for free whenever Codex writes them. Nothing runs while Windows sleeps, failures
/// back off up to 30 minutes, and every CLI runs off the UI thread.
/// </summary>
internal sealed class UsageService : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> ClaudeEnvironment = new Dictionary<string, string>
    {
        // Not CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: since Claude Code 2.1 it also skips the usage request.
        ["DISABLE_UPDATES"] = "1",
        ["DISABLE_TELEMETRY"] = "1",
        ["DISABLE_ERROR_REPORTING"] = "1",
    };

    /// <summary>
    /// agy otherwise updates itself on a run: it downloads the new build and starts a copy of itself in a
    /// console of its own, a terminal window that comes and goes; the job then kills it before the swap,
    /// so it began again every poll. On the Mac the sandbox keeps it from writing its own folder.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AgyEnvironment = new Dictionary<string, string>
    {
        ["AGY_CLI_DISABLE_AUTO_UPDATE"] = "1",
    };

    /// <summary>Settings files ignored (so none of their hooks run, ISLe8's own included), no MCP server, no transcript.</summary>
    private static readonly string[] ClaudeIsolation =
        ["--restricted", "--strict-mcp-config", "--no-session-persistence", "--settings", """{"disableAllHooks":true,"language":"English"}"""];

    private readonly AppSettings _settings;
    private readonly DispatcherQueue _queue;
    private readonly IIslandClock _clock;
    private readonly string _cacheFile;
    private readonly Dictionary<UsageVendor, UsageSnapshot> _snaps = [];
    private readonly UsageWarnings _warnings = new();
    private readonly Dictionary<UsageVendor, DateTimeOffset> _lastPoll = [];
    private readonly Dictionary<UsageVendor, int> _failures = [];
    private readonly Dictionary<UsageVendor, CancellationTokenSource> _jobs = [];
    private readonly HashSet<UsageVendor> _asking = [];
    private readonly CodexLogs _logs = new(Path.Combine(UsageCli.Home, ".codex", "sessions"));
    private HashSet<UsageVendor> _running = [];
    private IDisposable? _tick;
    private FileSystemWatcher? _watcher;
    private IDisposable? _localRead;
    private DateTimeOffset _lastLocalRead;
    private bool _readingLocal;
    private bool _started;
    private bool _visible;
    private bool _asleep;
    private bool _cacheLoaded;

    // What the fetchers remember between runs; each is touched by its own vendor's job only.
    private DateTimeOffset _lastClaudeUsage = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAccountCheck = DateTimeOffset.MinValue;
    private (bool SignedIn, string Plan)? _claudeAccount;
    private DateTimeOffset _lastCodexServer = DateTimeOffset.MinValue;

    public UsageService(AppSettings settings, DispatcherQueue queue, IIslandClock clock, string dataFolder)
    {
        _settings = settings;
        _queue = queue;
        _clock = clock;
        _cacheFile = Path.Combine(dataFolder, "usage", "state.json");
    }

    /// <summary>One per enabled vendor whose CLI is installed or that has data, in <see cref="UsageVendors.All"/> order.</summary>
    public IReadOnlyList<UsageSnapshot> Snapshots { get; private set; } = [];

    /// <summary>A CLI is being asked; the free checks between don't count.</summary>
    public bool IsRefreshing => _asking.Count > 0;

    public event Action? Changed;

    /// <summary>A window crossed 90 % (once each time it crosses).</summary>
    public event Action<UsageSnapshot, UsageWindow>? LimitWarning;

    public void Start()
    {
        if (_started) return;
        _started = true;
        PowerManager.EnergySaverStatusChanged += OnEnergySaverChanged;
        Reconcile();
    }

    public void Dispose()
    {
        PowerManager.EnergySaverStatusChanged -= OnEnergySaverChanged;
        _started = false;
        Reconcile();
    }

    /// <summary>Settings › Agents › Usage limits changed.</summary>
    public void ApplySettings() => Reconcile();

    /// <summary>The island opened or closed. Open asks Codex and Antigravity every minute; opening checks at once.</summary>
    public void SetVisible(bool visible)
    {
        if (_visible == visible) return;
        _visible = visible;
        if (visible) Poll(olderThan: TimeSpan.FromSeconds(30));
    }

    /// <summary>A manual refresh: every vendor now, but Claude's /usage and Codex's app-server at most every 30 s.</summary>
    public void Refresh() => Poll(force: true);

    /// <summary>WM_POWERBROADCAST: nothing runs while Windows sleeps.</summary>
    public void SetSleeping(bool sleeping)
    {
        if (_asleep == sleeping) return;
        _asleep = sleeping;
        Reconcile();
    }

    // MARK: State

    private bool IsActive => _started && _settings.UsageEnabled;
    private bool IsRunning => IsActive && !_asleep && Enabled.Count > 0;

    private HashSet<UsageVendor> Enabled => UsageVendors.All.Where(vendor => !_settings.UsageDisabledVendors.Contains(UsageVendors.Id(vendor))).ToHashSet();

    private static bool EnergySaverOn
    {
        get
        {
            try
            {
                return PowerManager.EnergySaverStatus == EnergySaverStatus.On;
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                return false;
            }
        }
    }

    private bool LowEnergy => _settings.UsageSaverMode || EnergySaverOn;

    private void OnEnergySaverChanged(object? sender, object e) => _queue.Post(ConfigureMonitoring);

    /// <summary>Brings jobs, timers and the watcher in line with the settings, and asks the vendors that just started running.</summary>
    private void Reconcile()
    {
        if (IsActive) LoadCacheOnce();
        if (!_settings.UsageEnabled) Clear();
        var running = IsRunning ? Enabled : [];
        foreach (var vendor in UsageVendors.All.Where(vendor => !running.Contains(vendor))) Cancel(vendor);
        var previous = _running;
        _running = running;
        ConfigureMonitoring();
        Publish();
        if (running.Count == 0) return;
        if (previous.Count == 0) Poll(force: true);
        else foreach (var vendor in running.Where(vendor => !previous.Contains(vendor))) Schedule(vendor, force: true);
    }

    private void Clear()
    {
        _snaps.Clear();
        _warnings.Clear();
        _lastPoll.Clear();
        _failures.Clear();
        _cacheLoaded = false;
    }

    // MARK: Timer and watcher

    private void ConfigureMonitoring()
    {
        _tick?.Dispose();
        _tick = null;
        if (!IsRunning)
        {
            StopWatching();
            return;
        }
        RebuildWatcher();
        // Only cheap checks run on this tick; every live question keeps its own minimum interval.
        void Tick()
        {
            _tick = _clock.After(TimeSpan.FromSeconds(LowEnergy ? 60 : 30), () =>
            {
                RebuildWatcher();
                Poll();
                Tick();
            });
        }
        Tick();
    }

    /// <summary>Codex's session logs: a new quota line is read within about two seconds, with no scanning.</summary>
    private void RebuildWatcher()
    {
        var wanted = IsRunning && Enabled.Contains(UsageVendor.Codex) && Directory.Exists(_logs.Root);
        if (wanted == (_watcher is not null)) return;
        if (!wanted)
        {
            StopWatching();
            return;
        }
        try
        {
            _watcher = new FileSystemWatcher(_logs.Root, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            };
            _watcher.Changed += (_, _) => _queue.Post(ScheduleLocalRead);
            _watcher.Created += (_, _) => _queue.Post(ScheduleLocalRead);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            Log.Error(e, "codex sessions watcher");
            _watcher = null;
        }
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _localRead?.Dispose();
        _localRead = null;
    }

    /// <summary>Codex writes a log several times in a row; it is read once it settles, at most every 2 s.</summary>
    private void ScheduleLocalRead()
    {
        if (!IsRunning) return;
        _localRead?.Dispose();
        var delay = TimeSpan.FromSeconds(Math.Max(0.6, 2 - (_clock.Now - _lastLocalRead).TotalSeconds));
        _localRead = _clock.After(delay, () =>
        {
            _localRead = null;
            _lastLocalRead = _clock.Now;
            _ = ReadLocalAsync();
        });
    }

    private async Task ReadLocalAsync()
    {
        if (_readingLocal || !Enabled.Contains(UsageVendor.Codex)) return;
        if (_snaps.GetValueOrDefault(UsageVendor.Codex)?.State is UsageState.Missing or UsageState.Untrusted or UsageState.SignedOut) return;
        _readingLocal = true;
        var now = _clock.Now;
        var value = await Task.Run(() => _logs.Latest(now));
        _readingLocal = false;
        if (value is null || !IsRunning) return;
        var previous = _snaps.GetValueOrDefault(UsageVendor.Codex);
        if (previous?.State is UsageState.SignedOut or UsageState.Untrusted or UsageState.Missing) return;
        if (value.MeasuredAt is not { } time || time <= (previous?.MeasuredAt ?? DateTimeOffset.MinValue)) return;
        Apply(UsageVendor.Codex, value with
        {
            Plan = value.Plan.Length > 0 ? value.Plan : previous?.Plan ?? "",
            ResetCredits = previous?.ResetCredits ?? 0,
            ResetCreditsExpire = previous?.ResetCreditsExpire,
        });
    }

    // MARK: Polling

    /// <summary><paramref name="olderThan"/> checks the free sources sooner (opening the island); a live question keeps its own minimum either way.</summary>
    private void Poll(bool force = false, TimeSpan? olderThan = null)
    {
        if (!IsRunning) return;
        var now = _clock.Now;
        foreach (var vendor in UsageVendors.All.Where(vendor => Enabled.Contains(vendor) && !_jobs.ContainsKey(vendor)))
        {
            var age = now - _lastPoll.GetValueOrDefault(vendor, DateTimeOffset.MinValue);
            if (!force)
            {
                var state = _snaps.GetValueOrDefault(vendor)?.State;
                if (state is UsageState.Missing or UsageState.Untrusted or UsageState.SignedOut)
                {
                    if (age < UsagePolicy.RecheckMissing) continue;
                }
                else if (_failures.GetValueOrDefault(vendor) is var failures and > 0 && age < UsagePolicy.RetryDelay(failures))
                {
                    continue;
                }
                // Antigravity has no free source: every poll is a live question.
                var interval = vendor == UsageVendor.Gemini ? RemoteInterval(vendor) : olderThan ?? TimeSpan.FromSeconds(LowEnergy ? 60 : 30);
                if (age < interval) continue;
            }
            Schedule(vendor, force);
        }
    }

    private TimeSpan RemoteInterval(UsageVendor vendor) => UsagePolicy.RemoteInterval(vendor, LowEnergy, _visible);

    private void Schedule(UsageVendor vendor, bool force)
    {
        if (!IsRunning || !Enabled.Contains(vendor) || _jobs.ContainsKey(vendor)) return;
        var job = new CancellationTokenSource();
        _jobs[vendor] = job;
        var previous = _snaps.GetValueOrDefault(vendor);
        var gap = RemoteInterval(vendor);
        var now = _clock.Now;
        void Asking() => _queue.Post(() =>
        {
            if (_jobs.GetValueOrDefault(vendor) != job) return;
            if (_asking.Add(vendor)) Changed?.Invoke();
        });
        _ = Task.Run(async () =>
        {
            UsageSnapshot? snapshot = null;
            try
            {
                snapshot = await FetchAsync(vendor, previous, force, gap, now, Asking, job.Token);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Log.Error(e, $"usage {UsageVendors.Id(vendor)}");
            }
            catch (OperationCanceledException)
            {
            }
            _queue.Post(() => Finished(vendor, job, snapshot));
        });
    }

    private void Finished(UsageVendor vendor, CancellationTokenSource job, UsageSnapshot? snapshot)
    {
        if (_jobs.GetValueOrDefault(vendor) != job) return;
        _jobs.Remove(vendor);
        var wasAsking = _asking.Remove(vendor);
        job.Dispose();
        if (wasAsking) Changed?.Invoke();
        if (!IsRunning || !Enabled.Contains(vendor)) return;
        _lastPoll[vendor] = _clock.Now;
        if (snapshot is null) return;
        _failures[vendor] = snapshot.State == UsageState.Failed ? _failures.GetValueOrDefault(vendor) + 1 : 0;
        Apply(vendor, snapshot);
    }

    private void Cancel(UsageVendor vendor)
    {
        if (_jobs.Remove(vendor, out var job))
        {
            job.Cancel();
            job.Dispose();
        }
        if (_asking.Remove(vendor)) Changed?.Invoke();
    }

    // MARK: Fetching (off the UI thread)

    private async Task<UsageSnapshot?> FetchAsync(UsageVendor vendor, UsageSnapshot? previous, bool force, TimeSpan gap, DateTimeOffset now, Action asking, CancellationToken cancel)
    {
        var (found, binary) = UsageCli.Find(vendor);
        if (found == UsageCli.Found.Missing) return UsageSnapshot.Status(vendor, UsageState.Missing);
        if (found == UsageCli.Found.Untrusted) return UsageSnapshot.Status(vendor, UsageState.Untrusted, UsageError.Untrusted);
        return vendor switch
        {
            UsageVendor.Claude => await ClaudeAsync(binary!, previous, force, now, asking, cancel),
            UsageVendor.Codex => await CodexAsync(binary!, previous, force, gap, now, asking, cancel),
            _ => await GeminiAsync(binary!, now, asking, cancel),
        };
    }

    private async Task<UsageSnapshot?> ClaudeAsync(string claude, UsageSnapshot? previous, bool force, DateTimeOffset now, Action asking, CancellationToken cancel)
    {
        // An unconfirmed sign-in is checked every 5 minutes, a confirmed one every 30.
        var sinceCheck = now - _lastAccountCheck;
        if (force || sinceCheck >= TimeSpan.FromMinutes(30) || (_claudeAccount?.SignedIn != true && sinceCheck >= UsagePolicy.RecheckMissing))
        {
            asking();
            var status = await UsageCli.RunAsync(claude, ["auth", "status", "--json"], TimeSpan.FromSeconds(20), ClaudeEnvironment, cancel);
            if (cancel.IsCancellationRequested) return null;
            _lastAccountCheck = now;
            if (status is not null && UsageParse.ClaudeAccount(status.Text) is { } account) _claudeAccount = account;
        }
        if (_claudeAccount?.SignedIn == false) return UsageSnapshot.Status(UsageVendor.Claude, UsageState.SignedOut);
        UsageSnapshot? read = null;
        var sinceLive = now - _lastClaudeUsage;
        if (force ? sinceLive >= UsagePolicy.ManualGap : sinceLive >= TimeSpan.FromMinutes(20))
        {
            asking();
            var output = await UsageCli.RunAsync(claude, ["-p", "/usage", "--output-format", "json", "--max-turns", "1", .. ClaudeIsolation],
                TimeSpan.FromSeconds(40), ClaudeEnvironment, cancel);
            if (cancel.IsCancellationRequested) return null;
            _lastClaudeUsage = now;
            read = output is null ? UsageSnapshot.Status(UsageVendor.Claude, UsageState.Failed, UsageError.Timeout) : ClaudeAnswer(output, now);
        }
        if (read is null) return previous ?? UsageSnapshot.Status(UsageVendor.Claude, UsageState.Failed, UsageError.Waiting);
        return read with { Plan = _claudeAccount?.Plan is { Length: > 0 } plan ? plan : read.Plan };
    }

    private static UsageSnapshot ClaudeAnswer(UsageCli.Output output, DateTimeOffset now)
    {
        try
        {
            using var document = JsonDocument.Parse(output.Text);
            if (document.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String) return UsageParse.ClaudeUsage(result.GetString()!, now);
        }
        catch (JsonException)
        {
        }
        var head = output.Text.Length > 2000 ? output.Text[..2000] : output.Text;
        return UsageParse.SignedOutText(head)
            ? UsageSnapshot.Status(UsageVendor.Claude, UsageState.SignedOut)
            : UsageSnapshot.Status(UsageVendor.Claude, UsageState.Failed, UsageError.NoData, output.Code);
    }

    private static async Task<UsageSnapshot?> GeminiAsync(string agy, DateTimeOffset now, Action asking, CancellationToken cancel)
    {
        asking();
        var output = await UsageCli.RunAsync(agy, ["-p", "/quota", "--output-format", "json", "--print-timeout", "30s"], TimeSpan.FromSeconds(40), AgyEnvironment, cancel);
        if (cancel.IsCancellationRequested) return null;
        return output is null ? UsageSnapshot.Status(UsageVendor.Gemini, UsageState.Failed, UsageError.Timeout) : UsageParse.Gemini(output.Text, output.Code, now);
    }

    private async Task<UsageSnapshot?> CodexAsync(string codex, UsageSnapshot? previous, bool force, TimeSpan gap, DateTimeOffset now, Action asking, CancellationToken cancel)
    {
        var logs = _logs.Latest(now);
        var remoteAge = now - _lastCodexServer;
        // The session log is free; the app-server only when it is due (a manual refresh: at most every 30 s).
        if ((!force && remoteAge < gap) || (force && remoteAge < UsagePolicy.ManualGap))
        {
            if (logs is not null && (logs.MeasuredAt ?? DateTimeOffset.MinValue) > (previous?.MeasuredAt ?? DateTimeOffset.MinValue)) return logs;
            return previous?.State == UsageState.Failed ? null : previous ?? logs;
        }
        asking();
        var version = typeof(UsageService).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        var result = await UsageCli.RpcAsync(codex, ["app-server"],
            new JsonObject { ["clientInfo"] = new JsonObject { ["name"] = "lrxe8_isle8", ["version"] = version } },
            "initialized", "account/rateLimits/read", TimeSpan.FromSeconds(20), cancel);
        if (cancel.IsCancellationRequested) return null;
        _lastCodexServer = now;
        if (result is null)
        {
            var login = await UsageCli.RunAsync(codex, ["login", "status"], TimeSpan.FromSeconds(15), null, cancel);
            if (login is not null && UsageParse.SignedOutText(login.Text)) return UsageSnapshot.Status(UsageVendor.Codex, UsageState.SignedOut);
            return logs is not null
                ? logs with { State = UsageState.Failed, Error = UsageError.ServerFallback }
                : UsageSnapshot.Status(UsageVendor.Codex, UsageState.Failed, UsageError.NoData);
        }
        using var document = JsonDocument.Parse(result.ToJsonString());
        return UsageParse.CodexServer(document.RootElement, now);
    }

    // MARK: Snapshots

    private void Apply(UsageVendor vendor, UsageSnapshot incoming)
    {
        var next = incoming;
        var old = _snaps.GetValueOrDefault(vendor);
        // A failure keeps the last numbers beside its error.
        if (next.State == UsageState.Failed && old is not null && next.Windows.Count == 0 && old.Windows.Count > 0)
        {
            next = next with { Windows = old.Windows, Bar = old.Bar, MeasuredAt = old.MeasuredAt, Source = old.Source, Plan = old.Plan };
        }
        if (next.Plan.Length == 0 && next.State == UsageState.Ok) next = next with { Plan = old?.Plan ?? "" };
        if (next.SameAs(old)) return;
        _snaps[vendor] = next;
        SaveCache();
        Publish();
        foreach (var window in _warnings.Read(next, _clock.Now)) LimitWarning?.Invoke(next, window);
    }

    private void Publish()
    {
        var enabled = Enabled;
        var visible = UsageVendors.All
            .Where(vendor => enabled.Contains(vendor) && _snaps.TryGetValue(vendor, out var snapshot) && (snapshot.State != UsageState.Missing || snapshot.Windows.Count > 0))
            .Select(vendor => _snaps[vendor])
            .ToList();
        if (visible.Count == Snapshots.Count && visible.Zip(Snapshots).All(pair => pair.First.SameAs(pair.Second))) return;
        Snapshots = visible;
        Changed?.Invoke();
    }

    // MARK: Cache

    private void LoadCacheOnce()
    {
        if (_cacheLoaded) return;
        _cacheLoaded = true;
        try
        {
            if (!File.Exists(_cacheFile)) return;
            var cached = JsonSerializer.Deserialize(File.ReadAllText(_cacheFile), UsageJsonContext.Default.DictionaryStringUsageSnapshot);
            foreach (var (key, value) in cached ?? [])
            {
                if (UsageVendors.Parse(key) is not { } vendor || value.Vendor != vendor || _snaps.ContainsKey(vendor)) continue;
                _snaps[vendor] = value;
                // Already over 90 % last time: that was warned about then.
                _warnings.Read(value, _clock.Now, fromCache: true);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Error(e, "usage cache");
        }
    }

    private void SaveCache()
    {
        var all = _snaps.ToDictionary(pair => UsageVendors.Id(pair.Key), pair => pair.Value);
        var json = JsonSerializer.Serialize(all, UsageJsonContext.Default.DictionaryStringUsageSnapshot);
        var file = _cacheFile;
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var temporary = file + ".new";
                File.WriteAllText(temporary, json);
                File.Move(temporary, file, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error(e, "usage cache");
            }
        });
    }

    /// <summary>
    /// Codex writes <c>rate_limits</c> into its own session logs whenever it talks to the server
    /// (UsageCodexLogs). The first time, only the last 256 KiB of each of the eight newest logs of today
    /// and yesterday (UTC) is read; after that only what was appended since, from the last whole line.
    /// Only quota lines are parsed; conversation content is never kept.
    /// </summary>
    private sealed class CodexLogs(string root)
    {
        private readonly Lock _gate = new();
        private const int TailBytes = 262_144;
        private readonly Dictionary<string, (DateTime Modified, long Size, long Offset, UsageSnapshot? Snapshot)> _memo = new(StringComparer.OrdinalIgnoreCase);

        public string Root { get; } = root;

        public UsageSnapshot? Latest(DateTimeOffset now)
        {
            lock (_gate)
            {
                var files = Recent(now);
                foreach (var gone in _memo.Keys.Except(files.Select(file => file.FullName), StringComparer.OrdinalIgnoreCase).ToList()) _memo.Remove(gone);
                UsageSnapshot? best = null;
                foreach (var file in files)
                {
                    UsageSnapshot? candidate;
                    if (_memo.TryGetValue(file.FullName, out var known) && known.Modified == file.LastWriteTimeUtc && known.Size == file.Length)
                    {
                        candidate = known.Snapshot;
                    }
                    else
                    {
                        // A log that grew is read from where the last read stopped; one that shrank or is new, its tail.
                        var appended = known.Modified != default && file.Length >= known.Size;
                        var (found, offset) = Read(file, appended ? known.Offset : -1, now);
                        // An append without a quota line keeps the file's last genuine measurement.
                        candidate = found ?? (appended ? known.Snapshot : null);
                        _memo[file.FullName] = (file.LastWriteTimeUtc, file.Length, offset, candidate);
                    }
                    if (candidate?.MeasuredAt is { } time && time <= now.AddMinutes(1) && time > (best?.MeasuredAt ?? DateTimeOffset.MinValue)) best = candidate;
                }
                return best;
            }
        }

        private List<FileInfo> Recent(DateTimeOffset now) =>
            new[] { now.UtcDateTime, now.UtcDateTime.AddDays(-1) }
                .Select(day => new DirectoryInfo(Path.Combine(Root, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"))))
                .Where(folder => folder.Exists)
                .SelectMany(folder => folder.EnumerateFiles("*.jsonl"))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(8)
                .ToList();

        /// <summary>
        /// The quota in the bytes from <paramref name="from"/> (or the last 256 KiB when it is -1) to
        /// the end, and where the next read starts: after the last whole line, so a line still being
        /// written is read whole next time.
        /// </summary>
        private static (UsageSnapshot? Snapshot, long Offset) Read(FileInfo file, long from, DateTimeOffset now)
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var end = stream.Length;
                var start = Math.Max(Math.Max(from, 0), end - TailBytes);
                var length = (int)(end - start);
                if (length <= 0) return (null, Math.Max(from, 0));
                var buffer = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    stream.Seek(start, SeekOrigin.Begin);
                    stream.ReadExactly(buffer, 0, length);
                    var lastLine = Array.LastIndexOf(buffer, (byte)'\n', length - 1);
                    var offset = lastLine >= 0 ? start + lastLine + 1 : start;
                    return (UsageParse.CodexSessionTail(System.Text.Encoding.UTF8.GetString(buffer, 0, length), now), offset);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return (null, Math.Max(from, 0));
            }
        }
    }
}
