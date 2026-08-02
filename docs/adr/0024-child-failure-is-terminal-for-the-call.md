# 0024 — A failed child is immediately terminal for the parent's call

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

A child session that ends in `SessionFailed` has already run its own unwind
(ADR-0017) — its side effects are compensated or explicitly reported leaked by
the time the parent hears about it. Re-driving the same child session returns
the same sticky terminal state, so retrying is pointless; retrying with a
*fresh* child session id changes the child's idempotency keys, so effects from
the failed attempt and the retry can both land — a dangerous default.

## Decision

When a child returns `failed`, the parent records a terminal `CallFailed` for
the agent call (no attempt loop) and runs its own parent-level unwind
(ADRs 0015–0017). Nested compensation is composition, not machinery: each
level compensates exactly the effects it owns.

## Consequences

Failure semantics are recursive and predictable at any nesting depth. A
genuinely-retryable child (fresh id, new effects) is a deliberate future
policy knob, not a default. Escalating a child failure to a human is done
explicitly with an `ApprovalGate` step where wanted.
