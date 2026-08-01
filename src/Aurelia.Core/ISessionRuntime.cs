// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE. Design before code: every shape here traces to an accepted
// ADR in docs/adr.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// Drives a session from its event log to completion. Must be safe to call on
/// a session in any state: fresh, mid-run after a crash, or already complete.
/// That property — resumability from the log alone — is the contract the
/// crash-recovery harness enforces (ADR-0002).
///
/// TODO: sketch. The real execution model (turn loop, model calls,
/// pause/resume for human approval) replaces this single-shot signature.
/// </summary>
public interface ISessionRuntime
{
    Task<SessionResult> RunToCompletionAsync(string sessionId, string goal, CancellationToken ct = default);
}

public sealed record SessionResult(string SessionId, string FinalStateJson);
