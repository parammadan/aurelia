# 0015 — Compensation is an external call in the log

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

When a session fails terminally after side-effectful calls have succeeded,
those effects must be undone (the saga pattern). A one-shot "compensation
executed" event recorded after the fact is blind to crashes mid-compensation —
the exact blindness ADR-0008 exists to prevent, here on the most
safety-critical path in the system. Tracking compensation outside the log
hides the session's most important recovery decision from the fold, replay,
and the trace viewer.

## Decision

Compensation is an external call under the standard two-phase rule:
`CompensationRequested` is an intent event carrying the tool, its arguments,
and a `CompensatesKey` link to the call being reversed; its outcomes are the
standard outcome events under the compensation's own key. When a session
unwinds, previously succeeded compensatable calls are compensated in reverse
order of their success. An intent whose `CompensatesKey` references no
recorded intent is an integrity violation.

## Consequences

Crash recovery mid-compensation, retry attempts, idempotency keys, and replay
all inherit from ADRs 0008/0010/0012 with no new machinery. The log fully
narrates the unwind, so the trace viewer can show "refund reversed because
notify failed" without side channels. Event volume grows by two per
compensated call, which is accepted.
