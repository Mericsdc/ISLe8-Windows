using System.Text.Json;
using System.Text.Json.Nodes;
using ISLe8.Core.Agents;
using ISLe8.Core.Geometry;
using ISLe8.Core.Island;
using ISLe8.Core.Settings;

namespace ISLe8.Core.Tests;

/// <summary>The AI tab: hook events, approvals, script activities and the hook installer (PORTING.md §3.12).</summary>
public class AgentTests
{
    private readonly ManualClock _clock = new();
    private readonly AppSettings _settings = new();
    private readonly AgentHub _hub;
    private readonly List<string> _replies = [];

    public AgentTests()
    {
        _hub = new AgentHub(_settings, _clock);
    }

    private void Wait(double seconds) => _clock.Advance(TimeSpan.FromSeconds(seconds));

    private void Hook(string json, int connection = 1, string agent = "claude")
    {
        var hookEvent = JsonNode.Parse(json)!.AsObject();
        hookEvent.TryAdd("session_id", "s1");
        hookEvent.TryAdd("cwd", @"C:\Users\LRX\Desktop\ISLe8-Windows-main");
        var request = new JsonObject { ["type"] = "hook", ["agent"] = agent, ["event"] = hookEvent };
        _hub.Handle(request.ToJsonString(), connection, _replies.Add);
    }

    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void AnApprovalCarriesTheWholeRequest()
    {
        var twoLines = Input("""{"command":"echo ok\nrm -rf ~/x"}""");
        Assert.Equal("echo ok\nrm -rf ~/x", AgentText.Request("Bash", twoLines));
        Assert.Equal(@"C:\Users\me\.claude\settings.json", AgentText.Request("Edit", Input("""{"file_path":"C:\\Users\\me\\.claude\\settings.json","old_string":"a"}""")));
        Assert.Equal("url: https://x\ndepth: 2", AgentText.Request("mcp__fetch", Input("""{"url":"https://x","depth":2}""")));
        var approval = new AgentApproval(1, "s", AgentKind.Claude, "p", "Bash", AgentText.Describe("Bash", twoLines), AgentText.Request("Bash", twoLines));
        Assert.Equal("echo ok", approval.Detail.Detail);
        Assert.True(approval.IsLongerThanDetail);
        Assert.Equal(2, approval.RequestLines);
        Assert.Equal(AgentText.RequestLimit, AgentText.Request("Bash", Input($$"""{"command":"{{new string('a', 9000)}}"}""")).Length);
    }

    [Fact]
    public void ToolCallsReadAsShortLines()
    {
        Assert.Equal(new AgentAction(ActionKind.Command, "npm test"), AgentText.Describe("Bash", Input("""{"command":"npm test\nnpm run lint"}""")));
        Assert.Equal(new AgentAction(ActionKind.Command, "bash -lc ls"), AgentText.Describe("shell", Input("""{"command":["bash","-lc","ls"]}""")));
        Assert.Equal(new AgentAction(ActionKind.Editing, "IslandModel.cs"), AgentText.Describe("Edit", Input("""{"file_path":"C:\\src\\IslandModel.cs"}""")));
        Assert.Equal(new AgentAction(ActionKind.Reading, "README.md"), AgentText.Describe("Read", Input("""{"file_path":"/c/repo/README.md"}""")));
        Assert.Equal(new AgentAction(ActionKind.Searching, "TODO"), AgentText.Describe("Grep", Input("""{"pattern":"TODO"}""")));
        Assert.Equal(new AgentAction(ActionKind.PlanUpdated), AgentText.Describe("TodoWrite", Input("{}")));
        Assert.Equal(new AgentAction(ActionKind.Tool, "mcp__x: hello"), AgentText.Describe("mcp__x", Input("""{"n":1,"text":"hello"}""")));
        Assert.Equal(new AgentAction(ActionKind.Tool, "Odd"), AgentText.Describe("Odd", default));
        Assert.Equal(new string('a', 117) + "…", AgentText.FirstLine(new string('a', 200)));
    }

