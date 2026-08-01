// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE — HANDWRITTEN ZONE (owner: Param). Interfaces and placeholder
// stubs only; see src/Aurelia.Core/README.md and ADR-0003. AI agents must not
// add or modify logic in this project.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// Envelope for every immutable session event. Current state is a fold over
/// the event log; the log is the only durable truth.
///
/// TODO(param): placeholder shape. The real schema — causality, schema
/// versioning, and capture of every nondeterminism source (model calls, tool
/// results, time, randomness, config versions) — is the central Phase 1
/// design decision and lands as an ADR before this changes.
/// </summary>
public abstract record SessionEvent
{
    public required string SessionId { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
}

// TODO(param): placeholder events — the minimum surface for the crash-recovery
// harness to assert against. To be replaced by the real event taxonomy.

public sealed record SessionStarted : SessionEvent
{
    public required string Goal { get; init; }
}

public sealed record ToolCallRequested : SessionEvent
{
    public required string ToolName { get; init; }
    public required string ArgumentsJson { get; init; }
    public required string IdempotencyKey { get; init; }
}

public sealed record ToolCallCompleted : SessionEvent
{
    public required string IdempotencyKey { get; init; }
    public required string ResultJson { get; init; }
}

public sealed record SessionCompleted : SessionEvent
{
    public required string FinalStateJson { get; init; }
}
