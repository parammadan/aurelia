# 0038 — The playground demos real processes; the trace viewer is a log projection

- **Status:** Accepted
- **Date:** 2026-08-02
- **Author:** Param Madan

> Backfill: playground and trace viewer shipped 2026-08-01 with no ADR
> (audit 2026-08-02 §1). This records the as-built decision for review.

## Context

The pitch is "kill it mid-refund and watch it finish the refund." A demo
that simulates the kill would be theater; the demo must run the same
machinery the tests run.

## Decision

`src/Aurelia.Playground` (localhost:5199) launches **real worker processes**
per session — the same Runtime binaries, located in the playground's own
output (the web-SDK deps.json lesson) — and its 💥 chaos button sends a real
SIGKILL, with a 2.5s auto-restart re-driving the session. Approve/deny for
parked sessions, a live event timeline, and the ledger are read from the
store; `AURELIA_STEP_DELAY_MS` slows runs to demo pace without changing
semantics.

The trace viewer (`/trace.html?session=id`, `GET /api/trace/{id}`) is a
**pure projection of the event log** (`TraceBuilder`): a Gantt timeline of
intent→outcome durations where a crash+recovery is *visible* as a long bar
spanning the kill, expandable to exact prompts, outputs, routing, attempts,
and idempotency keys, with parent↔child trace links. It reads any session —
playground-local or Cosmos — because it needs nothing but the log.

## Consequences

The demo *is* the contract, so demo breakage signals real breakage. Worker
lifecycle management stays deliberately crude (no supervisor); the
playground is a pitch artifact, not a deployment story. Status derivation is
shared with the runtime (`SessionStates`) to prevent drift.

## Verified by

`Aurelia.PlaygroundTests` (TraceBuilder projections; endpoint tests with real
worker E2E), manual chaos-button kill/recover demos, Cosmos historical trace.
