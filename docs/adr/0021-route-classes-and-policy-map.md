# 0021 — Routing is route classes resolved by a policy map

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Something must decide which backend serves a model call. Content-based
dynamic routing (a heuristic or model inspects the request) makes the router
itself a nondeterminism source that must be recorded as its own call —
meaningful machinery, unproven benefit at two agents. Delegating entirely to
Azure's `model-router` deployment leaves Aurelia with no routing behavior of
its own to measure, making the Phase 2 exit gate ("policy B cut latency X%")
unwritable.

## Decision

Each `ModelStep` declares a **route class** — the kind of work it is
(`classify`, `plan`, `general`). A **routing policy** (named, versioned by its
name) maps route class → a `ModelRoute`: primary backend, optional fallback
backend, optional latency budget. Resolution is a pure lookup — deterministic,
auditable, replay-safe. Azure's `model-router` remains available *as a
backend* inside any route. Content-based routing can arrive later as a new
policy type without touching the schema, since the intent records whatever
the policy decided (ADR-0020).

## Consequences

Routing is boring by design: same step + same policy = same choice, which is
exactly what recovery and replay need. Policies are code/config, compared by
running the same eval suite under different policies — the measurement path
for the Phase 2 exit gate. The step author must think about what class of
work each model call is, which is a feature.
