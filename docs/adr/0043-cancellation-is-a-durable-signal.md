# 0043 — Cancellation is a durable signal honoured at safe points

- **Status:** Accepted
- **Date:** 2026-08-04
- **Author:** Param Madan

## Context

"Stop this task" is a real operation with real consequences: the work may
have already moved money. Implemented as an in-memory `CancellationToken` it
would be lost with the process, and a cancelled-but-crashed session would
resume as if nothing had been asked. Implemented as a kill it would leave the
worst state of all — effects committed, nothing unwound, nobody told.

Second change under the owner's scoped freeze lift.

## Decision

**Cancellation is an event, not a token.** `CancellationRequested`
(reason, requestedBy) is appended to the session's log by any process, the
same way an approval is resolved (ADR-0011). It survives crashes, it replays,
and it is visible in the trace.

**It is honoured at safe points, and a safe point is between steps.** A
two-phase call already in flight is *not* abandoned: its outcome is recorded
first, because an intent with no outcome is the crash signature and forging
one would corrupt the very thing recovery depends on. Concretely:

| Cancellation lands… | What happens |
|---|---|
| before the step's intent | the step never starts; nothing to undo |
| after the intent, before the effect | the call completes and records its outcome, then the session stops at the boundary and unwinds |
| after the effect | identical — the outcome is recorded, then the effect is compensated |

So cancellation never races a side effect. It waits for the boundary and
then uses the **existing saga path** (ADR-0015): compensatable effects are
reversed, failures are reported, and effects that can never be undone are
named (ADR-0042).

**A cancelled session is terminal and distinguishable.** The terminal event
is `SessionFailed` — the goal was not achieved — but the log also holds the
`CancellationRequested`, so `SessionStates.Derive` reports status
**`cancelled`** rather than `failed`. Both the live driver and the replay
engine build their terminal JSON through the same helper, so replay-equals-live
still holds exactly.

**Requesting twice is idempotent**; the first request wins and later ones are
no-ops. Cancelling an already-terminal session is a no-op too.

## Consequences

An operator can stop a durable task from any process and get a truthful
account of what was undone, what was not, and what never could be. The costs:
cancellation is not instantaneous by design (it waits for the current call to
finish recording), and "cancelled" joins the derived-status vocabulary, which
every surface reading status must now recognise.

## Verified by

`tests/Aurelia.CrashTests/CancellationTests.cs` — one test per window
(before intent / after intent / after effect), asserting the outcome and the
ledger in each, plus idempotent re-request and replay equality.
