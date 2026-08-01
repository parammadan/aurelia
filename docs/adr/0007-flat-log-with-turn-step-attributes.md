# 0007 — Flat log with turn/step attributes

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

The key scheme of ADR-0005 (`session/turn/step`) requires "turn" and "step" to
be well-defined, which raises how logical structure is represented in the log:
flat attributes on every event, an event DAG with causation pointers, or
structure inferred from event types during the fold. Inference makes key
derivation hostage to taxonomy changes; a DAG turns folding, replay, and the
trace viewer into graph problems while a total order is still needed for
storage anyway.

## Decision

Each session has one flat, totally-ordered event log (which maps to a Cosmos
partition per session later). Every event's envelope is stamped with its turn
ordinal and step ordinal. A **turn** is one activation of the agent — a
user-input-to-response cycle, or one autonomous activation for unattended
agents. A **step** is one external call issued within a turn; step ordinals
are assigned in issue order, which also covers parallel calls within a turn.

## Consequences

Keys derive directly from envelope stamps rather than from a fold, and the
structure is queryable and indexable as-is. Rich causal provenance (which
event caused which) is not modeled; if it is ever needed, a causation
attribute can be added to the envelope without restructuring the log.
