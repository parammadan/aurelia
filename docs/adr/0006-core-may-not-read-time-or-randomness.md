# 0006 — Core code may not read time or randomness directly

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Wall-clock reads and random values are the sneaky nondeterminism sources: they
influence decisions without looking like external calls, and every uncaptured
read is a silent replay divergence discovered only during production
debugging. The ambiguity is already live — `ProvisionalSessionDriver` stamps
`RecordedAt` from the wall clock today.

## Decision

Core execution logic never reads the wall clock or randomness directly
(Temporal's rule). Any time or random value that can influence a decision is
supplied by the runtime as an injected input, recorded as an event, and keyed
per ADR-0005 so recovery and replay substitute the recorded value.

The event envelope distinguishes **observability metadata** from **decision
inputs**: `RecordedAt` is runtime-supplied metadata for humans and dashboards
and may never be read by core logic; logical time used in decisions arrives
only through the injected, recorded path.

Enforcement is by code review at the core boundary now; a Roslyn analyzer that
bans the forbidden APIs inside `src/Aurelia.Core` is planned perimeter work.

## Consequences

Core code pays a ceremony cost for something as small as "what time is it."
In exchange, replay determinism (ADR-0004) is enforceable by tooling rather
than by discipline, and the perimeter keeps a clear rule for what it must
capture on the core's behalf.
