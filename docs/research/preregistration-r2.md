# Paper 1 — pre-registration, round 2

> **STATUS: DRAFT — NOT APPROVED.** Code for R2 is written only after R1
> finishes (R1 runs `agent.py` from disk; editing it mid-run would break R1's
> frozen configuration). Measured R2 runs need Param's explicit approval.

R1 tests whether re-asking duplicates effects when every tool argument is
copied from a tool result. R2 tests the two cases R1 cannot reach:

## Task families

| Task | Request | What changes | Which key strategy it stresses |
|---|---|---|---|
| **T-memo** | case 42, as R1 | `issue_service_credit` gains a required `reason` string the model writes ("customer-facing memo") | `args_hash` — a rewritten memo is a new hash |
| **T-two** | "cases 42 and 7" | two credits in one run; the model picks the order | `position` — if a re-ask flips the order, the per-tool ordinal for case 7 collides with case 42's recorded key and the ledger **drops a legitimate credit** while the tool returns a success-shaped result |

Server, kill window, models, temperatures, seeds, N = 30: identical to R1.
Granularity × keys as R1. 2 tasks × 36 cells × 30 = 2,160 runs.

## New outcome (T-two)

- **Lost effect:** the run claims success but the ledger lacks a credit for a
  case the request named. Scored per case from the ledger.
- Duplicate is redefined per case: > 1 credit **for the same case**.

## Predictions (Claude's draft — edit before approving)

| # | Prediction |
|---|---|
| R2-H1 | step: 0 duplicates and 0 lost effects in every cell, both tasks |
| R2-H2 | T-memo, turn + args_hash: duplicate rate ≤ 10% at T = 0, ≥ 50% at T = 1.0 |
| R2-H3 | T-memo, turn + position: ≤ 10% at all temperatures |
| R2-H4 | T-two, turn + position: lost-effect rate > 0 at T ≥ 0.7 (any lost effect confirms the failure mode exists) |
| R2-H5 | T-two, turn + args_hash: ≤ 10% duplicates (args fully determined per case) |

## Why this matters for the paper

If R2-H2 and R2-H4 hold together, then **no key strategy is safe under
turn-level recovery**: each one fails on a task shape that is common in
practice (free text in arguments, several effects in one turn). Only
recording the model's decision before acting is safe in every case. That's
the paper's thesis.
