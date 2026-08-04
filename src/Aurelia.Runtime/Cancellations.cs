// Requesting cancellation from anywhere (ADR-0043). Mirrors Approvals: the
// signal is an appended event, so it survives the process that asked for it
// and is honoured whenever the session is next driven.
using Aurelia.Core;

namespace Aurelia.Runtime;

public static class Cancellations
{
    /// <summary>Records a cancellation request. Idempotent: the first request
    /// wins and later ones are no-ops. Returns false when the session is
    /// already terminal — there is nothing left to stop.</summary>
    public static async Task<bool> RequestAsync(
        IEventStore store, string sessionId, string reason, string requestedBy,
        CancellationToken ct = default)
    {
        // Bounded retry on sequence conflicts (ADR-0018): another writer may be
        // appending unrelated events while this lands.
        for (var attempt = 0; ; attempt++)
        {
            var log = await store.ReadAsync(sessionId, ct);
            if (log.Any(e => e is SessionCompleted or SessionFailed))
                return false;
            if (log.OfType<CancellationRequested>().Any())
                return true; // already asked; the first request stands

            var last = log.Count == 0 ? -1 : log[^1].Sequence;
            var lastTurn = log.OfType<CallRequested>().Max(r => (int?)r.TurnOrdinal) ?? 0;
            try
            {
                await store.AppendAsync(new CancellationRequested
                {
                    SessionId = sessionId,
                    Sequence = last + 1,
                    TurnOrdinal = lastTurn,
                    StepOrdinal = 0,
                    RecordedAt = DateTimeOffset.UtcNow,
                    Reason = reason,
                    RequestedBy = requestedBy,
                }, ct);
                return true;
            }
            catch (SequenceConflictException) when (attempt < 5)
            {
                // Someone else wrote first; re-read and try again.
            }
        }
    }

    /// <summary>The pending request, if this session has been asked to stop
    /// and has not yet reached a terminal event.</summary>
    public static CancellationRequested? PendingIn(IReadOnlyList<SessionEvent> log) =>
        log.Any(e => e is SessionCompleted or SessionFailed)
            ? null
            : log.OfType<CancellationRequested>().FirstOrDefault();
}
