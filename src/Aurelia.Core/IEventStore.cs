// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE. Design before code: every shape here traces to an accepted
// ADR in docs/adr.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// Append-only store for session events.
///
/// Concurrency (ADR-0018): an append succeeds only if the event's
/// <see cref="SessionEvent.Sequence"/> is exactly the last stored sequence
/// plus one for that session, asserted atomically; otherwise the store throws
/// <see cref="SequenceConflictException"/> and writes nothing. A stale writer
/// therefore cannot corrupt a session log.
///
/// TODO: batch reads from a checkpoint and snapshot support — queued for the
/// durable store (Cosmos) design round.
/// </summary>
public interface IEventStore
{
    /// <exception cref="SequenceConflictException">The event's sequence is not
    /// contiguous with the stored log (ADR-0018).</exception>
    Task<long> AppendAsync(SessionEvent evt, CancellationToken ct = default);

    Task<IReadOnlyList<SessionEvent>> ReadAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>Thrown when an append's sequence is not exactly one past the last
/// stored event — the writer folded a stale view of the log (ADR-0018). The
/// activation should fail loudly; the caller re-invokes against the fresh log.</summary>
public sealed class SequenceConflictException(string sessionId, long attemptedSequence, long lastSequence)
    : Exception($"Append to session '{sessionId}' rejected: attempted sequence "
        + $"{attemptedSequence}, but the log ends at {lastSequence}. The writer is stale.");
