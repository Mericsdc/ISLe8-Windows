using System.Text;
using System.Text.Json;
using ISLe8.Core.Island;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Agents;

/// <summary>What the pill's ears show for the agents (AgentsModule.swift, activity).</summary>
public enum AgentEarKind
{
    /// <summary>Working: bars in the agent's colour.</summary>
    Working,
    /// <summary>Waiting on the user: an approval or a question.</summary>
    Attention,
    /// <summary>A script's activity: its progress, or bars.</summary>
    Script,
}

public readonly record struct AgentEar(AgentEarKind Kind, AgentKind? Agent, ScriptActivity? Script, bool IsApproval);

/// <summary>
/// Claude Code and Codex on the island, through their hooks (AgentsModule.swift): what each session
/// is doing, tool approvals answered with a click, and a note when a turn is done. It also answers
/// the rest of the <c>isle8</c> command (activity, open, close, devices, ping). Requests and replies
/// are one JSON line each (PORTING.md §1); the app carries them over its named pipe.
/// </summary>
public sealed class AgentHub
{
    /// <summary>Steps kept per turn.</summary>
    public const int LogLength = 8;

    /// <summary>A finished session leaves the list after this long.</summary>
    public static readonly TimeSpan DoneKeep = TimeSpan.FromMinutes(10);

    private readonly AppSettings _settings;
    private readonly IIslandClock _clock;
    private readonly List<AgentSession> _sessions = [];
    private readonly List<AgentApproval> _approvals = [];
    private readonly List<ScriptActivity> _activities = [];
    private readonly Dictionary<int, Action<string>> _replies = [];
    private readonly Dictionary<int, IDisposable> _timeouts = [];
    private IDisposable? _cleanup;

