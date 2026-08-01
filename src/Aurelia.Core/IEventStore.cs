// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE. Design before code: every shape here traces to an accepted
// ADR in docs/adr.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// Append-only store for session events.
///
/// TODO: sketch. The real contract needs optimistic concurrency
/// (expected-sequence append), batch reads from a checkpoint, and a decision
/// on snapshot support — queued for the durable store (Cosmos) design round.
/// </summary>
public interface IEventStore
{
    Task<long> AppendAsync(SessionEvent evt, CancellationToken ct = default);

    Task<IReadOnlyList<SessionEvent>> ReadAsync(string sessionId, CancellationToken ct = default);
}
