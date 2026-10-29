using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ISLe8.Core.Agents;

/// <summary>
/// Adds ISLe8's hooks to Claude Code (<c>%USERPROFILE%\.claude\settings.json</c>) or Codex
/// (<c>%USERPROFILE%\.codex\hooks.json</c>), and takes them out again (AgentHooks.swift). Only
/// entries that call <c>isle8 hook</c> are ever touched; the file is backed up next to itself
/// (<c>.isle8-backup</c>) before the first change. ISLe8 never does this on its own: Settings › Agents › Connect.
/// </summary>
public static class AgentHooks
{
    public static string File(AgentKind agent, string home) => agent == AgentKind.Codex
        ? Path.Combine(home, ".codex", "hooks.json")
        : Path.Combine(home, ".claude", "settings.json");

    public static IReadOnlyList<string> Events(AgentKind agent) => agent == AgentKind.Codex
        ? ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest", "Stop", "SessionEnd"]
        : ["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PermissionRequest", "Notification", "Stop", "SessionEnd"];

    /// <summary>
    /// The command a hook runs, with forward slashes: agents run hooks through a shell, Git Bash
    /// on Windows as often as cmd, and both take them.
    /// </summary>
    public static string Command(string executable, AgentKind agent) => $"\"{executable.Replace('\\', '/')}\" hook {AgentKinds.Id(agent)}";

    public static bool IsInstalled(string file) =>
        Read(file)?["hooks"] is JsonObject hooks && hooks.Any(pair => pair.Value is JsonArray groups && groups.Any(IsOurs));

    /// <summary>Every event gets one group without a matcher (every tool). An approval may wait for the user, so it gets the full 600 s.</summary>
    public static void Install(AgentKind agent, string file, string executable)
    {
        var root = Read(file) ?? new JsonObject();
        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }
        foreach (var name in Events(agent))
        {
            var groups = hooks[name] as JsonArray ?? [];
            var kept = new JsonArray();
            foreach (var group in groups.ToList())
            {
                if (IsOurs(group)) continue;
                groups.Remove(group);
                kept.Add(group);
            }
            kept.Add((JsonNode)new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = Command(executable, agent),
                    ["timeout"] = name == "PermissionRequest" ? 600 : 5,
                }),
            });
            hooks[name] = kept;
        }
        Write(root, file);
    }

    public static void Uninstall(string file)
    {
        if (Read(file) is not { } root || root["hooks"] is not JsonObject hooks) return;
        foreach (var (name, value) in hooks.ToList())
        {
            if (value is not JsonArray groups) continue;
            var kept = new JsonArray();
            foreach (var group in groups.ToList())
            {
                if (IsOurs(group)) continue;
                groups.Remove(group);
                kept.Add(group);
            }
            if (kept.Count == 0) hooks.Remove(name);
            else hooks[name] = kept;
        }
        if (hooks.Count == 0) root.Remove("hooks");
        Write(root, file);
    }

    private static bool IsOurs(JsonNode? group) =>
        group?["hooks"] is JsonArray handlers && handlers.Any(handler =>
            handler?["command"] is JsonValue value && value.TryGetValue<string>(out var command)
            && command.ToLowerInvariant() is var lower
            && lower.Contains("isle8") && (lower.Contains(" hook claude") || lower.Contains(" hook codex")));

    private static JsonObject? Read(string file)
    {
        try
        {
            return System.IO.File.Exists(file) ? JsonNode.Parse(System.IO.File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject : null;
        }
        catch (JsonException)
        {
            // A file the agent can't read either is left for the user; writing over it would lose it.
            throw new InvalidDataException($"{file} is not valid JSON.");
        }
    }

    private static void Write(JsonObject root, string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var backup = file + ".isle8-backup";
        if (System.IO.File.Exists(file) && !System.IO.File.Exists(backup)) System.IO.File.Copy(file, backup);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            root.WriteTo(writer);
        }
        var temporary = file + ".isle8-new";
        System.IO.File.WriteAllBytes(temporary, stream.ToArray());
        System.IO.File.Move(temporary, file, overwrite: true);
    }
}