    public AgentHub(AppSettings settings, IIslandClock clock)
    {
        _settings = settings;
        _clock = clock;
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<AgentSession> Sessions => _sessions;
    public IReadOnlyList<AgentApproval> Approvals => _approvals;
    public IReadOnlyList<ScriptActivity> Activities => _activities;

    /// <summary>Anything changed.</summary>
    public event Action? Changed;

    /// <summary>A tool call asks for approval: worth a banner.</summary>
    public event Action<AgentApproval>? ApprovalRequested;

    /// <summary>A turn ended (Settings › Agents › tell me when an agent is done).</summary>
    public event Action<AgentSession>? SessionFinished;

    /// <summary><c>isle8 open [tab]</c> and <c>isle8 close</c>: the action and the tab's name.</summary>
    public event Action<string, string?>? IslandCommand;

    /// <summary><c>isle8 devices</c>: one line per connected device and its batteries.</summary>
    public Func<IReadOnlyList<string>>? DevicesQuery { get; set; }

    /// <summary>An approval or a question waits: the island opens on the AI tab.</summary>
    public bool NeedsAttention => _approvals.Count > 0 || _sessions.Any(session => session.Status == AgentStatus.NeedsInput);

    /// <summary>The ears: an approval before everything, then a working or waiting session, then a script.</summary>
    public AgentEar? Ear
    {
        get
        {
            if (_approvals.FirstOrDefault() is { } approval) return new AgentEar(AgentEarKind.Attention, approval.Agent, null, true);
            if (_sessions.FirstOrDefault(session => session.Status is AgentStatus.Working or AgentStatus.NeedsInput) is { } session)
            {
                return new AgentEar(session.Status == AgentStatus.NeedsInput ? AgentEarKind.Attention : AgentEarKind.Working, session.Agent, null, false);
            }
            return _activities.FirstOrDefault() is { } script ? new AgentEar(AgentEarKind.Script, null, script, false) : null;
        }
    }

    /// <summary>The session the AI tab follows: the one picked, else whoever waits or works, else the latest.</summary>
    public AgentSession? Followed(string? picked) =>
        _sessions.FirstOrDefault(session => session.Id == picked)
        ?? _sessions.FirstOrDefault(session => session.Status is AgentStatus.WaitingApproval or AgentStatus.NeedsInput)
        ?? _sessions.FirstOrDefault(session => session.Status == AgentStatus.Working)
        ?? _sessions.FirstOrDefault();

    // MARK: Requests

    /// <summary>
    /// One request from a connection. <paramref name="reply"/> gets the answer line, at once or, for
    /// an approval, when the user decides (or the time runs out).
    /// </summary>
    public void Handle(string line, int connection, Action<string> reply)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            reply(Reply(writer => writer.WriteBoolean("ok", false)));
            return;
        }
        using (document)
        {
            var request = document.RootElement;
            switch (AgentText.String(request, "type"))
            {
                case null:
                    reply(Reply(writer => writer.WriteBoolean("ok", false)));
                    break;
                case "hook":
                    var agent = AgentKinds.Parse(AgentText.String(request, "agent"));
                    var hookEvent = AgentText.Property(request, "event") ?? default;
                    int? appPid = AgentText.Property(request, "app_pid") is { ValueKind: JsonValueKind.Number } pid && pid.TryGetInt32(out var value) ? value : null;
                    HandleHook(agent, hookEvent, appPid, connection, reply);
                    break;
                case "activity":
                    HandleActivity(request);
                    reply(Ok);
                    break;
                case "island":
                    IslandCommand?.Invoke(AgentText.String(request, "action") ?? "", AgentText.String(request, "tab"));
                    reply(Ok);
                    break;
                case "devices":
                    var devices = DevicesQuery?.Invoke() ?? [];
                    reply(Reply(writer =>
                    {
                        writer.WriteBoolean("ok", true);
                        writer.WriteStartArray("devices");
                        foreach (var device in devices) writer.WriteStringValue(device);
                        writer.WriteEndArray();
                    }));
                    break;
                default:
                    reply(Reply(writer =>
                    {
                        writer.WriteBoolean("ok", true);
                        writer.WriteString("app", "ISLe8");
                    }));
                    break;
            }
        }
    }

    /// <summary>
    /// The user's answer: true approves, false denies, null hands the question back to the agent,
    /// which then asks in its own window.
    /// </summary>
    public void Decide(AgentApproval approval, bool? allow)
    {
        if (_timeouts.Remove(approval.Id, out var timeout)) timeout.Dispose();
        if (_replies.Remove(approval.Id, out var reply))
        {
            reply(Reply(writer =>
            {
                writer.WriteString("decision", allow switch { true => "allow", false => "deny", null => "none" });
                if (allow == false) writer.WriteString("message", "Denied in ISLe8.");
            }));
        }
        _approvals.RemoveAll(pending => pending.Id == approval.Id);
        if (Find(approval.SessionId) is { Status: AgentStatus.WaitingApproval } session)
        {
            session.Status = allow is null ? AgentStatus.NeedsInput : AgentStatus.Working;
        }
        Changed?.Invoke();
    }

    /// <summary>The hook gave up (its own timeout, or the agent was stopped) before an answer.</summary>
    public void Abandoned(int connection)
    {
        _replies.Remove(connection);
        if (_timeouts.Remove(connection, out var timeout)) timeout.Dispose();
        if (_approvals.Find(pending => pending.Id == connection) is not { } approval) return;
        _approvals.Remove(approval);
        if (Find(approval.SessionId) is { Status: AgentStatus.WaitingApproval } session) session.Status = AgentStatus.NeedsInput;
        Changed?.Invoke();
    }

    /// <summary>ISLe8 quits: no hook is left hanging; whoever waits falls back to its own prompt.</summary>
    public void Stop()
    {
        foreach (var approval in _approvals.ToList()) Decide(approval, null);
        _cleanup?.Dispose();
    }

    private void HandleHook(AgentKind agent, JsonElement hookEvent, int? appPid, int connection, Action<string> reply)
    {
        var name = AgentText.String(hookEvent, "hook_event_name") ?? "";
        var sessionId = $"{AgentKinds.Id(agent)}:{AgentText.String(hookEvent, "session_id") ?? "default"}";
        var project = AgentText.Project(AgentText.String(hookEvent, "cwd"), agent);
        var now = _clock.Now;

        void Touch(Action<AgentSession> change)
        {
            var session = Find(sessionId) ?? new AgentSession(sessionId, agent, project, now);
            _sessions.Remove(session);
            session.Project = project;
            session.Updated = now;
            if (appPid is { } pid) session.AppPid = pid;
            change(session);
            _sessions.Insert(0, session);
        }

        var tool = AgentText.String(hookEvent, "tool_name") ?? "?";
        var input = AgentText.Property(hookEvent, "tool_input") ?? default;
        switch (name)
        {
            case "SessionStart":
                Touch(_ => { });
                break;
            case "UserPromptSubmit":
                Touch(session =>
                {
                    session.Status = AgentStatus.Working;
                    session.Action = new AgentAction(ActionKind.Thinking);
                    session.Steps = 0;
                    session.TurnStarted = now;
                    session.TurnEnded = null;
                    session.LastMessage = null;
                    session.Log.Clear();
                    session.ChangedFiles.Clear();
                });
                break;
            case "PreToolUse":
                Touch(session =>
                {
                    session.Status = AgentStatus.Working;
                    session.Action = AgentText.Describe(tool, input);
                    session.Steps++;
                    session.TurnStarted ??= now;
                    if (AgentText.Plan(tool, input) is { } plan) session.Plan = plan;
                    session.ChangedFiles.UnionWith(AgentText.ChangedFiles(tool, input));
                    // Anything still open was answered without a PostToolUse (denied, interrupted).
                    foreach (var step in session.Log) step.Finished ??= now;
                    session.Log.Add(new AgentStep(session.Steps, tool, session.Action, AgentText.Icon(tool), now));
                    if (session.Log.Count > LogLength) session.Log.RemoveRange(0, session.Log.Count - LogLength);
                });
                break;
            case "PostToolUse":
                Touch(session =>
                {
                    if (session.Status != AgentStatus.WaitingApproval) session.Status = AgentStatus.Working;
                    if (session.Log.LastOrDefault(step => step.Tool == tool && step.Finished is null) is { } open) open.Finished = now;
                });
                break;
            case "PermissionRequest" when _settings.AgentApprovalsOnIsland:
                var approval = new AgentApproval(connection, sessionId, agent, project, tool, AgentText.Describe(tool, input), AgentText.Request(tool, input));
                Touch(session => session.Status = AgentStatus.WaitingApproval);
                _approvals.Add(approval);
                _replies[connection] = reply;
                _timeouts[connection] = _clock.After(TimeSpan.FromSeconds(_settings.AgentApprovalTimeout), () =>
                {
                    _timeouts.Remove(connection);
                    if (_approvals.Find(pending => pending.Id == connection) is { } pending) Decide(pending, null);
                });
                Changed?.Invoke();
                ApprovalRequested?.Invoke(approval);
                // The answer comes with the user's decision.
                return;
            case "Notification":
                var kind = AgentText.String(hookEvent, "notification_type") ?? "";
                if (kind is "permission_prompt" or "idle_prompt" or "agent_needs_input" or "elicitation_dialog")
                {
                    var message = AgentText.String(hookEvent, "message");
                    Touch(session =>
                    {
                        session.Status = AgentStatus.NeedsInput;
                        session.Action = string.IsNullOrEmpty(message) ? new AgentAction(ActionKind.WaitingInput) : new AgentAction(ActionKind.Message, message);
                    });
                }
                break;
            case "Stop":
                var last = AgentText.String(hookEvent, "last_assistant_message");
                Touch(session =>
                {
                    session.Status = AgentStatus.Done;
                    session.LastMessage = last is null ? null : AgentText.FirstLine(last);
                    session.Action = new AgentAction(ActionKind.Done);
                    session.TurnEnded = now;
                    foreach (var step in session.Log) step.Finished ??= now;
                });
                if (Find(sessionId) is { } finished && _settings.AgentDoneAlert) SessionFinished?.Invoke(finished);
                ScheduleCleanup();
                break;
            case "SessionEnd":
                _sessions.RemoveAll(session => session.Id == sessionId);
                break;
        }
        Changed?.Invoke();
        reply(Ok);
    }

    private void HandleActivity(JsonElement request)
    {
        if (AgentText.String(request, "id") is not { } id) return;
        var action = AgentText.String(request, "action") ?? "start";
        double? progress = AgentText.Property(request, "progress") is { ValueKind: JsonValueKind.Number } number ? Math.Clamp(number.GetDouble(), 0, 1) : null;
        var existing = _activities.Find(activity => activity.Id == id);
        if (action == "end")
        {
            if (existing is not null) _activities.Remove(existing);
        }
        else if (existing is not null)
        {
            if (AgentText.String(request, "title") is { } title) existing.Title = title;
            if (AgentText.String(request, "subtitle") is { } subtitle) existing.Subtitle = subtitle;
            if (progress is not null) existing.Progress = progress;
            if (AgentText.String(request, "icon") is { } icon) existing.Icon = icon;
        }
        else
        {
            _activities.Insert(0, new ScriptActivity(id)
            {
                Title = AgentText.String(request, "title") ?? id,
                Subtitle = AgentText.String(request, "subtitle") ?? "",
                Progress = progress,
                Icon = AgentText.String(request, "icon") ?? "",
            });
        }
        Changed?.Invoke();
    }

    /// <summary>Finished sessions fade from the list after ten minutes.</summary>
    private void ScheduleCleanup()
    {
        _cleanup?.Dispose();
        _cleanup = _clock.After(DoneKeep, () =>
        {
            _cleanup = null;
            var cutoff = _clock.Now - DoneKeep;
            if (_sessions.RemoveAll(session => session.Status == AgentStatus.Done && session.Updated <= cutoff) > 0) Changed?.Invoke();
            if (_sessions.Any(session => session.Status == AgentStatus.Done)) ScheduleCleanup();
        });
    }

    private AgentSession? Find(string id) => _sessions.Find(session => session.Id == id);

    private static readonly string Ok = Reply(writer => writer.WriteBoolean("ok", true));

    private static string Reply(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
