// ─────────────────────────────────────────────────────────────────────────────
// AURELIA CORE. Design before code: every shape in this file traces to an
// accepted ADR in docs/adr — see 0004–0013 for the event model.
// ─────────────────────────────────────────────────────────────────────────────
namespace Aurelia.Core;

/// <summary>
/// Envelope for every immutable session event. Current state is a fold over
/// the log; the log is the only durable truth.
///
/// Coordinates (ADR-0007): each session is one flat, totally-ordered log.
/// <see cref="TurnOrdinal"/> identifies the activation (a user-input-to-
/// response cycle, or one autonomous activation) and <see cref="StepOrdinal"/>
/// the external call within it, assigned in issue order. Outcome events
/// inherit their intent's coordinates (ADR-0012), so consumers must order by
/// <see cref="Sequence"/>, never by coordinates.
///
/// <see cref="RecordedAt"/> is observability metadata only (ADR-0006): core
/// logic never reads it, and it never influences a decision. Logical time
/// arrives through <see cref="TimeRequested"/>/<see cref="TimeProvided"/>.
///
/// <see cref="SchemaVersion"/> increments per event type when its shape
/// changes; upcasters lift stored events to the current shape on read
/// (ADR-0009).
/// </summary>
public abstract record SessionEvent
{
    public required string SessionId { get; init; }
    public required long Sequence { get; init; }
    public required int TurnOrdinal { get; init; }
    public required int StepOrdinal { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public required DateTimeOffset RecordedAt { get; init; }
}

// ── Session lifecycle ────────────────────────────────────────────────────────

public sealed record SessionStarted : SessionEvent
{
    public required string Goal { get; init; }
}

/// <summary>Opens a new turn (ADR-0007).</summary>
public sealed record UserMessageReceived : SessionEvent
{
    public required string Content { get; init; }
}

public sealed record AgentMessageSent : SessionEvent
{
    public required string Content { get; init; }
}

public sealed record SessionCompleted : SessionEvent
{
    public required string FinalStateJson { get; init; }
}

// ── External calls: intent phase (ADR-0008) ──────────────────────────────────
// An intent is durable BEFORE execution begins. An intent with no terminal
// outcome means the call may have executed and may have had side effects.

/// <summary>
/// Base for all intent events. The idempotency key identifies the logical
/// call, is identical across retry attempts, and is pure arithmetic over the
/// envelope stamps (ADR-0005, ADR-0010) — never stored state.
/// </summary>
public abstract record CallRequested : SessionEvent
{
    public string IdempotencyKey => $"{SessionId}/{TurnOrdinal}/{StepOrdinal}";
}

public sealed record ToolCallRequested : CallRequested
{
    public required string ToolName { get; init; }
    public required string ArgumentsJson { get; init; }
}

/// <summary>
/// Records the FULL inputs — exact prompt, model, parameters, config version —
/// so that re-execution and counterfactual replay remain possible without a
/// schema migration (ADR-0004).
/// </summary>
public sealed record ModelCallRequested : CallRequested
{
    public required string ModelId { get; init; }
    public required string PromptJson { get; init; }
    public required string ParametersJson { get; init; }
    public required string ConfigVersion { get; init; }
}

/// <summary>
/// Human approval is a slow external call (ADR-0011): "paused awaiting
/// approval" is a derived condition — this intent without an outcome — not a
/// stored state. Policy and optional expiry per ADR-0013.
/// </summary>
public sealed record ApprovalRequested : CallRequested
{
    public required string Question { get; init; }
    public required string ApproverPolicy { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>Injected logical time (ADR-0006): core asks, the runtime answers
/// with a recorded <see cref="TimeProvided"/>.</summary>
public sealed record TimeRequested : CallRequested;

/// <summary>Injected randomness (ADR-0006).</summary>
public sealed record RandomRequested : CallRequested
{
    public int? MaxExclusive { get; init; }
}

// ── External calls: outcome phase (ADR-0008, ADR-0010, ADR-0012) ─────────────
// Outcomes inherit their intent's turn/step coordinates; a logical call is one
// intent, zero or more numbered attempt failures, and at most one terminal
// outcome. Replay substitutes the terminal outcome (ADR-0004).

public abstract record CallOutcome : SessionEvent
{
    public required int Attempt { get; init; }

    public string IdempotencyKey => $"{SessionId}/{TurnOrdinal}/{StepOrdinal}";
}

public sealed record ToolCallSucceeded : CallOutcome
{
    public required string ResultJson { get; init; }
}

public sealed record ModelCallSucceeded : CallOutcome
{
    public required string OutputJson { get; init; }
}

public sealed record ApprovalResolved : CallOutcome
{
    public required bool Approved { get; init; }
    public required string ApproverId { get; init; }
}

/// <summary>Terminal outcome produced when an approval's expiry fires; the
/// expiry itself arrives as an injected-time event (ADR-0013).</summary>
public sealed record ApprovalExpired : CallOutcome;

public sealed record TimeProvided : CallOutcome
{
    public required DateTimeOffset Value { get; init; }
}

public sealed record RandomProvided : CallOutcome
{
    public required int Value { get; init; }
}

/// <summary>Non-terminal: attempt <see cref="CallOutcome.Attempt"/> failed and
/// the call will be retried under the same key (ADR-0010).</summary>
public sealed record CallAttemptFailed : CallOutcome
{
    public required string Error { get; init; }
}

/// <summary>Terminal failure: retries exhausted or the error is not
/// retryable. For side-effectful calls this is where saga compensation is
/// triggered (contract pending its own ADR).</summary>
public sealed record CallFailed : CallOutcome
{
    public required string Error { get; init; }
}
