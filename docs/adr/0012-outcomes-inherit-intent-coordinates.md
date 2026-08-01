# 0012 — Outcome events inherit their intent's turn/step coordinates

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

An outcome can arrive long after its intent — seconds for a tool call, days
for an approval (ADR-0011). The envelope stamps every event with turn/step
ordinals (ADR-0007), which raises whether an outcome is stamped with its
intent's coordinates or with its own position at arrival. Divergent stamps
would mean the key derived from coordinates (ADR-0005/0010) no longer
identifies both phases of the call.

## Decision

Both phases of a logical call carry the same turn/step coordinates: the
outcome inherits its intent's stamps. The key `session/turn/step` therefore
identifies the whole logical call. Temporal placement is not lost —
`Sequence` records actual arrival order in the log and `RecordedAt` the wall
time, both as observability data (ADR-0006).

## Consequences

Key derivation stays pure arithmetic over the stamps of either phase. Events
in the log are not monotonic in (turn, step) — a late outcome carries old
coordinates at a high sequence — so consumers must order by `Sequence`, never
by coordinates. Analytics on "which turn did the answer arrive in" use
`Sequence`/`RecordedAt` rather than an extra field.
