# Night log — 2026-08-02 (autonomous overnight session)

## ☀️ MORNING SUMMARY — read this first

**All four charter items completed. Nothing merged — PR #7 is your review queue.**

1. **Benchmark at N=100** (900 runs): aurelia RR tracks retry math within one
   run of theory (75/100 vs 75.4%), CAF a perfect 300/300; plain-rerun
   double-credited the customer in 100/100 kill runs; naive resubmit made F1
   correctness WORSE than no-retry (15 duplicate credits). → experiment 004.
2. **Neutral harness extracted**: IFrameworkAdapter contract + scoring that
   reads only harness-owned ground truth; the N=100 matrix re-run through the
   new core matched the original in every cell. The honest gap (tools must
   move behind an HTTP boundary before third-party rounds) is documented in
   docs/benchmark/harness.md.
3. **Status derivation deduplicated** (SessionStates.Derive — one source for
   playground, trace viewer, MCP).
4. **Playground/TraceBuilder tests added** (own test project — see the
   web-SDK-reference gotcha in Wake 3, worth remembering).

Suite: 59 tests green. Charter honored: no merges, no Azure spend, no cloud
models, nothing public. Your moves today: review + merge PR #7, then the
still-yours three (disclosure, blog voice, defense.md reading).

---


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

## Wake 5 (~05:50 ET) — final
- Confirmation N=100 through the neutral harness: identical to the original
  in every cell. Provenance noted in experiment 004.
- Morning summary written; loop stopping — charter complete.
