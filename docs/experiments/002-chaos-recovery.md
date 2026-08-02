# Experiment 002 — Recovery rate under injected tool chaos

- **Date:** 2026-08-01
- **Question:** when tools fail randomly, how often does the runtime still
  complete the task — and does its recovery behave the way its retry policy
  predicts?
- **Verdict:** recovery tracks the analytical prediction of the retry policy,
  and every non-recovered run ended in a *clean* terminal state (parked via
  the saga path), never a corrupted one.

## Setup

- **Runtime:** Aurelia @ `main`, chaos API (`--chaos` on the eval command),
  seeded RNG so the fault sequence is reproducible from the spec.
- **Scenario:** `case-42-happy` — three tool calls end-to-end (lookup, ledger
  credit, notify), ledger and replay assertions. Retry policy: 2 attempts per
  call (the provisional default; no ADR yet).
- **Faults:** `tool_fail=r` — each tool execution independently throws with
  probability *r*. N = 20 per rate, `seed=7`.

```bash
dotnet run --project src/Aurelia.Runtime -- eval --suite evals/phase1.json \
  --filter case-42-happy --repeat 20 --chaos "seed=7,tool_fail=<r>"
```

## Results

A session completes iff all 3 calls succeed within 2 attempts each, so the
predicted completion rate is `(1 − r²)³`.

| tool_fail | Predicted | Measured (N=20) |
|---|---|---|
| 0.00 | 100% | 20/20 (100%) |
| 0.15 | 93% | 17/20 (85%) |
| 0.30 | 75% | 14/20 (70%) |
| 0.50 | 42% | 7/20 (35%) |

Measured rates sit within sampling noise of the prediction at every fault
level (binomial N=20 has ±10 pt standard error in this range).

## What "failed" means here — the part that matters

A non-recovered run is not a corrupted run. Under total chaos
(`tool_fail=1.0`, covered by unit test), the session takes the normal
terminal path: `CallFailed` → saga unwind → `SessionFailed` with explicit
compensated/uncompensated key lists, replaying byte-identically afterward.
No run in any condition produced a double side effect or an unreadable log.
This is the distinction the Orchestration Reliability Benchmark is built to
score: **recovery rate** (did the task complete?) and **correctness after
failure** (did anything leak?) are different axes, and the second one is
where agent frameworks quietly differ.

## Threats to validity

1. N = 20 per condition; the prediction-tracking claim is directional, not a
   fit — larger N belongs to the benchmark proper.
2. Faults are independent per execution; real outages are correlated (a down
   dependency fails every retry). Correlated-fault scenarios are benchmark
   work.
3. Correctness-after-failure was verified by the scenario's ledger/replay
   assertions and unit tests at rate 1.0, not by per-run forensic audit of
   all 60 chaos runs.
4. The retry count (2) is a provisional default; recovery rate is a direct
   function of it, which is exactly why retry policy deserves its own ADR.

## Conclusion

The runtime degrades predictably — recovery follows the retry-policy math —
and fails cleanly when it fails. The chaos API (`ChaosPolicy`/`ChaosSource`,
seeded, spec-reproducible) is now a permanent part of the harness and the
foundation of the cross-framework benchmark.
