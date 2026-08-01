// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE. Design before code: every shape here traces to an accepted
// ADR in docs/adr.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// A tool the runtime can invoke on behalf of an agent. Every execution
/// carries an idempotency key because crash recovery re-executes unfinished
/// calls (ADR-0010).
///
/// TODO: retry policy declaration is still pending its own design round.
/// </summary>
public interface ITool
{
    string Name { get; }

    Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default);
}

/// <summary>
/// Opt-in compensability (ADR-0016): the runtime discovers this by type
/// during a saga unwind (ADR-0015) and reverses succeeded calls in reverse
/// order of success. The compensating action receives the original call's
/// arguments and result, plus its own idempotency key.
/// </summary>
public interface ICompensatableTool : ITool
{
    Task<string> CompensateAsync(
        string originalArgumentsJson,
        string originalResultJson,
        string compensationKey,
        CancellationToken ct = default);
}
