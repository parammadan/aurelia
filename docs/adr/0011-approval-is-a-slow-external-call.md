# 0011 — Human approval is a slow external call

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

The human-in-the-loop primitive — an agent pauses awaiting approval
indefinitely and resumes with full context — needs a representation in the
event log. Dedicated `SessionPaused`/`SessionResumed` lifecycle events read
explicitly but create a second recovery mechanism that must stay consistent
with the call machinery forever; a hybrid records the same fact in two places.

## Decision

An approval is modeled as an external call under the existing two-phase rule
(ADR-0008): `ApprovalRequested` is an intent, durable before the session
pauses; `ApprovalResolved` (or `Expired`, per ADR-0013) is its outcome. There
is no separate pause machinery. "Paused" is a derived condition — the latest
intent has no outcome — not a stored state.

## Consequences

Recovery of a paused session is identical to recovery of a crashed one:
intent-without-outcome, wait or re-dispatch. Keys and dedupe (ADR-0010) apply
unchanged, so a double-submitted approval resolves once. Anything that renders
"is this session paused?" computes it from the fold; that cost is accepted in
exchange for having exactly one recovery semantics in the runtime.
