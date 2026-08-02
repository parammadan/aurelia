# 0020 — The routing decision is recorded on the model intent

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Routing decisions must be part of the durable record — "why did this call go
to the expensive model?" has to be answerable from the log alone, and replay
must see the decision (Phase 2 mandate). A separate `RoutingDecided` event
doubles the events per model call and adds a second thing recovery must pair
up; leaving routing in configuration makes the log mute on the question.

## Decision

`ModelCallRequested` carries the decision: optional `RouteClass` (what kind of
work), `RoutingPolicy` (which policy decided), and `RoutingReason` fields,
alongside the already-recorded `ModelId` (who was chosen). This bumps
`ModelCallRequested` to **schemaVersion 2** — the first real use of ADR-0009:
a registered upcaster lifts every stored v1 event on read; the new fields are
null for pre-routing history. `ModelCallSucceeded` also moves to v2, gaining
a `ModelId` field recording which model *actually served* (see ADR-0022 —
under fallback it can differ from the intent's choice).

## Consequences

The intent remains the single source of call provenance; no new event types.
Every store now ships with the standard upcaster registry by default. Old
logs — including sessions already in Cosmos — read back as v2 without
migration, proving the ADR-0009 machinery on real history.
