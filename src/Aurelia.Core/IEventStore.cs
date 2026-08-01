// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE — HANDWRITTEN ZONE (owner: Param). Interfaces and placeholder
// stubs only; see src/Aurelia.Core/README.md and ADR-0003.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// Append-only store for session events.
///
/// TODO(param): sketch. The real contract needs optimistic concurrency
/// (expected-sequence append), batch reads from a checkpoint, and a decision
/// on snapshot support — all pending the event schema ADR.
/// </summary>
public interface IEventStore
{
    Task<long> AppendAsync(SessionEvent evt, CancellationToken ct = default);

    Task<IReadOnlyList<SessionEvent>> ReadAsync(string sessionId, CancellationToken ct = default);
}
