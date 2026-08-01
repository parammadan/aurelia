# 0008 — External calls are two-phase: intent then outcome

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Crash recovery must distinguish "this call was never attempted" from "this
call may have executed and had side effects" — the entire compensation problem
for effectful tools hangs on that distinction. A single event written after
completion cannot make it: a crash mid-call leaves no trace. The existing
crash harness already keys off intent-without-outcome as the mid-flight
signature.

## Decision

Every external call — tool invocation, model call, injected time/randomness —
produces two phases of events. The **intent** event (full inputs and the
deterministic key, per ADR-0004 and ADR-0005) is durable *before* execution
begins. The **outcome** — succeeded with result, or failed with error and
attempt number — follows. An intent with no terminal outcome means the call
may have executed; recovery re-executes it under the same idempotency key.
Per-attempt start events (a three-phase shape) are not recorded; attempt
history is carried on failure outcomes instead.

## Consequences

The log doubles as the recovery protocol: the fold alone tells the runtime
exactly where a session died and what may have leaked side effects. Event
volume is roughly two per call plus one per failed attempt, which is accepted.
The exactly-once story for effectful tools rests on downstream deduplication
by idempotency key (ADR-0010) plus saga compensation, designed later in
Phase 1.
