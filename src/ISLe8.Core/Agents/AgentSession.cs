using System.Text.Json;

namespace ISLe8.Core.Agents;

public enum AgentKind
{
    Claude,
    Codex,
}

public static class AgentKinds
{
    public static string Name(AgentKind kind) => kind == AgentKind.Codex ? "Codex" : "Claude Code";

    /// <summary>The name in hook commands and requests: "claude" or "codex".</summary>
    public static string Id(AgentKind kind) => kind == AgentKind.Codex ? "codex" : "claude";

    public static AgentKind Parse(string? id) => id == "codex" ? AgentKind.Codex : AgentKind.Claude;
}

public enum AgentStatus
{
    Idle,
    Working,
    NeedsInput,
    WaitingApproval,
    Done,
}

/// <summary>What a session is doing, in words the app puts together in the user's language.</summary>
public enum ActionKind
{
    None,
    Thinking,
    WaitingInput,
    Done,
    /// <summary>The agent's own words (a Notification's message).</summary>
    Message,
    /// <summary>"$ npm test": the same in every language.</summary>
    Command,
    Reading,
    Writing,
    Editing,
    Patching,
    Searching,
    /// <summary>"Web: https://…": the same in every language.</summary>
    Web,
    WebSearch,
    Subagent,
    PlanUpdated,
    /// <summary>Any other tool: "tool: detail", or the tool's name.</summary>
    Tool,
}

/// <summary>A short line for what a session does, e.g. "$ npm test" or "Düzenliyor: Island.cs" (AgentsModule.swift, describe).</summary>
public readonly record struct AgentAction(ActionKind Kind, string Detail = "");

/// <summary>The icon beside a step (AgentsModule.swift, symbol).</summary>
public enum StepIcon
{
    Terminal,
    Read,
    Write,
    Edit,
    Search,
    Web,
    Subagent,
    Plan,
    Tool,
}

/// <summary>One tool call of the current turn.</summary>
public sealed class AgentStep(int id, string tool, AgentAction action, StepIcon icon, DateTimeOffset started)
{
    public int Id { get; } = id;
    public string Tool { get; } = tool;
    public AgentAction Action { get; } = action;
    public StepIcon Icon { get; } = icon;
    public DateTimeOffset Started { get; } = started;
    public DateTimeOffset? Finished { get; set; }
}

public enum PlanState
{
    Pending,
    Active,
    Done,
}

public sealed record AgentPlanItem(string Title, PlanState State);

/// <summary>One Claude Code or Codex session, as its hooks describe it (AgentsModule.swift, AgentSession).</summary>
public sealed class AgentSession(string id, AgentKind agent, string project, DateTimeOffset updated)
{
    public string Id { get; } = id;
    public AgentKind Agent { get; } = agent;
    public string Project { get; set; } = project;
    public AgentStatus Status { get; set; } = AgentStatus.Idle;
    public AgentAction Action { get; set; }
    public int Steps { get; set; }
    public DateTimeOffset? TurnStarted { get; set; }
    public DateTimeOffset? TurnEnded { get; set; }
    public DateTimeOffset Updated { get; set; } = updated;
    public string? LastMessage { get; set; }

    /// <summary>The latest tool calls of this turn, oldest first; at most <see cref="AgentHub.LogLength"/>.</summary>
    public List<AgentStep> Log { get; } = [];

    /// <summary>The agent's own to-do list (Claude Code's TodoWrite, Codex's update_plan), if it keeps one.</summary>
    public List<AgentPlanItem> Plan { get; set; } = [];

    /// <summary>Files written or edited this turn.</summary>
    public HashSet<string> ChangedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The app the agent runs in (a terminal, the Claude app), to bring it forward.</summary>
    public int? AppPid { get; set; }

    /// <summary>"3/6" from the plan; null without one.</summary>
    public (int Done, int Total)? PlanProgress => Plan.Count == 0 ? null : (Plan.Count(item => item.State == PlanState.Done), Plan.Count);

    /// <summary>"Adım 3/6": the step being worked on, or the last one once done.</summary>
    public (int Step, int Total)? CurrentStep => PlanProgress is { } plan
        ? (Math.Min(plan.Done + (Status == AgentStatus.Done ? 0 : 1), plan.Total), plan.Total)
        : null;

    /// <summary>
    /// The bar under the run: one segment per plan item; without a plan one per step so far (at
    /// most 12), the current one half lit (AgentViews.swift, SegmentedBar).
    /// </summary>
    public IReadOnlyList<PlanState> Segments
    {
        get
        {
            if (Plan.Count > 0) return Plan.Select(item => item.State).ToList();
            var done = Math.Min(Steps, 12);
            var active = Status == AgentStatus.Working ? 1 : 0;
            var segments = Enumerable.Repeat(PlanState.Done, Math.Max(done - active, 0)).ToList();
            if (active == 1) segments.Add(PlanState.Active);
            if (done == 0) segments.Add(PlanState.Pending);
            return segments;
        }
    }

    /// <summary>The turn's length once it ended, in whole seconds.</summary>
    public int? TurnSeconds => TurnStarted is { } started && TurnEnded is { } ended ? (int)Math.Round((ended - started).TotalSeconds) : null;
}

/// <summary>A tool call waiting for a yes or no; the hook that asked is still connected (<see cref="Id"/>).</summary>
public sealed record AgentApproval(int Id, string SessionId, AgentKind Agent, string Project, string Tool, AgentAction Detail);

