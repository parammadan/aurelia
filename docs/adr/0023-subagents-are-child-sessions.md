# 0023 — Sub-agents are child sessions behind a two-phase call

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Multi-agent coordination needs a durable shape. Embedding a child's events
inline in the parent's log kills independent child recovery and collides with
the parent's coordinate scheme; a plain grain call is invisible to replay and
recovery, breaking ADR-0004's promise.

## Decision

A sub-agent invocation is an external call under the standard two-phase rule
(ADR-0008): `AgentCallRequested` records the agent name, the handed-off goal,
and a **deterministic child session id** derived from the parent
(`{parentSessionId}.{stepOrdinal}`); `AgentCallSucceeded` records the child's
final state. The child runs as a **full session of its own** — own log, own
crash recovery, own retries, own saga — in the same event store.

Durability composes: recovering the parent re-drives the child, and the
child's own log resumes it from wherever it died. A child that pauses (e.g.,
awaiting approval, ADR-0011) pauses the parent for free — the parent's call
has no outcome, so re-driving the parent re-drives the child, which resumes
when resolved. No new pause machinery.

## Consequences

The parent's log stays small (one call pair per child); the full child story
lives one deterministic id away, which the trace viewer can link to.
Arbitrary-depth nesting works by induction. Cross-session queries ("all
children of X") lean on the id convention until a real index exists.

## Amendment (2026-08-02)

Multi-turn conversations (ADR-0028) made step ordinals turn-scoped, so the
step ordinal alone no longer identifies a call within a session. The child
session id is therefore
**`{parentSessionId}.{turnOrdinal}.{stepOrdinal}`** (e.g. `front.0.1`).
The code has produced this format since conversations landed; this amendment
brings the ADR text in line with it (flagged by the 2026-08-02 audit).
