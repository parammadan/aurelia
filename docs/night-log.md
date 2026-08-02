# Night log — 2026-08-02 (autonomous overnight session)

Charter: branches + PRs only, no self-merges, no Azure spend, no cloud models,
nothing public. Param reviews everything in the morning.

## Wake 1 (~03:55 ET)
- Started N=100 benchmark run in the background (900 runs, local backends
  only — the benchmark harness makes zero model calls). Raw data will land in
  docs/experiments/data/004-n100-runs.json.
- Next planned wakes: collect + write experiment 004 → neutral harness
  extraction → playground tests → status-derivation dedup.

## Wake 2 (~04:30 ET, woken by benchmark completion)
- N=100 matrix finished (900 runs, ~30 min): aurelia F1 75/100 RR (theory:
  75.4%) with CAF 300/300 overall; plain-rerun F6 double-credited 100/100.
  New finding: naive resubmit made F1 CAF *worse* than no-retry (15 duplicate
  credits from partial-success reruns).
- Wrote docs/experiments/004-benchmark-n100.md + committed raw data.
- Opening PR (unmerged, for morning review). Next wake: playground tests +
  status-derivation dedup, then neutral harness extraction.

## Wake 3 (~04:45 ET)
- Deduplicated session-status derivation: SessionStates.Derive is now the
  single source; playground snapshot, trace viewer, and MCP all consume it
  (defense-pack weakness #2 closed).
- Added TraceBuilder tests: call pairing/durations, child links, pending
  approvals, retry counts, saga compensation (weakness #8 closed).
- GOTCHA worth remembering: referencing the web-SDK Playground project from
  Aurelia.CrashTests silently pruned the test output's copy of
  Aurelia.Runtime's dependency closure, killing the child-process crash
  harness with FileNotFoundException. Fix: playground tests live in their own
  test project (tests/Aurelia.PlaygroundTests); the crash-test project stays
  reference-minimal. Suite: 59 tests green.
- Next wake: neutral benchmark harness extraction.

## Wake 4 (~05:10 ET)
- Neutral harness extraction: benchmark restructured into framework-agnostic
  core (Contracts/IFrameworkAdapter, HarnessRunner, Scoring — reads only the
  harness-owned ledger + adapter claims) with AureliaAdapter and two
  PlainAdapter flavors. docs/benchmark/harness.md documents the adapter
  contract AND the honest gap: tools must move behind a harness-owned HTTP
  boundary before third-party framework rounds.
- Sanity n=5 through the new core matches prior behavior; kicked off N=100
  confirmation run in the background.
- Also this wake: Param chose bypass-permissions mode for the rest of the
  night (his explicit selection), so remaining wakes run unattended.
