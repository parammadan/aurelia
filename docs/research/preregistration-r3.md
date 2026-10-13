# Paper 1 — pre-registration, round 3 (crash-free control)

> **STATUS: APPROVED 2026-10-13** by Param (explicit "Approve R3 + GPU job"),
> predictions as drafted by Claude, unedited. Frozen code: commit `bf1987a` on `research/r2`.
> Written 2026-10-13 after R2's results
> were seen. That is the reason R3 exists, and it is stated here plainly.

## Why R3 exists

R2's H1 ("step: 0 duplicates and 0 lost effects") was **falsified**: 23
duplicates and 21 lost effects under step recording. An exploratory look at
all 44 runs found that in every one, recovery re-executed the recorded call
under its recorded key, and the extra or missing credit came from a *later,
new* model decision. H1 as written mixed recovery errors up with model errors.
That was a flaw in the prediction, and **H1 stays reported as falsified**.

The real question needs a baseline: **how often do these models create
duplicates or lost effects when nothing crashes?** Only then can recovered
runs be compared with crash-free ones.

## Design

Identical to R2 (same frozen agent, tool server, models and digests, Ollama
0.32.5 with `OLLAMA_CONTEXT_LENGTH=4096`, one A100, 16 workers, seeds
1000–1029, N = 30), except:

- **No fault:** no kill window; the agent runs once to completion
  (`"fault": "none"` in `config_r3.json`).
- Granularity is irrelevant without a crash (fixed at `step`). Keys still
  matter, because they decide whether a *model-made* repeat call is deduped.
- 3 tasks × 2 models × 3 keys × 3 temperatures × 30 = **1,620 runs**.
- Denominator: runs where the model called `issue_service_credit` at least
  once (`reached_credit`), the crash-free analogue of "kill triggered".

## Comparisons (pooled over models and temperatures, per task × keys)

Difference in rates between R2-recovered and R3 crash-free, with a Newcombe
(Wilson-score) 95% interval for the difference of two proportions.

## Predictions

| # | Prediction |
|---|---|
| R3-H1 | **step-recovered vs crash-free:** the duplicate-rate and lost-effect-rate differences have 95% intervals that include 0, for every task × keys |
| R3-H2 | **turn + tool_call_id recovered vs crash-free:** duplicate rate is higher by ≥ 30 percentage points in the `credit` and `memo` tasks |
| R3-H3 | crash-free duplicate rate under `args_hash` ≤ crash-free rate under `tool_call_id`, for every task (identical model repeats are deduplicated) |

## Rules

As R1/R2: frozen code and config at approval; all cells run to N; no early
stopping; raw rows published; a change means a new round.
