# Paper 1 — pre-registration, round 1

> **STATUS: APPROVED 2026-10-12** by Param (explicit "Approve as drafted"),
> predictions as drafted by Claude, unedited. Frozen code: commit `389835f`.

## Frozen configuration

- Code: `research/replan/agent.py`, `research/replan/run_study.py`, tool server
  `src/Aurelia.ToolServer` (Release build), at commit `389835f`.
- Config: `research/replan/config.json` (factors below), copied verbatim into
  the results file header.
- Task: service credit for case 42 (entitled $25), goal text fixed in
  `run_study.py`.
- Fault: **after-effect kill** — the server has written the credit to the
  ledger and holds the response; the agent is SIGKILLed; the hold is released;
  recovery = the same command re-run once.
- Models (local, Ollama, OpenAI-compatible API): `qwen2.5:3b`, `llama3.2:3b`.
- Model seed per request = `seed*1000 + invocation*100 + call#`, so a recovery
  is a *new* request, as in production.

| Factor | Levels |
|---|---|
| granularity | turn, step |
| keys | tool_call_id, args_hash, position |
| temperature | 0.0, 0.7, 1.0 |
| model | qwen2.5:3b, llama3.2:3b |

36 cells × **N = 30** = 1,080 runs, seeds 1000–1029 in every cell.

## Outcomes (defined before data)

- **Primary — duplicate rate:** fraction of kill-triggered runs whose ledger
  holds > 1 credit for the case. Ledger is ground truth; keys are ignored.
- **Secondary:** completion (recovery exited 0 and notified the customer);
  correct (completed ∧ exactly one credit); re-ask rate (recovery made a model
  call that chose `issue_service_credit`).
- **Exclusion:** runs where the kill never triggered (the model did not reach
  the credit call within 180 s) are excluded from the duplicate-rate
  denominator and **reported per cell**, never dropped silently.
- **Intervals:** Wilson 95% CI per cell. No significance tests beyond the
  pre-stated hypotheses.

## Hypotheses and predictions

| # | Prediction | Falsified by |
|---|---|---|
| H1 | **step**: duplicate rate = 0 in all 18 cells | any single duplicate |
| H2 | **turn + tool_call_id**: duplicate rate ≥ 90% at every temperature (provider ids are fresh per request) | any cell < 90% |
| H3 | **turn + args_hash**: duplicate rate ≤ 10% at every temperature — *because in this task every argument is copied from a tool result*, so re-decisions are identical. Hash keys are predicted to fail on tasks with model-authored arguments (round 2) | any cell > 10% |
| H4 | **turn + position**: duplicate rate ≤ 10% at every temperature | any cell > 10% |
| H5 | re-ask rate: turn ≥ 90%, step = 0% | either bound missed |
| H6 | completion: qwen2.5:3b ≥ 80%, llama3.2:3b ≥ 60% (descriptive only; not a thesis claim) | — |

## Rules

- Smoke runs (N ≤ 2, one cell) are allowed for debugging, written outside
  `research/replan/data/`, and never reported. One was run on 2026-10-12 to
  verify the pipeline.
- No code or config change after approval. A change means a new round
  (R2) with its own pre-registration; R1 data stays published and labelled.
- All cells run to N; no early stopping, no re-runs of "bad" runs.
- Raw per-run JSONL is committed alongside the analysis.

## Round 2 (planned, separate pre-registration)

Task families with **model-authored arguments** (customer email body, refund
reason memo, booking notes) where `args_hash` is expected to fail; kill
points before the effect and between model response and tool call; one
hosted frontier model if spend is approved.
