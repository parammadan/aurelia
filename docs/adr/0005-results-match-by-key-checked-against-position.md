# 0005 — Recorded results match by deterministic key, checked against position

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Crash recovery today — and re-execution replay later (ADR-0004) — must pair a
call the runtime is about to make with a result already in the log. Temporal
matches strictly by history position, which is simple but makes any change to
orchestration logic between recording and replay a breaking event (workflow
versioning is Temporal's most notorious operational pain). Matching purely by
key tolerates code evolution but has no tripwire: a buggy key scheme silently
returns the wrong recorded result.

## Decision

Every external call — tool invocation, model call, and injected time or
randomness per ADR-0006 — carries a deterministic ID derived from its position
in the session's logical structure (session/turn/step). The idempotency key
already present in `ITool` generalizes to all call types. Matching is by key,
**and** the log position is asserted to agree; disagreement is a loud
divergence error, never a silent fallback to either scheme alone.

## Consequences

The key derivation scheme becomes part of the core schema and must itself be
deterministic — it is designed alongside the event envelope. The envelope
carries both key and sequence. Orchestration logic can evolve between
recording and recovery without breaking matching, while a wrong or colliding
key surfaces immediately instead of corrupting a session.
