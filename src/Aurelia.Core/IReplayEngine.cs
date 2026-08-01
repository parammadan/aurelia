// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE. Design before code: every shape here traces to an accepted
// ADR in docs/adr.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// Deterministic replay of a recorded session. Phase 1 promises pure
/// reconstruction (ADR-0004): a fold over the event log with every external
/// result substituted from the recording; nothing is re-executed.
/// Re-execution with substitution is reserved for later (matching rules fixed
/// by ADR-0005).
///
/// TODO: placeholder with no implementation yet; lands with the trace-replay
/// design round.
/// </summary>
public interface IReplayEngine
{
    Task<SessionResult> ReplayAsync(string sessionId, CancellationToken ct = default);
}
