// The MCP tool surface. Each session gets its own data directory (or Cosmos
// via AURELIA_COSMOS_ENDPOINT); the agent kind is remembered in a meta file
// so any later call can re-drive the session.
using System.ComponentModel;
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using ModelContextProtocol.Server;

namespace Aurelia.Mcp;

[McpServerToolType]
public static class AureliaTools
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private static string DataRoot =>
        Environment.GetEnvironmentVariable("AURELIA_MCP_DATA")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aurelia-mcp");

    [McpServerTool(Name = "aurelia_start_session")]
    [Description("Start a durable agent session. Returns immediately-meaningful state: "
        + "completed, failed (with compensation report), or awaiting_approval (resolve with "
        + "aurelia_resolve_approval). Sessions survive process death; resume any time with "
        + "aurelia_resume_session.")]
    public static async Task<string> StartSession(
        [Description("Agent: case-resolution | case-triage | concierge | echo")] string agent,
        [Description("The goal, e.g. 'credit for case 42' (case 7 needs manager approval)")] string goal,
        [Description("Sabotage customer notification to demo the saga (credit issued, then reversed)")]
        bool sabotageNotify = false,
        CancellationToken ct = default)
    {
        var sessionId = $"mcp-{DateTime.UtcNow:MMddHHmmss}-{Random.Shared.Next(1000, 9999)}";
        var dir = SessionDir(sessionId);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "meta.json"),
            JsonSerializer.Serialize(new { agent, sabotageNotify }), ct);

        var result = await DriveAsync(sessionId, goal, ct);
        return Envelope(sessionId, result.FinalStateJson);
    }

    [McpServerTool(Name = "aurelia_get_session")]
    [Description("Inspect a session: derived status, the full event timeline, and the side-effect "
        + "ledger. Everything comes from the durable event log.")]
    public static async Task<string> GetSession(
        [Description("The session id returned by aurelia_start_session")] string sessionId,
        CancellationToken ct = default)
    {
        var store = EventStores.FromEnvironment(SessionDir(sessionId));
        var events = await store.ReadAsync(sessionId, ct);
        if (events.Count == 0)
            return $"no session '{sessionId}'";

        var (status, _) = SessionStates.Derive(events);

        var ledgerPath = Path.Combine(SessionDir(sessionId), "ledger.jsonl");
        var ledger = File.Exists(ledgerPath) ? File.ReadAllLines(ledgerPath) : [];

        return JsonSerializer.Serialize(new
        {
            sessionId,
            status,
            events = events.Select(e => $"#{e.Sequence} {e.GetType().Name}").ToArray(),
            ledger,
        }, Pretty);
    }

    [McpServerTool(Name = "aurelia_resolve_approval")]
    [Description("Approve or deny a pending human-approval request, then resume the session. "
        + "The approval key comes from the awaiting_approval state.")]
    public static async Task<string> ResolveApproval(
        [Description("The session id")] string sessionId,
        [Description("The approval key, e.g. mcp-.../0/1")] string approvalKey,
        [Description("true to approve, false to deny")] bool approved,
        [Description("Who is deciding")] string approver = "mcp-client",
        CancellationToken ct = default)
    {
        var store = EventStores.FromEnvironment(SessionDir(sessionId));
        if (!await Approvals.ResolveAsync(store, sessionId, approvalKey, approved, approver, ct))
            return $"no pending approval '{approvalKey}' in session '{sessionId}'";
        var result = await DriveAsync(sessionId, goal: "", ct); // recorded goal wins on resume
        return Envelope(sessionId, result.FinalStateJson);
    }

    [McpServerTool(Name = "aurelia_resume_session")]
    [Description("Re-drive a session from its event log — after a crash, a pause, or just to "
        + "prove idempotence (completed sessions return their recorded state without re-executing "
        + "anything).")]
    public static async Task<string> ResumeSession(
        [Description("The session id")] string sessionId,
        CancellationToken ct = default)
    {
        var result = await DriveAsync(sessionId, goal: "", ct);
        return Envelope(sessionId, result.FinalStateJson);
    }

    [McpServerTool(Name = "aurelia_replay_session")]
    [Description("Deterministically replay a session: a pure fold over its event log with every "
        + "recorded result substituted — nothing re-executes. Returns the reconstructed final state.")]
    public static async Task<string> ReplaySession(
        [Description("The session id")] string sessionId,
        CancellationToken ct = default)
    {
        var store = EventStores.FromEnvironment(SessionDir(sessionId));
        var replayed = await new ReconstructionReplayEngine(store).ReplayAsync(sessionId, ct);
        return Envelope(sessionId, replayed.FinalStateJson);
    }

    private static string SessionDir(string sessionId)
    {
        SessionIds.Validate(sessionId);
        return Path.Combine(DataRoot, sessionId);
    }

    private static async Task<SessionResult> DriveAsync(string sessionId, string goal, CancellationToken ct)
    {
        var dir = SessionDir(sessionId);
        using var meta = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dir, "meta.json"), ct));
        var agent = meta.RootElement.GetProperty("agent").GetString() ?? "case-resolution";
        var sabotage = meta.RootElement.GetProperty("sabotageNotify").GetBoolean();

        var store = EventStores.FromEnvironment(dir);
        ISessionRuntime driver = agent switch
        {
            "case-triage" => CaseResolutionAgent.CreateTriageDriver(store, dir,
                OpenAICompatibleModelClient.FromEnvironmentOrFake(), sabotage,
                OpenAICompatibleModelClient.FallbackFromEnvironment()),
            "concierge" => CaseResolutionAgent.CreateConciergeDriver(store, dir,
                OpenAICompatibleModelClient.FromEnvironmentOrFake(), sabotage),
            "echo" => new ScriptedAgentDriver(store,
                new Dictionary<string, ITool> { ["echo"] = new BlockableEchoTool() },
                [new ScriptStep("echo", (g, _) => g)]),
            _ => CaseResolutionAgent.CreateDriver(store, dir, sabotage),
        };
        return await driver.RunToCompletionAsync(sessionId, goal, ct);
    }

    private static string Envelope(string sessionId, string finalStateJson) =>
        JsonSerializer.Serialize(new
        {
            sessionId,
            state = JsonSerializer.Deserialize<JsonElement>(finalStateJson),
        }, Pretty);
}
