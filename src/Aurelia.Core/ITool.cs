// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE. Design before code: every shape here traces to an accepted
// ADR in docs/adr.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// A tool the runtime can invoke on behalf of an agent.
///
/// TODO: sketch. The real contract adds retry policy, side-effect
/// classification, and saga compensation for effectful tools. The one
/// deliberate day-one choice is that every execution carries an idempotency
/// key, because crash recovery re-executes unfinished tool calls.
/// </summary>
public interface ITool
{
    string Name { get; }

    Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default);
}
