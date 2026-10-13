# Paper 1 — pre-registration, round 2

> **STATUS: APPROVED 2026-10-13** by Param (explicit "Approve all three":
> this pre-registration as drafted, predictions by Claude unedited; one GPU
> smoke job; the R2 GPU job). Frozen code: commit `05d7ba4` on `research/r2`.

R1 tests whether re-asking duplicates effects when every tool argument is
copied from a tool result. R2 tests the two cases R1 cannot reach:

## Task families

| Task | Request | What changes | Which key strategy it stresses |
|---|---|---|---|
| **T-memo** | case 42, as R1 | `issue_service_credit` gains a required `reason` string the model writes ("customer-facing memo") | `args_hash` — a rewritten memo is a new hash |
| **T-two** | "cases 42 and 7" | two credits in one run; the model picks the order | `position` — if a re-ask flips the order, the per-tool ordinal for case 7 collides with case 42's recorded key and the ledger **drops a legitimate credit** while the tool returns a success-shaped result |

| **credit** | R1's task, unchanged | nothing — a **hardware replication** of R1 | — (checks R1's conclusions survive the move to the cluster) |

Server, kill window, models (same Ollama digests `357c53fb659c`,
`a80c4f17acd5`), temperatures, seeds, N = 30: identical to R1.
Granularity × keys as R1. 3 tasks × 36 cells × 30 = **3,240 runs**.

**Hardware (differs from R1, fixed here):** NU Explorer, one NVIDIA A100,
Ollama 0.32.5 with `OLLAMA_NUM_PARALLEL=16`, 16 concurrent runs
(`--workers 16`), each with its own tool server and directories. Concurrent
batching can change model outputs relative to serial decoding; that is why
the `credit` task is re-run here and reported beside R1, never pooled.
Job script: `research/replan/slurm/r2_gpu.sbatch`; config `config_r2.json`.

## New outcome (T-two)

- **Lost effect:** the run claims success but the ledger lacks a credit for a
  case the request named. Scored per case from the ledger.
- Duplicate is redefined per case: > 1 credit **for the same case**.

## Predictions

| # | Prediction |
|---|---|
| R2-H1 | step: 0 duplicates and 0 lost effects in every cell, both tasks |
| R2-H2 | T-memo, turn + args_hash: duplicate rate ≤ 10% at T = 0, ≥ 50% at T = 1.0 |
| R2-H3 | T-memo, turn + position: ≤ 10% at all temperatures |
| R2-H4 | T-two, turn + position: lost-effect rate > 0 at T ≥ 0.7 (any lost effect confirms the failure mode exists) |
| R2-H5 | T-two, turn + args_hash: ≤ 10% duplicates (args fully determined per case) |
| R2-H6 | credit (replication): every R1 hypothesis H1–H5 gets the same held/falsified verdict on the cluster |

## Why this matters for the paper

If R2-H2 and R2-H4 hold together, then **no key strategy is safe under
turn-level recovery**: each one fails on a task shape that is common in
practice (free text in arguments, several effects in one turn). Only
recording the model's decision before acting is safe in every case. That's
the paper's thesis.