    [Fact]
    public void APatchListsTheFilesItChanges()
    {
        var patch = Input("""{"input":"*** Begin Patch\n*** Update File: src/a.cs\n@@\n*** Add File: src/b.cs\n*** End Patch"}""");
        Assert.Equal(["src/a.cs", "src/b.cs"], AgentText.ChangedFiles("apply_patch", patch));
        Assert.Equal(["C:/x.cs"], AgentText.ChangedFiles("Write", Input("""{"file_path":"C:/x.cs"}""")));
    }

    [Fact]
    public void AFakeTurnMakesTheRunCard()
    {
        Hook("""{"hook_event_name":"SessionStart"}""");
        Hook("""{"hook_event_name":"UserPromptSubmit"}""");
        var session = _hub.Sessions.Single();
        Assert.Equal(("claude:s1", "ISLe8-Windows-main", AgentStatus.Working, ActionKind.Thinking), (session.Id, session.Project, session.Status, session.Action.Kind));

        Wait(1);
        Hook("""{"hook_event_name":"PreToolUse","tool_name":"TodoWrite","tool_input":{"todos":[{"content":"Read","status":"completed","activeForm":"Reading"},{"content":"Edit","status":"in_progress","activeForm":"Editing the model"},{"content":"Test","status":"pending","activeForm":"Testing"},{"content":"Commit","status":"pending","activeForm":"Committing"}]}}""");
        Hook("""{"hook_event_name":"PostToolUse","tool_name":"TodoWrite"}""");
        Assert.Equal((2, 4), session.CurrentStep);
        Assert.Equal("Editing the model", session.Plan[1].Title);
        Assert.Equal([PlanState.Done, PlanState.Active, PlanState.Pending, PlanState.Pending], session.Segments);

        Wait(2);
        Hook("""{"hook_event_name":"PreToolUse","tool_name":"Edit","tool_input":{"file_path":"C:\\src\\A.cs"}}""");
        Wait(1.5);
        Hook("""{"hook_event_name":"PostToolUse","tool_name":"Edit"}""");
        Hook("""{"hook_event_name":"PreToolUse","tool_name":"Write","tool_input":{"file_path":"C:\\src\\B.cs"}}""");
        Wait(3.5);
        Hook("""{"hook_event_name":"Stop","last_assistant_message":"Done.\nMore text"}""");

        Assert.Equal(AgentStatus.Done, session.Status);
        Assert.Equal(8, session.TurnSeconds);
        Assert.Equal(2, session.ChangedFiles.Count);
        Assert.Equal(3, session.Steps);
        Assert.Equal("Done.", session.LastMessage);
        // Done, the count is what was completed (AgentViews.swift, progressText).
        Assert.Equal((1, 4), session.CurrentStep);
        Assert.Equal(TimeSpan.FromSeconds(1.5), session.Log[1].Finished - session.Log[1].Started);
        // The Write had no PostToolUse; the turn's end closes it.
        Assert.Equal(TimeSpan.FromSeconds(3.5), session.Log[2].Finished - session.Log[2].Started);
        Assert.All(_replies, reply => Assert.Equal("""{"ok":true}""", reply));
    }

    [Fact]
    public void WithoutAPlanTheBarCountsSteps()
    {
        Hook("""{"hook_event_name":"UserPromptSubmit"}""");
        Assert.Equal([PlanState.Active, PlanState.Pending], _hub.Sessions[0].Segments);
        Hook("""{"hook_event_name":"PreToolUse","tool_name":"Read","tool_input":{}}""");
        Hook("""{"hook_event_name":"PreToolUse","tool_name":"Read","tool_input":{}}""");
        Assert.Null(_hub.Sessions[0].CurrentStep);
        Assert.Equal([PlanState.Done, PlanState.Active], _hub.Sessions[0].Segments);
    }

