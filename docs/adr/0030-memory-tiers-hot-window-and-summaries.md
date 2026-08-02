# 0030 — Memory tiers: hot window plus durable summary checkpoints

- **Status:** Accepted
- **Date:** 2026-08-02
- **Author:** Param Madan

## Context

Long conversations cannot ship their full history into every prompt, and a
memory system whose compression is invisible to the log would break replay:
the same turn replayed later would have seen different context.

## Decision

Three tiers. **Hot:** the last K turns verbatim (K is driver policy).
**Warm:** every M turns the runtime issues a *summarization model call* —
route class `summarize` (ADR-0021), durable two-phase like any model call —
and records a new `SummaryCheckpointed` event (`Summary`, `ThroughTurn`).
The window then presents the summary in place of turns ≤ `ThroughTurn`.
**Cold:** the full event log, always. Because the summary is itself a
recorded event produced by a recorded call, memory compression replays: any
turn's reconstructed context is exactly what the live run saw.

## Consequences

Prompt growth is bounded; summary quality becomes a measurable eval concern
(the eval suite can diff answers under different K/M). One new event type,
one entry in the type map, versioned per ADR-0009. Vector/semantic recall is
explicitly out of scope until it earns its own phase.
