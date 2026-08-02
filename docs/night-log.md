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
