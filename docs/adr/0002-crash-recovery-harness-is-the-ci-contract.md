# 0002 — The crash-recovery harness is the CI contract

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Durability is the product. A durability claim that is demonstrated manually is
a party trick; one that is asserted automatically on every commit is a
contract. The temptation in every runtime project is to build features first
and bolt recovery on later — at which point recovery is retrofitted onto
designs that never considered it.

## Decision

From the first commit, CI runs an automated test that starts the runtime host
as a real OS process, kills it with SIGKILL while a tool call is provably in
flight, restarts it, and asserts that the session resumes from the event log
and completes with the correct final state. Any change that breaks this test
does not merge.

The test is currently satisfied by a provisional, hard-coded session driver
(`ProvisionalSessionDriver`) that is explicitly disposable. The trivial pass is
acceptable; the missing test is not. As the handwritten core lands, the same
harness gets harder to satisfy — kill points multiply (mid-model-call,
mid-compensation, mid-approval-wait) — but the contract's shape never changes.

## Consequences

Every feature is forced through the question "what happens if the process dies
here?" at design time, not after. CI is slower than a unit-test-only suite
(the harness runs real processes with real kills), which is accepted. The
harness also becomes the seed of the Phase 3 chaos-injection framework.

## Amendment (2026-08-02)

The harness kills in two windows, not one: mid-tool-call as originally
stated, and — via `AURELIA_SIGNAL_AFTER_EFFECT=1` — in the window *after*
the tool's side effect commits but *before* the outcome event is durable.
The second window is the harder contract (recovery must deduplicate, not
re-execute) and is the basis of the kill-after-effect fault condition. The
contract's shape is unchanged; its coverage is wider than the original text.

## Verified by

`tests/Aurelia.CrashTests/CrashRecoveryTests.cs`, run in `.github/workflows/ci.yml`.