    [Fact]
    public void TheLogKeepsTheLastEightSteps()
    {
        Hook("""{"hook_event_name":"UserPromptSubmit"}""");
        for (var i = 0; i < 11; i++) Hook("""{"hook_event_name":"PreToolUse","tool_name":"Read","tool_input":{}}""");
        var session = _hub.Sessions[0];
        Assert.Equal(AgentHub.LogLength, session.Log.Count);
        Assert.Equal(4, session.Log[0].Id);
    }

    [Fact]
    public void AnApprovalWaitsForTheUsersAnswer()
    {
        AgentApproval? asked = null;
        _hub.ApprovalRequested += approval => asked = approval;
        Hook("""{"hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"rm -rf build"}}""", connection: 7);
        Assert.Empty(_replies);
        Assert.NotNull(asked);
        Assert.Equal(new AgentAction(ActionKind.Command, "rm -rf build"), asked.Detail);
        Assert.Equal(AgentStatus.WaitingApproval, _hub.Sessions[0].Status);
        Assert.True(_hub.NeedsAttention);
        Assert.Equal(new AgentEar(AgentEarKind.Attention, AgentKind.Claude, null, true), _hub.Ear);

        _hub.Decide(asked, allow: false);
        Assert.Equal("""{"decision":"deny","message":"Denied in ISLe8."}""", Assert.Single(_replies));
        Assert.Empty(_hub.Approvals);
        Assert.Equal(AgentStatus.Working, _hub.Sessions[0].Status);
    }

    [Fact]
    public void UnansweredItGoesBackToTheAgent()
    {
        _settings.AgentApprovalTimeout = 15;
        Hook("""{"hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"ls"}}""", connection: 3);
        Wait(14.9);
        Assert.Empty(_replies);
        Wait(0.1);
        Assert.Equal("""{"decision":"none"}""", Assert.Single(_replies));
        Assert.Equal(AgentStatus.NeedsInput, _hub.Sessions[0].Status);
    }

    [Fact]
    public void AHookThatGivesUpTakesItsQuestionAlong()
    {
        Hook("""{"hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"ls"}}""", connection: 4);
        _hub.Abandoned(4);
        Assert.Empty(_hub.Approvals);
        Assert.Empty(_replies);
        Wait(200);
        Assert.Empty(_replies);
    }

    [Fact]
    public void ApprovalsOffLeaveTheQuestionToTheAgent()
    {
        _settings.AgentApprovalsOnIsland = false;
        Hook("""{"hook_event_name":"PermissionRequest","tool_name":"Bash","tool_input":{"command":"ls"}}""");
        Assert.Equal("""{"ok":true}""", Assert.Single(_replies));
        Assert.Empty(_hub.Approvals);
    }

    [Fact]
    public void FinishedSessionsLeaveAfterTenMinutes()
    {
        var finished = new List<string>();
        _hub.SessionFinished += session => finished.Add(session.Id);
        Hook("""{"hook_event_name":"UserPromptSubmit"}""");
        Hook("""{"hook_event_name":"Stop"}""");
        Assert.Equal(["claude:s1"], finished);
        Wait(599);
        Assert.Single(_hub.Sessions);
        Wait(1);
        Assert.Empty(_hub.Sessions);

        _settings.AgentDoneAlert = false;
        Hook("""{"hook_event_name":"Stop"}""");
        Assert.Single(finished);
    }

    [Fact]
    public void AQuestionFromTheAgentWaitsForTheUser()
    {
        Hook("""{"hook_event_name":"Notification","notification_type":"idle_prompt","message":"Claude is waiting for your input"}""");
        Assert.Equal(new AgentAction(ActionKind.Message, "Claude is waiting for your input"), _hub.Sessions[0].Action);
        Assert.Equal(AgentEarKind.Attention, _hub.Ear!.Value.Kind);
        Hook("""{"hook_event_name":"SessionEnd"}""");
        Assert.Empty(_hub.Sessions);
        Assert.Null(_hub.Ear);
    }

