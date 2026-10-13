# Paper 1 — round 1 results

Source: `data/r1.jsonl` · 1080 runs · analysis per the approved pre-registration.

| model | granularity | keys | T | runs | excl. | duplicate rate (95% CI) | re-ask | completed | correct |
|---|---|---|---|---|---|---|---|---|---|
| qwen2.5:3b | turn | tool_call_id | 0.0 | 30 | 0 | 100% (89–100) | 100% | 100% | 0% |
| qwen2.5:3b | turn | tool_call_id | 0.7 | 30 | 1 | 100% (88–100) | 100% | 100% | 0% |
| qwen2.5:3b | turn | tool_call_id | 1.0 | 30 | 2 | 100% (88–100) | 100% | 100% | 0% |
| qwen2.5:3b | turn | args_hash | 0.0 | 30 | 0 | 0% (0–11) | 100% | 100% | 100% |
| qwen2.5:3b | turn | args_hash | 0.7 | 30 | 1 | 0% (0–12) | 100% | 100% | 100% |
| qwen2.5:3b | turn | args_hash | 1.0 | 30 | 2 | 0% (0–12) | 100% | 100% | 100% |
| qwen2.5:3b | turn | position | 0.0 | 30 | 0 | 0% (0–11) | 100% | 100% | 100% |
| qwen2.5:3b | turn | position | 0.7 | 30 | 1 | 0% (0–12) | 100% | 100% | 100% |
| qwen2.5:3b | turn | position | 1.0 | 30 | 3 | 0% (0–12) | 100% | 100% | 100% |
| qwen2.5:3b | step | tool_call_id | 0.0 | 30 | 0 | 0% (0–11) | 0% | 100% | 100% |
| qwen2.5:3b | step | tool_call_id | 0.7 | 30 | 1 | 0% (0–12) | 0% | 100% | 100% |
| qwen2.5:3b | step | tool_call_id | 1.0 | 30 | 2 | 0% (0–12) | 0% | 100% | 100% |
| qwen2.5:3b | step | args_hash | 0.0 | 30 | 0 | 0% (0–11) | 0% | 100% | 100% |
| qwen2.5:3b | step | args_hash | 0.7 | 30 | 1 | 0% (0–12) | 0% | 100% | 100% |
| qwen2.5:3b | step | args_hash | 1.0 | 30 | 2 | 0% (0–12) | 0% | 100% | 100% |
| qwen2.5:3b | step | position | 0.0 | 30 | 0 | 0% (0–11) | 0% | 100% | 100% |
| qwen2.5:3b | step | position | 0.7 | 30 | 1 | 0% (0–12) | 0% | 100% | 100% |
| qwen2.5:3b | step | position | 1.0 | 30 | 2 | 0% (0–12) | 0% | 100% | 100% |
| llama3.2:3b | turn | tool_call_id | 0.0 | 30 | 30 | — | — | — | — |
| llama3.2:3b | turn | tool_call_id | 0.7 | 30 | 30 | — | — | — | — |
| llama3.2:3b | turn | tool_call_id | 1.0 | 30 | 30 | — | — | — | — |
| llama3.2:3b | turn | args_hash | 0.0 | 30 | 30 | — | — | — | — |
| llama3.2:3b | turn | args_hash | 0.7 | 30 | 30 | — | — | — | — |
| llama3.2:3b | turn | args_hash | 1.0 | 30 | 30 | — | — | — | — |
| llama3.2:3b | turn | position | 0.0 | 30 | 30 | — | — | — | — |
| llama3.2:3b | turn | position | 0.7 | 30 | 30 | — | — | — | — |
| llama3.2:3b | turn | position | 1.0 | 30 | 30 | — | — | — | — |
| llama3.2:3b | step | tool_call_id | 0.0 | 30 | 30 | — | — | — | — |
| llama3.2:3b | step | tool_call_id | 0.7 | 30 | 30 | — | — | — | — |
| llama3.2:3b | step | tool_call_id | 1.0 | 30 | 30 | — | — | — | — |
| llama3.2:3b | step | args_hash | 0.0 | 30 | 30 | — | — | — | — |
| llama3.2:3b | step | args_hash | 0.7 | 30 | 30 | — | — | — | — |
| llama3.2:3b | step | args_hash | 1.0 | 30 | 30 | — | — | — | — |
| llama3.2:3b | step | position | 0.0 | 30 | 30 | — | — | — | — |
| llama3.2:3b | step | position | 0.7 | 30 | 30 | — | — | — | — |
| llama3.2:3b | step | position | 1.0 | 30 | 30 | — | — | — | — |

## Pre-registered hypotheses

| # | prediction | held? | observed |
|---|---|---|---|
| H1 | step: duplicate rate = 0 in all cells | ✅ held | 0 duplicates total |
| H2 | turn + tool_call_id: ≥ 90% every cell | ✅ held | 100%–100% cell range |
| H3 | turn + args_hash: ≤ 10% every cell | ✅ held | 0%–0% cell range |
| H4 | turn + position: ≤ 10% every cell | ✅ held | 0%–0% cell range |
| H5 | re-ask: turn ≥ 90%, step = 0% | ✅ held | turn min 100%, step max 0%  |
| H6 | completion qwen2.5:3b ≥ 80% (descriptive) | ✅ held | 100%  |
| H6 | completion llama3.2:3b ≥ 60% (descriptive) | ⏳ pending | no data  |

## Notes (written by hand after the automated analysis above; not part of it)

- **llama3.2:3b produced no usable data in R1.** In 540 / 540 runs it never
  made a structured `issue_service_credit` tool call, so the after-effect
  fault never fired and every run is an exclusion under the pre-registered
  rule. 492 runs emitted the call as plain text in the message content
  (e.g. `{"name": "issue_service_credit", "parameters": {...}}`) and 48 ended
  the turn without crediting. The H6-llama row reads "pending" because the
  script has no denominator; in fact it is **untestable**, not pending.
  **All R1 conclusions therefore rest on qwen2.5:3b only.**
- **qwen2.5:3b exclusions: 19 / 540.** 18 runs ended the turn without calling
  the credit tool. Several told the customer a credit ID that was never
  issued, which is model behaviour recorded verbatim in `victim_tail`. One run
  (seed 1013, turn/position, T = 1.0) has empty output and timed out at
  180 s: see deviation 1.
- **qwen2.5:3b, 521 kill-triggered runs:** duplicates appear only under
  turn + `tool_call_id` (87 / 87). Every other granularity × key cell has
  0 duplicates.

### Deviations from the pre-registered procedure

1. **Mac slept 22:08–07:30 EDT (2026-10-12/13)** while the study was running.
   The run in flight at sleep time (row 253, see above) timed out and is an
   exclusion. No other row is affected; the runner continued on wake.
2. **Mac powered off ~08:4x EDT 2026-10-13** at 653 rows. All 653 written
   rows parsed intact; the in-flight run was never written and re-ran from
   scratch with the same seed when the resumable runner restarted.
3. The prereg says the config is "copied verbatim into the results file
   header". It is instead in `research/replan/data/r1.meta.json` (written at
   launch), with the frozen commit.
