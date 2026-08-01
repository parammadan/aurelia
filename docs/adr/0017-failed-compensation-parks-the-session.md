# 0017 — Failed compensation parks the session as SessionFailed

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

The nightmare case: the refund succeeded, a later step failed terminally, and
now the refund reversal is failing too. Retrying forever pins the session open
with no escalation path; giving up silently is a money-losing quiet failure —
exactly what this runtime exists to eliminate. Something must be terminal, and
a human must be able to find it.

## Decision

A failing compensation retries under its own key per the normal retry policy.
On exhaustion, a terminal `CallFailed` is recorded for the compensation and
the session ends with a new lifecycle event, `SessionFailed`, carrying the
failure reason and the explicit lists of compensated and uncompensated call
keys. A `SessionFailed` session is parked: it requires human resolution, which
will arrive through the ADR-0011 approval primitive in a later round.

## Consequences

Nothing is silent and nothing blocks forever: the fold alone answers "what was
unwound and what leaked." Sessions gain a second terminal state, which replay
and every consumer must handle (exactly one of `SessionCompleted` or
`SessionFailed`). Operational dashboards get a precise, queryable definition
of "needs a human."
