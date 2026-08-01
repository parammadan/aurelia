// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE — HANDWRITTEN ZONE (owner: Param). Interfaces and placeholder
// stubs only; see src/Aurelia.Core/README.md and ADR-0003.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// Deterministic replay of a recorded session: re-execute from the event log
/// with model and tool calls substituted from recorded results.
///
/// TODO(param): interface is a placeholder with no implementation anywhere.
/// The determinism rules — what is substituted vs. re-executed, and how
/// divergence from the recording is detected and reported — are a core design
/// decision that lands as an ADR first.
/// </summary>
public interface IReplayEngine
{
    Task<SessionResult> ReplayAsync(string sessionId, CancellationToken ct = default);
}
