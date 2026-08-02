# 0025 — Parent and child share state by explicit handoff only

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Chatty collaboration wants shared memory, but every read of a shared mutable
store is a nondeterminism source that must become a recorded event (the
ADR-0006 argument, applied to memory), and letting the child read the parent's
log couples the child to the parent's schema and makes solo replay of the
child ill-defined.

## Decision

The only state that crosses the boundary is recorded on the call events: the
child receives a goal built from the parent's recorded results
(`AgentCallRequested.Goal`), and returns its final state
(`AgentCallSucceeded.ResultJson`). Nothing else crosses. Session-scoped memory
tiers, when they arrive, get their own design round and their own recorded
read/write events.

## Consequences

Recovery and replay of either session are self-contained. Handoffs are
auditable in the trace viewer as ordinary call payloads. Agents that need
richer shared context must say so explicitly in their goals — verbosity
traded for determinism, deliberately.