/// <summary>Something a script put on the island with <c>isle8 activity</c>.</summary>
public sealed class ScriptActivity(string id)
{
    public string Id { get; } = id;
    public string Title { get; set; } = id;
    public string Subtitle { get; set; } = "";
    public double? Progress { get; set; }
    public string Icon { get; set; } = "";
}

/// <summary>Reading hook events and tool inputs (AgentsModule.swift, helpers).</summary>
public static class AgentText
{
    /// <summary>A short, readable line for a tool call.</summary>
    public static AgentAction Describe(string? tool, JsonElement input)
    {
        tool ??= "?";
        string File(string key) => Path.GetFileName(String(input, key) ?? "");
        switch (tool)
        {
            case "Bash" or "shell" or "exec_command" or "local_shell":
                var command = String(input, "command")
                    ?? (Property(input, "command") is { ValueKind: JsonValueKind.Array } parts
                        ? string.Join(' ', parts.EnumerateArray().Where(part => part.ValueKind == JsonValueKind.String).Select(part => part.GetString()))
                        : "");
                return new(ActionKind.Command, FirstLine(command));
            case "Read": return new(ActionKind.Reading, File("file_path"));
            case "Write": return new(ActionKind.Writing, File("file_path"));
            case "Edit" or "MultiEdit": return new(ActionKind.Editing, File("file_path"));
            case "NotebookEdit": return new(ActionKind.Editing, File("notebook_path"));
            case "apply_patch": return new(ActionKind.Patching);
            case "Grep" or "Glob": return new(ActionKind.Searching, String(input, "pattern") ?? "");
            case "WebFetch": return new(ActionKind.Web, String(input, "url") ?? "");
            case "WebSearch": return new(ActionKind.WebSearch, String(input, "query") ?? "");
            case "Task" or "Agent": return new(ActionKind.Subagent, String(input, "description") ?? "");
            case "TodoWrite" or "update_plan": return new(ActionKind.PlanUpdated);
            default:
                var detail = input.ValueKind == JsonValueKind.Object
                    ? input.EnumerateObject().Where(item => item.Value.ValueKind == JsonValueKind.String).Select(item => FirstLine(item.Value.GetString()!)).FirstOrDefault() ?? ""
                    : "";
                return new(ActionKind.Tool, detail.Length == 0 ? tool : $"{tool}: {detail}");
        }
    }

    /// <summary>The agent's to-do list, when this call writes one.</summary>
    public static List<AgentPlanItem>? Plan(string tool, JsonElement input)
    {
        static PlanState State(string? status) => status switch
        {
            "completed" => PlanState.Done,
            "in_progress" => PlanState.Active,
            _ => PlanState.Pending,
        };
        switch (tool)
        {
            case "TodoWrite" when Property(input, "todos") is { ValueKind: JsonValueKind.Array } todos:
                return todos.EnumerateArray().Select(todo =>
                {
                    var status = String(todo, "status");
                    var title = (status == "in_progress" ? String(todo, "activeForm") : null) ?? String(todo, "content") ?? "";
                    return new AgentPlanItem(title, State(status));
                }).ToList();
            case "update_plan" when Property(input, "plan") is { ValueKind: JsonValueKind.Array } steps:
                return steps.EnumerateArray().Select(step => new AgentPlanItem(String(step, "step") ?? "", State(String(step, "status")))).ToList();
            default:
                return null;
        }
    }

    /// <summary>Paths a call writes to: Edit and Write name one; a Codex patch lists them in its text.</summary>
    public static IEnumerable<string> ChangedFiles(string tool, JsonElement input)
    {
        switch (tool)
        {
            case "Write" or "Edit" or "MultiEdit":
                return String(input, "file_path") is { } file ? [file] : [];
            case "NotebookEdit":
                return String(input, "notebook_path") is { } notebook ? [notebook] : [];
            case "apply_patch":
                if (input.ValueKind != JsonValueKind.Object) return [];
                var text = string.Join('\n', input.EnumerateObject().Where(item => item.Value.ValueKind == JsonValueKind.String).Select(item => item.Value.GetString()));
                return text.Split('\n').Select(line => line.TrimEnd('\r')).SelectMany(line =>
                    new[] { "*** Update File: ", "*** Add File: " }.Where(line.StartsWith).Select(prefix => line[prefix.Length..])).ToList();
            default:
                return [];
        }
    }

    public static StepIcon Icon(string tool) => tool switch
    {
        "Bash" or "shell" or "exec_command" or "local_shell" => StepIcon.Terminal,
        "Read" => StepIcon.Read,
        "Write" => StepIcon.Write,
        "Edit" or "MultiEdit" or "NotebookEdit" or "apply_patch" => StepIcon.Edit,
        "Grep" or "Glob" => StepIcon.Search,
        "WebFetch" or "WebSearch" => StepIcon.Web,
        "Task" or "Agent" => StepIcon.Subagent,
        "TodoWrite" or "update_plan" => StepIcon.Plan,
        _ => StepIcon.Tool,
    };

    /// <summary>The first line, at most 120 characters.</summary>
    public static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].TrimEnd('\r');
        return line.Length > 120 ? line[..117] + "…" : line;
    }

    /// <summary>The folder a session works in, by name: "ISLe8-Windows-main" out of its cwd.</summary>
    public static string Project(string? cwd, AgentKind agent) =>
        string.IsNullOrWhiteSpace(cwd) ? AgentKinds.Name(agent) : Path.GetFileName(cwd.TrimEnd('/', '\\')) is { Length: > 0 } name ? name : cwd;

    public static JsonElement? Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    public static string? String(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
