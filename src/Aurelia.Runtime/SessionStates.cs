// The single source of the derived-state JSON shapes, so the driver (which
// returns them live) and the replay engine (which reconstructs them) can never
// drift apart.
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public static class SessionStates
{
    /// <summary>
    /// The single derivation of a session's status from its event log
    /// (completed / failed / awaiting_approval / in_flight), plus the pending
    /// approval when there is one. Every surface (playground, trace viewer,
    /// MCP) uses this so they cannot drift.
    /// </summary>
    public static (string Status, ApprovalRequested? PendingApproval) Derive(IReadOnlyList<SessionEvent> events)
    {
        if (events.Any(e => e is SessionCompleted))
            return ("completed", null);
        if (events.Any(e => e is SessionFailed))
            return ("failed", null);
        var pending = events.OfType<ApprovalRequested>().FirstOrDefault(r =>
            !events.OfType<ApprovalResolved>().Any(o => o.IdempotencyKey == r.IdempotencyKey));
        return pending is not null ? ("awaiting_approval", pending) : ("in_flight", null);
    }

    /// <summary>Derives the status from a final-state JSON: the value of its
    /// "status" property, or "completed" when none is present.</summary>
    public static string StatusOf(string finalStateJson)
    {
        using var doc = JsonDocument.Parse(finalStateJson);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("status", out var status)
            ? status.GetString() ?? "completed"
            : "completed";
    }

    public static string FailedJson(SessionFailed failed) =>
        JsonSerializer.Serialize(new
        {
            status = "failed",
            reason = failed.Reason,
            compensatedKeys = failed.CompensatedKeys,
            uncompensatedKeys = failed.UncompensatedKeys,
        });

    public static string AwaitingApprovalJson(ApprovalRequested approval) =>
        JsonSerializer.Serialize(new
        {
            status = "awaiting_approval",
            approvalKey = approval.IdempotencyKey,
            question = approval.Question,
            approverPolicy = approval.ApproverPolicy,
        });

    public static string AwaitingSubAgentJson(AgentCallRequested call, string childStateJson) =>
        JsonSerializer.Serialize(new
        {
            status = "awaiting_subagent",
            agent = call.AgentName,
            childSessionId = call.ChildSessionId,
            childState = JsonSerializer.Deserialize<JsonElement>(childStateJson),
        });
}
