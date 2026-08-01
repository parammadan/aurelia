// Resolving an approval is appending its outcome event — from any process,
// at any time. The outcome inherits the intent's coordinates (ADR-0012) and
// resolution is idempotent by key (ADR-0010).
using Aurelia.Core;

namespace Aurelia.Runtime;

public static class Approvals
{
    /// <summary>Returns false if no such approval request exists; resolving
    /// an already-resolved approval is a no-op that returns true.</summary>
    public static async Task<bool> ResolveAsync(
        IEventStore store, string sessionId, string approvalKey,
        bool approved, string approverId, CancellationToken ct = default)
    {
        var log = await store.ReadAsync(sessionId, ct);
        var pending = log.OfType<ApprovalRequested>()
            .FirstOrDefault(r => r.IdempotencyKey == approvalKey);
        if (pending is null)
            return false;
        if (log.OfType<ApprovalResolved>().Any(o => o.IdempotencyKey == approvalKey))
            return true;

        await store.AppendAsync(new ApprovalResolved
        {
            SessionId = sessionId,
            Sequence = log[^1].Sequence + 1,
            TurnOrdinal = pending.TurnOrdinal,
            StepOrdinal = pending.StepOrdinal,
            RecordedAt = DateTimeOffset.UtcNow,
            Attempt = 1,
            Approved = approved,
            ApproverId = approverId,
        }, ct);
        return true;
    }
}
