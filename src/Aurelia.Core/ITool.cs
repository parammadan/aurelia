// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE — HANDWRITTEN ZONE (owner: Param). Interfaces and placeholder
// stubs only; see src/Aurelia.Core/README.md and ADR-0003.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// A tool the runtime can invoke on behalf of an agent.
///
/// TODO(param): sketch. The real contract adds retry policy, side-effect
/// classification, and saga compensation for effectful tools. The one
/// deliberate day-one choice is that every execution carries an idempotency
/// key, because crash recovery re-executes unfinished tool calls.
/// </summary>
public interface ITool
{
    string Name { get; }

    Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default);
}
