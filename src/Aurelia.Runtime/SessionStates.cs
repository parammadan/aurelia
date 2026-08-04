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
            // A cancelled session is terminal-failed (the goal was not met),
            // but the log says WHY, so the derived status says so too (ADR-0043).
            return (events.OfType<CancellationRequested>().Any() ? "cancelled" : "failed", null);
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

    /// <summary>The terminal JSON for a failed session, cancellation-aware.
    /// The driver and the replay engine BOTH call this, so a cancelled session
    /// replays to exactly the state it reported live (ADR-0043).</summary>
    public static string TerminalJson(IReadOnlyList<SessionEvent> log, SessionFailed failed)
    {
        var cancellation = log.OfType<CancellationRequested>().FirstOrDefault();
        return cancellation is null
            ? FailedJson(failed)
            : JsonSerializer.Serialize(new
            {
                status = "cancelled",
                reason = failed.Reason,
                requestedBy = cancellation.RequestedBy,
                compensatedKeys = failed.CompensatedKeys,
                uncompensatedKeys = failed.UncompensatedKeys,
                unrecoverableKeys = failed.UnrecoverableKeys,
            });
    }

    public static string FailedJson(SessionFailed failed) =>
        JsonSerializer.Serialize(new
        {
            status = "failed",
            reason = failed.Reason,
            compensatedKeys = failed.CompensatedKeys,
            uncompensatedKeys = failed.UncompensatedKeys,
            unrecoverableKeys = failed.UnrecoverableKeys,
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
