# Experiment 004 — Reliability benchmark at N=100

- **Date:** 2026-08-02 (autonomous overnight run)
- **Scope:** the [experiment 003](003-benchmark-pilot.md) matrix at the
  methodology's required scale: 3 frameworks × 3 fault conditions × **N=100**
  (900 runs, seeded, local backends only — the benchmark task makes no model
  calls). Raw per-run data: [`data/004-n100-runs.json`](data/004-n100-runs.json).

## Results (N=100 per cell, seed 7)

| Framework | Fault | Recovery Rate | Correctness After Failure | Violations |
|---|---|---|---|---|
| aurelia | F0 | 100/100 | 100/100 | — |
| plain-rerun | F0 | 100/100 | 100/100 | — |
| plain-noretry | F0 | 100/100 | 100/100 | — |
| aurelia | F1 | 75/100 | **100/100** | — |
| plain-rerun | F1 | 35/100 | 85/100 | I1×15 (double credit), I2×15 |
| plain-noretry | F1 | 35/100 | 85/100 | I2×15 (silent leak) |
| aurelia | F6 | **100/100** | **100/100** | — |
| plain-rerun | F6 | 0/100 | **0/100** | **I1×100 — double credit, every single run** |
| plain-noretry | F6 | 0/100 | 0/100 | I2×100, I3×100 |

## What N=100 sharpened

1. **The runtime behaves exactly as its retry math predicts.** Under F1
   (30% per-call fault rate, 2 attempts, 3 calls), the closed-form prediction
   is `(1 − 0.3²)³ = 75.4%`; measured: **75/100**. The floor's no-retry
   prediction is `(1 − 0.3)³ = 34.3%`; measured: **35/100** — both within a
   run of theory. A reviewer can verify the engine from the scoreboard.
2. **plain-rerun's resubmit makes F1 *worse*, not better.** Its RR equals
   no-retry's (the resubmit mostly re-fails), but resubmitting after a partial
   success — credit landed, notify failed — minted **15 duplicate credits**
   that plain-noretry never created. Naive retry converts availability
   failures into correctness failures.
3. **F6 is deterministic, not probabilistic.** Kill-after-effect produced
   100/100 exactly-once completions for Aurelia and 100/100 double-charges
   for the naive resubmit. No amount of N changes a structural property.
4. **Aurelia's CAF is 300/300 across all conditions** — every one of its 25
   F1 non-recoveries was a clean, explicit, compensated terminal state.

## Threats to validity

Unchanged from experiment 003 except scale: the referee still employs a
contestant (neutral harness extraction pending), the floor is naive by
design, and F1 partially measures "has retries at all." One machine, one
night. Runtime for the full matrix: ~30 minutes on an M1 laptop.

## Conclusion

At the methodology's required N, the two axes hold: recovery is engineering
(tunable, predictable — ADR-0027 makes it a declared policy), while
correctness-after-failure is architecture (structural, binary). The floor
baselines fail the second axis in exactly the ways the invariants were
designed to catch. Next: extract the framework-neutral harness so these
numbers can be produced for LangGraph, Temporal, and Agent Framework under
rules their maintainers have reviewed.
