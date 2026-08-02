# Experiment 005 — The cross-framework reliability table (N=100)

> **⚠️ WITHDRAWN (2026-08-02).** These results are withdrawn and must not be
> cited: the Temporal adapter's activity StartToClose timeout was changed
> mid-measurement (120s → 30s → 10s), and the run configuration was not
> pre-registered. Finding 5 is invalidated outright (see below). The raw data
> stays for the audit trail. A rerun happens only under a pre-registered,
> owner-approved configuration (methodology header).

- **Date:** 2026-08-02
- **Scope:** the benchmark's first cross-framework round: **seven
  contestants** × 3 fault conditions × N=100, all through the harness-owned
  HTTP tool boundary (physical fault injection). Raw per-run data:
  [`data/005-langgraph-n100.json`](data/005-langgraph-n100.json),
  [`data/005-temporal-n100.json`](data/005-temporal-n100.json).
- **Headline:** durable-execution frameworks *recover*; whether recovery is
  **exactly-once** comes down to idempotency-key discipline — structural in
  Aurelia, optional-and-usually-absent everywhere else. The same framework
  scores 100/100 or 0/100 on correctness depending on one line of key code.

## Contestants

| Name | What it is |
|---|---|
| aurelia | this runtime's case-resolution agent (keys derived from the log) |
| plain-rerun / plain-noretry | the floor: in-memory loop, resubmit-or-nothing recovery |
| langgraph / langgraph-uuidkeys | StateGraph + Sqlite checkpointer, thread-resume recovery; deterministic vs per-attempt keys |
| temporal / temporal-uuidkeys | workflow + activities + documented saga compensation, history-resume recovery; deterministic vs per-attempt keys |

LangGraph and Temporal implementations follow their documented durable
patterns (checkpointer + thread id; history + workflow id + saga), with
retry parity (2 attempts) across all contestants. **Both implementations are
ours and owe their maintainers a review before anything public** (methodology
§4).

## Results (N=100 per cell, seed 7, HTTP tool boundary)

| Framework | F0 (none) | F1 (30% tool faults) | F6 (kill after side effect) |
|---|---|---|---|
| **aurelia** | 100 · 100 | 75 · **100** | **100 · 100** |
| plain-rerun | 100 · 100 | 57 · 79 (I1×6, I2×17) | 0 · 0 (**I1×100**) |
| plain-noretry | 100 · 100 | 35 · 85 (I2×15) | 0 · 0 (I2×100, I3×100) |
| langgraph | 100 · 100 | 70 · 88 (I2×12) | **100 · 100** |
| langgraph-uuidkeys | 100 · 100 | 70 · 88 (I2×12) | 0 · 0 (**I1×100**) |
| temporal | 100 · 100 | 89 · **100** | **100 · 100** |
| temporal-uuidkeys | 100 · 100 | 83 · 93 (**I1×7**) | 0 · 0 (**I1×100**) |

*Cells are RR · CAF out of 100. I1 = double side effect; I2 = silent leak;
I3 = no queryable terminal state.*

## Findings

1. **F0 says nothing.** All seven contestants are indistinguishable without
   faults — which is the condition every popular agent benchmark measures.
2. **The key-discipline split is universal.** LangGraph and Temporal both
   went 100/100 exactly-once with deterministic keys and 0/100 —
   double-charging every single run — with per-attempt keys, on identical
   recovery machinery. Checkpointing/history buys *recovery*; **keys buy
   correctness**, and every framework leaves them to the application author.
   Aurelia's 100/100 is not superior engineering effort at the call site —
   the key is derived from the log (`session/turn/step`, ADR-0010), so the
   fresh-key mistake is unwritable.
3. **Compensation separates the disciplined.** Temporal's documented saga
   pattern gave it the best F1 row (89 recovery, zero violations — every
   terminal failure reversed its credit). LangGraph, with no compensation
   concept, leaked credits silently in 12 F1 runs (I2). Aurelia's saga
   machinery matched Temporal's cleanliness (75 · 100). The floor baselines
   corrupted or leaked.
4. **Naive retry is worse than nothing** under partial failure: plain-rerun's
   resubmits minted 6 duplicate charges (I1) that plain-noretry never
   created.
5. ~~**A timeout is not a failure — and naive keys pay for the difference.**~~
   **INVALIDATED (2026-08-02): do not cite.** This finding was an artifact of
   the measurement, not a property of Temporal: the 10s StartToClose timeout
   that produced the attempt-1-timeout-after-commit condition was set
   mid-measurement (120s → 30s → 10s, changed while correcting an unrelated
   F6 scoring artifact), so the configuration that *created* the finding was
   chosen by the party reporting it, after measurement had begun. The
   forensic trail (workflow `bench-bench-jX8nYS`, event 12) is real, but a
   finding manufactured by an un-pre-registered config change is not a
   finding. If a pre-registered rerun reproduces the effect under a timeout
   value fixed in advance, it can be re-stated then.
6. F1 recovery-rate differences (89 vs 75 vs 70) trace to retry topology
   (where the 2-attempt budget sits relative to the failure), not to depth of
   durability — a tuning observation, not a verdict.

## Threats to validity

- **The referee employs a contestant, and wrote the competitors' code.**
  Both framework implementations follow public documentation and are
  committed for inspection, but maintainer review has not happened; the
  Temporal F6 configuration was already corrected once during development
  (activity StartToClose gating orphaned-activity re-dispatch — initially
  mis-scored as a Temporal failure; it was a harness/config artifact). Treat
  every non-Aurelia number as provisional until reviewed.
- One task, one fault per condition, one machine. Latency/cost are not
  compared here (Temporal carries a server; the others don't — apples/oranges).
- The uuid-keys variants are deliberately naive — but they mirror the
  dominant pattern in published examples, which is the point.

## Conclusion

The benchmark now does what it was built to do: distinguish frameworks that
*look identical* on happy paths by what they guarantee when processes die
mid-side-effect. Next per methodology §8: neutral-repo extraction and
maintainer review outreach.