    [Fact]
    public void TheTabFollowsWhoeverNeedsTheUser()
    {
        Hook("""{"hook_event_name":"UserPromptSubmit","session_id":"a"}""");
        Hook("""{"hook_event_name":"Notification","notification_type":"permission_prompt","session_id":"b"}""", agent: "codex");
        Hook("""{"hook_event_name":"UserPromptSubmit","session_id":"c"}""");
        Assert.Equal("codex:b", _hub.Followed(null)!.Id);
        Assert.Equal("claude:a", _hub.Followed("claude:a")!.Id);
        Assert.Equal("Codex", AgentKinds.Name(_hub.Followed(null)!.Agent));
    }

    [Fact]
    public void ScriptsPutActivitiesOnTheIsland()
    {
        _hub.Handle("""{"type":"activity","action":"start","id":"build","title":"Derleme","progress":0.4}""", 1, _replies.Add);
        _hub.Handle("""{"type":"activity","action":"update","id":"build","progress":1.7,"subtitle":"x64"}""", 2, _replies.Add);
        var activity = Assert.Single(_hub.Activities);
        Assert.Equal(("Derleme", "x64", (double?)1), (activity.Title, activity.Subtitle, activity.Progress));
        Assert.Equal(AgentEarKind.Script, _hub.Ear!.Value.Kind);
        _hub.Handle("""{"type":"activity","action":"end","id":"build"}""", 3, _replies.Add);
        Assert.Empty(_hub.Activities);
    }

    [Fact]
    public void TheRestOfTheCommandIsAnswered()
    {
        string? opened = null;
        _hub.IslandCommand += (action, tab) => opened = $"{action}:{tab}";
        _hub.DevicesQuery = () => ["AirPods Pro · Sol %80"];
        _hub.Handle("""{"type":"island","action":"open","tab":"agents"}""", 1, _replies.Add);
        _hub.Handle("""{"type":"devices"}""", 2, _replies.Add);
        _hub.Handle("""{"type":"ping"}""", 3, _replies.Add);
        _hub.Handle("not json", 4, _replies.Add);
        Assert.Equal("open:agents", opened);
        Assert.Equal(["""{"ok":true}""", """{"ok":true,"devices":["AirPods Pro · Sol %80"]}""", """{"ok":true,"app":"ISLe8"}""", """{"ok":false}"""],
            _replies.Select(reply => Regex(reply)));
    }

    /// <summary>Non-ASCII is escaped in the reply; the agent reads it back the same.</summary>
    private static string Regex(string reply) => JsonNode.Parse(reply)!.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    [Fact]
    public void BannersAndTheTimeoutKeepToTheMac()
    {
        var approval = new AgentApproval(1, "s", AgentKind.Claude, "p", "Bash", default);
        Assert.Equal(new DipSize(440, 106), IslandSizes.Alert(new ApprovalAlert(approval)));
        Assert.Equal(new DipSize(400, 56), IslandSizes.Alert(new AgentDoneAlert(new AgentSession("s", AgentKind.Claude, "p", default))));
        Assert.Equal(TimeSpan.FromSeconds(15), AlertDurations.Approval);
        var settings = new AppSettings { AgentApprovalTimeout = 400 };
        settings.Normalize();
        Assert.Equal(300, settings.AgentApprovalTimeout);
        settings.AgentApprovalTimeout = 50;
        settings.Normalize();
        Assert.Equal(45, settings.AgentApprovalTimeout);
    }

