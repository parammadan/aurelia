# 0010 — Keys identify logical calls; attempts are numbered

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

ADR-0005 fixed that keys are deterministic; retries force the question of what
a key identifies. A key that changes per attempt makes attempt two of a refund
look like a second refund — exactly-once collapses to at-least-once precisely
where it matters. Runtime-generated GUIDs survive crash recovery (keys are
read back from the log) but cannot be re-derived, which forecloses the
re-execution replay mode ADR-0004 reserves.

## Decision

The deterministic key `session/turn/step` (ordinals per ADR-0007) identifies
the **logical call** and is identical across every retry attempt. It is the
idempotency key presented to tools and downstream systems on each attempt, so
they can deduplicate. Each outcome event carries an attempt number; a logical
call's history is one intent, zero or more numbered failures, and at most one
terminal outcome. Replay substitutes the terminal outcome (ADR-0004).

## Consequences

Exactly-once for effectful tools becomes a contract the runtime can actually
offer: dedupe by key downstream, compensation by saga when dedupe is
impossible. Key derivation is pure arithmetic over envelope stamps — no fold,
no state, re-derivable during future re-execution replay. The key scheme is
now part of the core schema and changes only by superseding this ADR.
