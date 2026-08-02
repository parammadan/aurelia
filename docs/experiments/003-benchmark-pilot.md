# Experiment 003 — Reliability benchmark pilot (Aurelia vs. the floor)

- **Date:** 2026-08-01
- **Scope:** pilot-scale run of the [benchmark methodology](../benchmark/methodology.md):
  3 frameworks × 3 fault conditions × N=10, seeded, raw per-run data in
  [`data/003-pilot-runs.json`](data/003-pilot-runs.json).
- **Headline:** all frameworks are indistinguishable with no faults injected —
  and under a SIGKILL landing after the side effect, the naive
  retry-the-task agent **double-credited the customer in 10 of 10 runs**,
  while Aurelia completed 10 of 10 with exactly one credit.

## Contestants

- **aurelia** — the case-resolution agent on the durable runtime (event-sourced
  sessions, keyed retries, saga compensation).
- **plain-rerun** — the floor baseline: an in-memory loop over the *same
  tools*, fresh random idempotency keys per attempt (what ad-hoc agents do),
  whose recovery story is "resubmit the task."
- **plain-noretry** — the same loop with no recovery story at all.

## Conditions

- **F0** — no faults.
- **F1** — each tool execution throws with p=0.3 (seeded).
- **F6** — SIGKILL after the credit hits the ledger but before the call
  returns: the effect happened; the process never learned it. Recovery:
  aurelia re-drives the same session; plain-rerun resubmits the task;
  plain-noretry does nothing.

## Results (N=10 per cell, seed 7)

| Framework | Fault | Recovery Rate | Correctness After Failure | Violations |
|---|---|---|---|---|
| aurelia | F0 | 10/10 | 10/10 | — |
| plain-rerun | F0 | 10/10 | 10/10 | — |
| plain-noretry | F0 | 10/10 | 10/10 | — |
| aurelia | F1 | 8/10 | **10/10** | — |
| plain-rerun | F1 | 3/10 | 8/10 | I1×2 (double credit), I2×2 |
| plain-noretry | F1 | 3/10 | 8/10 | I2×2 (silent leak) |
| aurelia | F6 | **10/10** | **10/10** | — |
| plain-rerun | F6 | 0/10 | **0/10** | **I1×10 — double credit, every run** |
| plain-noretry | F6 | 0/10 | 0/10 | I2×10 (silent leak), I3×10 (no terminal state) |

Reading the F6 row precisely: plain-rerun *finished the task* in all 10 runs —
the customer got notified — and every single one of those "successes" charged
the ledger twice, because the resubmit used fresh idempotency keys. This is
the failure mode that looks like success from the outside, and it is why
Recovery Rate and Correctness After Failure are separate axes: a completed
task with a corrupted ledger scores 0 on both here (RR requires *correct*
completion).

Aurelia's F6 mechanics, for contrast: the intent event was durable before the
call; the effect landed; recovery re-executed under the **same recorded key**;
the ledger deduplicated; exactly one credit, every run. Its two F1 misses were
clean terminal failures (retries exhausted → saga unwind → explicit
`SessionFailed`) — zero invariant violations.

## Threats to validity

1. **Pilot scale.** N=10 per cell; the methodology requires N≥100. The F6
   effects are 10/10 vs 0/10 — scale will not rescue the floor — but F1's
   rates carry wide intervals.
2. **The referee employs a contestant.** This harness lives in Aurelia's repo
   and was written by the same author. The methodology's answer — a neutral
   harness repo and maintainer review — hasn't happened yet. Treat these
   numbers as the pilot they are.
3. **The floor baselines are naive on purpose.** A careful engineer can add
   idempotency keys to a plain loop by hand; the benchmark's real question —
   pending the framework rounds — is what the popular frameworks give you *by
   default and by documentation*.
4. F1 comparison partially measures "has retries" (Aurelia does, the floor
   doesn't) — which is a real product difference, but a shallower one than
   the F6 result.

## Conclusion

The benchmark's two axes separate cleanly in practice, on real processes and
real kills, at pilot scale. The instrument works. Next per the methodology
roadmap: neutral harness extraction, N≥100, then the framework rounds
(LangGraph, Temporal, Agent Framework/SK) with maintainer review.