    [Fact]
    public void AnAgentsEarComesBeforeTheMusic()
    {
        var model = new IslandModel(_settings, _clock);
        model.SetLive(true);
        Assert.Equal(PillActivity.Media, model.Activity);
        model.SetAgentLive(true);
        Assert.Equal(PillActivity.Agent, model.Activity);
        model.SetAgentLive(false);
        Assert.Equal(PillActivity.Media, model.Activity);

        model.PreferredTab = () => IslandTab.Agents;
        model.Click();
        Assert.Equal(IslandTab.Agents, model.Tab);
        model.Close();
        model.Open(IslandTab.Shelf);
        Assert.Equal((IslandState.Expanded, IslandTab.Shelf), (model.State, model.Tab));
    }

    // MARK: Hooks

    [Fact]
    public void ConnectingAddsAGroupPerEventAndKeepsTheRest()
    {
        var folder = Directory.CreateTempSubdirectory("isle8-hooks");
        try
        {
            var file = Path.Combine(folder.FullName, ".claude", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, """
                {
                  "model": "opus",
                  "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "say done" } ] } ] }
                }
                """);
            Assert.False(AgentHooks.IsInstalled(file));

            AgentHooks.Install(AgentKind.Claude, file, @"C:\Program Files\ISLe8\isle8-cli.exe");
            Assert.True(AgentHooks.IsInstalled(file));
            Assert.True(File.Exists(file + ".isle8-backup"));
            var root = JsonNode.Parse(File.ReadAllText(file))!;
            Assert.Equal("opus", root["model"]!.GetValue<string>());
            Assert.Equal(AgentHooks.Events(AgentKind.Claude).Count, root["hooks"]!.AsObject().Count);
            var stop = root["hooks"]!["Stop"]!.AsArray();
            Assert.Equal(2, stop.Count);
            Assert.Equal("say done", stop[0]!["hooks"]![0]!["command"]!.GetValue<string>());
            Assert.Equal("\"C:/Program Files/ISLe8/isle8-cli.exe\" hook claude", stop[1]!["hooks"]![0]!["command"]!.GetValue<string>());
            Assert.Equal(600, root["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!["timeout"]!.GetValue<int>());

            // Connecting twice doesn't add twice.
            AgentHooks.Install(AgentKind.Claude, file, @"C:\Program Files\ISLe8\isle8-cli.exe");
            Assert.Equal(2, JsonNode.Parse(File.ReadAllText(file))!["hooks"]!["Stop"]!.AsArray().Count);

            AgentHooks.Uninstall(file);
            Assert.False(AgentHooks.IsInstalled(file));
            root = JsonNode.Parse(File.ReadAllText(file))!;
            Assert.Single(root["hooks"]!.AsObject());
            Assert.Equal("say done", root["hooks"]!["Stop"]![0]!["hooks"]![0]!["command"]!.GetValue<string>());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void CodexGetsItsOwnFileWithoutNotifications()
    {
        var folder = Directory.CreateTempSubdirectory("isle8-hooks");
        try
        {
            var file = AgentHooks.File(AgentKind.Codex, folder.FullName);
            AgentHooks.Install(AgentKind.Codex, file, "isle8.exe");
            var hooks = JsonNode.Parse(File.ReadAllText(file))!["hooks"]!.AsObject();
            Assert.False(hooks.ContainsKey("Notification"));
            Assert.Equal("\"isle8.exe\" hook codex", hooks["Stop"]![0]!["hooks"]![0]!["command"]!.GetValue<string>());
            Assert.False(File.Exists(file + ".isle8-backup"));
            AgentHooks.Uninstall(file);
            Assert.Equal("{}", File.ReadAllText(file).Trim());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void ABrokenSettingsFileIsLeftAlone()
    {
        var folder = Directory.CreateTempSubdirectory("isle8-hooks");
        try
        {
            var file = Path.Combine(folder.FullName, "settings.json");
            File.WriteAllText(file, "{ not json");
            Assert.Throws<InvalidDataException>(() => AgentHooks.Install(AgentKind.Claude, file, "isle8.exe"));
            Assert.Equal("{ not json", File.ReadAllText(file));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
