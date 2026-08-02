# 0022 — A latency budget breach is an attempt failure that falls back

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Latency budgets are a first-class runtime policy (Phase 2 mandate). Treating
a breach as terminal turns a slow model into a failed customer interaction
when a fallback could have served; not shipping budgets now means retrofitting
the timeout path into the same retry loop later.

## Decision

A `ModelRoute` may carry a latency budget. The budget applies to the
**primary attempt**: on breach, the attempt is cancelled and recorded as a
normal `CallAttemptFailed` ("latency budget exceeded (N ms)"), and the next
attempt — same idempotency key (ADR-0010) — runs on the **fallback backend**,
unbudgeted, as the last resort. Which model actually served is recorded in
`ModelCallSucceeded.ModelId` (ADR-0020); under fallback it differs from the
intent's routed `ModelId`, and the log narrates exactly that. A breach with
no fallback follows the ordinary retry-then-terminal path into the saga
unwind.

## Consequences

Slow backends degrade gracefully instead of failing sessions, and the
degradation is measurable from the log (count outcomes where served ≠
routed). The budget cancellation must be distinguished from caller
cancellation — a timeout is an attempt failure, a real cancellation still
aborts the activation. Fallback answers may be lower quality than primary
answers; that trade is the policy author's, made explicit per route.
