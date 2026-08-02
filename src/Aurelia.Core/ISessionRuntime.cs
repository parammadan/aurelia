// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE. Design before code: every shape here traces to an accepted
// ADR in docs/adr.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// Drives a session from its event log. Must be safe to call on a session in
/// any state: fresh, mid-run after a crash, or already terminal. That
/// property — resumability from the log alone — is the contract the
/// crash-recovery harness enforces (ADR-0002).
///
/// Two entry modes over one log (ADR-0028): single-shot activations
/// (<see cref="RunToCompletionAsync"/> — goal-driven and queue agents,
/// terminal semantics) and conversations
/// (<see cref="HandleTurnAsync"/> — each user message is one turn; the
/// session stays open until explicitly failed or closed).
/// </summary>
public interface ISessionRuntime
{
    Task<SessionResult> RunToCompletionAsync(string sessionId, string goal, CancellationToken ct = default);

    /// <summary>Executes one conversation turn: records the user message
    /// (turn N), runs the agent's script scoped to that turn, records the
    /// agent's reply. Re-invoking after a crash resumes the interrupted turn
    /// (the recorded user message wins).</summary>
    Task<SessionResult> HandleTurnAsync(string sessionId, string userMessage, CancellationToken ct = default);
}

public sealed record SessionResult(string SessionId, string FinalStateJson);
