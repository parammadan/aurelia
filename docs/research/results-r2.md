# Paper 1 — round 2 results

Source: `data/r2.jsonl` · 3240 runs · analysis per the approved R2 pre-registration.

| task | model | granularity | keys | T | runs | excl. | duplicate (95% CI) | lost effect (95% CI) | re-ask | completed | correct | wrong amount |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| credit | qwen2.5:3b | turn | tool_call_id | 0.0 | 30 | 0 | 100% (89–100) | 0% (0–11) | 100% | 100% | 0% | 0% |
| credit | qwen2.5:3b | turn | tool_call_id | 0.7 | 30 | 0 | 100% (89–100) | 0% (0–11) | 100% | 100% | 0% | 0% |
| credit | qwen2.5:3b | turn | tool_call_id | 1.0 | 30 | 1 | 100% (88–100) | 0% (0–12) | 100% | 100% | 0% | 0% |
| credit | qwen2.5:3b | turn | args_hash | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | 100% | 100% | 100% | 0% |
| credit | qwen2.5:3b | turn | args_hash | 0.7 | 30 | 0 | 0% (0–11) | 0% (0–11) | 100% | 100% | 100% | 0% |
| credit | qwen2.5:3b | turn | args_hash | 1.0 | 30 | 1 | 0% (0–12) | 0% (0–12) | 100% | 100% | 100% | 0% |
| credit | qwen2.5:3b | turn | position | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | 100% | 100% | 100% | 0% |
| credit | qwen2.5:3b | turn | position | 0.7 | 30 | 1 | 0% (0–12) | 0% (0–12) | 100% | 100% | 100% | 0% |
| credit | qwen2.5:3b | turn | position | 1.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | 100% | 97% | 97% | 0% |
| credit | qwen2.5:3b | step | tool_call_id | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| credit | qwen2.5:3b | step | tool_call_id | 0.7 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| credit | qwen2.5:3b | step | tool_call_id | 1.0 | 30 | 1 | 0% (0–12) | 0% (0–12) | 0% | 100% | 100% | 0% |
| credit | qwen2.5:3b | step | args_hash | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| credit | qwen2.5:3b | step | args_hash | 0.7 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| credit | qwen2.5:3b | step | args_hash | 1.0 | 30 | 2 | 0% (0–12) | 0% (0–12) | 0% | 100% | 100% | 0% |
| credit | qwen2.5:3b | step | position | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| credit | qwen2.5:3b | step | position | 0.7 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| credit | qwen2.5:3b | step | position | 1.0 | 30 | 1 | 0% (0–12) | 0% (0–12) | 0% | 100% | 100% | 0% |
| credit | llama3.2:3b | turn | tool_call_id | 0.0 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | turn | tool_call_id | 0.7 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | turn | tool_call_id | 1.0 | 30 | 27 | 0% (0–56) | 0% (0–56) | 0% | 0% | 0% | 33% |
| credit | llama3.2:3b | turn | args_hash | 0.0 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | turn | args_hash | 0.7 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | turn | args_hash | 1.0 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | turn | position | 0.0 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | turn | position | 0.7 | 30 | 29 | 0% (0–79) | 0% (0–79) | 0% | 0% | 0% | 100% |
| credit | llama3.2:3b | turn | position | 1.0 | 30 | 27 | 0% (0–56) | 0% (0–56) | 0% | 0% | 0% | 0% |
| credit | llama3.2:3b | step | tool_call_id | 0.0 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | step | tool_call_id | 0.7 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | step | tool_call_id | 1.0 | 30 | 29 | 0% (0–79) | 0% (0–79) | 0% | 0% | 0% | 0% |
| credit | llama3.2:3b | step | args_hash | 0.0 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | step | args_hash | 0.7 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | step | args_hash | 1.0 | 30 | 29 | 0% (0–79) | 0% (0–79) | 0% | 100% | 100% | 0% |
| credit | llama3.2:3b | step | position | 0.0 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | step | position | 0.7 | 30 | 30 | — | — | — | — | — | — |
| credit | llama3.2:3b | step | position | 1.0 | 30 | 30 | — | — | — | — | — | — |
| memo | qwen2.5:3b | turn | tool_call_id | 0.0 | 30 | 0 | 100% (89–100) | 0% (0–11) | 100% | 100% | 0% | 0% |
| memo | qwen2.5:3b | turn | tool_call_id | 0.7 | 30 | 0 | 100% (89–100) | 0% (0–11) | 100% | 100% | 0% | 0% |
| memo | qwen2.5:3b | turn | tool_call_id | 1.0 | 30 | 1 | 97% (83–99) | 0% (0–12) | 100% | 100% | 3% | 0% |
| memo | qwen2.5:3b | turn | args_hash | 0.0 | 30 | 0 | 53% (36–70) | 0% (0–11) | 100% | 100% | 47% | 0% |
| memo | qwen2.5:3b | turn | args_hash | 0.7 | 30 | 0 | 97% (83–99) | 0% (0–11) | 100% | 100% | 3% | 0% |
| memo | qwen2.5:3b | turn | args_hash | 1.0 | 30 | 1 | 97% (83–99) | 0% (0–12) | 100% | 100% | 3% | 0% |
| memo | qwen2.5:3b | turn | position | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | 100% | 100% | 100% | 0% |
| memo | qwen2.5:3b | turn | position | 0.7 | 30 | 0 | 0% (0–11) | 0% (0–11) | 100% | 100% | 100% | 0% |
| memo | qwen2.5:3b | turn | position | 1.0 | 30 | 1 | 0% (0–12) | 0% (0–12) | 100% | 100% | 100% | 0% |
| memo | qwen2.5:3b | step | tool_call_id | 0.0 | 30 | 0 | 3% (1–17) | 0% (0–11) | 3% | 100% | 97% | 0% |
| memo | qwen2.5:3b | step | tool_call_id | 0.7 | 30 | 0 | 3% (1–17) | 0% (0–11) | 3% | 100% | 97% | 0% |
| memo | qwen2.5:3b | step | tool_call_id | 1.0 | 30 | 2 | 4% (1–18) | 0% (0–12) | 4% | 100% | 96% | 0% |
| memo | qwen2.5:3b | step | args_hash | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| memo | qwen2.5:3b | step | args_hash | 0.7 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| memo | qwen2.5:3b | step | args_hash | 1.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| memo | qwen2.5:3b | step | position | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| memo | qwen2.5:3b | step | position | 0.7 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| memo | qwen2.5:3b | step | position | 1.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | 0% | 100% | 100% | 0% |
| memo | llama3.2:3b | turn | tool_call_id | 0.0 | 30 | 30 | — | — | — | — | — | — |
| memo | llama3.2:3b | turn | tool_call_id | 0.7 | 30 | 30 | — | — | — | — | — | — |
| memo | llama3.2:3b | turn | tool_call_id | 1.0 | 30 | 27 | 0% (0–56) | 0% (0–56) | 0% | 0% | 0% | 67% |
| memo | llama3.2:3b | turn | args_hash | 0.0 | 30 | 30 | — | — | — | — | — | — |
| memo | llama3.2:3b | turn | args_hash | 0.7 | 30 | 30 | — | — | — | — | — | — |
| memo | llama3.2:3b | turn | args_hash | 1.0 | 30 | 28 | 0% (0–66) | 0% (0–66) | 0% | 0% | 0% | 100% |
| memo | llama3.2:3b | turn | position | 0.0 | 30 | 30 | — | — | — | — | — | — |
| memo | llama3.2:3b | turn | position | 0.7 | 30 | 28 | 0% (0–66) | 0% (0–66) | 0% | 0% | 0% | 100% |
| memo | llama3.2:3b | turn | position | 1.0 | 30 | 30 | — | — | — | — | — | — |
| memo | llama3.2:3b | step | tool_call_id | 0.0 | 30 | 30 | — | — | — | — | — | — |
| memo | llama3.2:3b | step | tool_call_id | 0.7 | 30 | 29 | 0% (0–79) | 0% (0–79) | 0% | 0% | 0% | 100% |
| memo | llama3.2:3b | step | tool_call_id | 1.0 | 30 | 29 | 0% (0–79) | 0% (0–79) | 0% | 0% | 0% | 100% |
| memo | llama3.2:3b | step | args_hash | 0.0 | 30 | 30 | — | — | — | — | — | — |
| memo | llama3.2:3b | step | args_hash | 0.7 | 30 | 28 | 0% (0–66) | 0% (0–66) | 0% | 0% | 0% | 100% |
| memo | llama3.2:3b | step | args_hash | 1.0 | 30 | 28 | 0% (0–66) | 0% (0–66) | 0% | 0% | 0% | 50% |
| memo | llama3.2:3b | step | position | 0.0 | 30 | 30 | — | — | — | — | — | — |
| memo | llama3.2:3b | step | position | 0.7 | 30 | 29 | 0% (0–79) | 0% (0–79) | 0% | 0% | 0% | 100% |
| memo | llama3.2:3b | step | position | 1.0 | 30 | 28 | 0% (0–66) | 0% (0–66) | 0% | 0% | 0% | 100% |
| two | qwen2.5:3b | turn | tool_call_id | 0.0 | 30 | 0 | 100% (89–100) | 0% (0–11) | n/a | 100% | 0% | 100% |
| two | qwen2.5:3b | turn | tool_call_id | 0.7 | 30 | 0 | 100% (89–100) | 0% (0–11) | n/a | 97% | 0% | 93% |
| two | qwen2.5:3b | turn | tool_call_id | 1.0 | 30 | 3 | 100% (88–100) | 0% (0–12) | n/a | 74% | 0% | 89% |
| two | qwen2.5:3b | turn | args_hash | 0.0 | 30 | 0 | 7% (2–21) | 0% (0–11) | n/a | 100% | 93% | 100% |
| two | qwen2.5:3b | turn | args_hash | 0.7 | 30 | 0 | 30% (17–48) | 0% (0–11) | n/a | 93% | 67% | 90% |
| two | qwen2.5:3b | turn | args_hash | 1.0 | 30 | 2 | 68% (49–82) | 0% (0–12) | n/a | 57% | 14% | 93% |
| two | qwen2.5:3b | turn | position | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | n/a | 100% | 100% | 97% |
| two | qwen2.5:3b | turn | position | 0.7 | 30 | 0 | 10% (3–26) | 3% (1–17) | n/a | 100% | 90% | 97% |
| two | qwen2.5:3b | turn | position | 1.0 | 30 | 0 | 10% (3–26) | 0% (0–11) | n/a | 70% | 60% | 83% |
| two | qwen2.5:3b | step | tool_call_id | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | n/a | 100% | 100% | 100% |
| two | qwen2.5:3b | step | tool_call_id | 0.7 | 30 | 1 | 10% (4–26) | 3% (1–17) | n/a | 100% | 86% | 83% |
| two | qwen2.5:3b | step | tool_call_id | 1.0 | 30 | 3 | 4% (1–18) | 4% (1–18) | n/a | 81% | 74% | 59% |
| two | qwen2.5:3b | step | args_hash | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | n/a | 100% | 100% | 100% |
| two | qwen2.5:3b | step | args_hash | 0.7 | 30 | 0 | 7% (2–21) | 0% (0–11) | n/a | 83% | 77% | 90% |
| two | qwen2.5:3b | step | args_hash | 1.0 | 30 | 3 | 7% (2–23) | 7% (2–23) | n/a | 74% | 63% | 67% |
| two | qwen2.5:3b | step | position | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | n/a | 100% | 100% | 93% |
| two | qwen2.5:3b | step | position | 0.7 | 30 | 0 | 23% (12–41) | 0% (0–11) | n/a | 87% | 67% | 93% |
| two | qwen2.5:3b | step | position | 1.0 | 30 | 2 | 4% (1–18) | 11% (4–27) | n/a | 89% | 75% | 68% |
| two | llama3.2:3b | turn | tool_call_id | 0.0 | 30 | 0 | 100% (89–100) | 0% (0–11) | n/a | 93% | 0% | 100% |
| two | llama3.2:3b | turn | tool_call_id | 0.7 | 30 | 3 | 67% (48–81) | 15% (6–32) | n/a | 63% | 0% | 100% |
| two | llama3.2:3b | turn | tool_call_id | 1.0 | 30 | 12 | 22% (9–45) | 6% (1–26) | n/a | 22% | 0% | 100% |
| two | llama3.2:3b | turn | args_hash | 0.0 | 30 | 0 | 3% (1–17) | 0% (0–11) | n/a | 77% | 73% | 100% |
| two | llama3.2:3b | turn | args_hash | 0.7 | 30 | 3 | 15% (6–32) | 11% (4–28) | n/a | 56% | 30% | 100% |
| two | llama3.2:3b | turn | args_hash | 1.0 | 30 | 12 | 11% (3–33) | 11% (3–33) | n/a | 33% | 6% | 100% |
| two | llama3.2:3b | turn | position | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | n/a | 93% | 93% | 100% |
| two | llama3.2:3b | turn | position | 0.7 | 30 | 2 | 29% (15–47) | 46% (30–64) | n/a | 57% | 11% | 100% |
| two | llama3.2:3b | turn | position | 1.0 | 30 | 10 | 5% (1–24) | 30% (15–52) | n/a | 35% | 5% | 95% |
| two | llama3.2:3b | step | tool_call_id | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | n/a | 97% | 97% | 100% |
| two | llama3.2:3b | step | tool_call_id | 0.7 | 30 | 2 | 11% (4–27) | 14% (6–31) | n/a | 79% | 61% | 100% |
| two | llama3.2:3b | step | tool_call_id | 1.0 | 30 | 8 | 0% (0–15) | 9% (3–28) | n/a | 50% | 41% | 95% |
| two | llama3.2:3b | step | args_hash | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | n/a | 90% | 90% | 100% |
| two | llama3.2:3b | step | args_hash | 0.7 | 30 | 4 | 0% (0–13) | 15% (6–34) | n/a | 77% | 62% | 100% |
| two | llama3.2:3b | step | args_hash | 1.0 | 30 | 9 | 0% (0–15) | 10% (3–29) | n/a | 24% | 14% | 100% |
| two | llama3.2:3b | step | position | 0.0 | 30 | 0 | 0% (0–11) | 0% (0–11) | n/a | 100% | 100% | 100% |
| two | llama3.2:3b | step | position | 0.7 | 30 | 2 | 4% (1–18) | 11% (4–27) | n/a | 68% | 57% | 100% |
| two | llama3.2:3b | step | position | 1.0 | 30 | 10 | 0% (0–16) | 10% (3–30) | n/a | 30% | 20% | 95% |

## Pre-registered hypotheses

| # | prediction | held? | observed |
|---|---|---|---|
| R2-H1 | step: 0 duplicates and 0 lost effects, every cell | ❌ falsified | 23 dup, 24 lost |
| R2-H2 | memo, turn+args_hash: ≤ 10% at T=0, ≥ 50% at T=1 | ❌ falsified | T=0 ['53%'], T=1 ['97%', '0%'] |
| R2-H3 | memo, turn+position: ≤ 10% every cell | ✅ held | 0%–0% |
| R2-H4 | two, turn+position: lost effect > 0 at T ≥ 0.7 | ✅ held | 20 lost across 168 runs |
| R2-H5 | two, turn+args_hash: ≤ 10% duplicates every cell | ❌ falsified | 3%–68% |

## Notes (written by hand after the automated analysis above; not part of it)

### R2-H6: replicating R1 on the cluster
Running R1's own analysis (`analyze.py`) on R2's `credit` rows:

- **All rows: R2-H6 is falsified as written.** On the cluster H2 and H5 come
  out falsified (H1, H3, H4 held). The cause: llama3.2:3b reached the fault in
  9 / 540 credit runs here (0 / 540 on the Mac), and cells with n = 1 or 2
  show 0% duplicates and 0% re-ask.
- **qwen2.5:3b only (exploratory): every R1 verdict is reproduced** (H1–H5
  held, same 100% / 0% values).

### R2-H1 (falsified): what the 44 step-recording failures are (exploratory)
In **44 / 44** step-recording runs with a duplicate or a lost effect,
recovery re-executed the recorded credit call under its **recorded key**,
and the ledger deduplicated it. The failure came afterwards:

- **23 duplicates:** the model later made a *new* credit decision for a case
  it had already credited (e.g. re-crediting case 7 with a corrected amount).
- **21 lost effects:** the run ended with a notification and a success reply
  while a named case was never credited. In 10 of these the model later made
  another credit decision; in 11 it made none.

So these are model errors that happen after recovery, not errors made by
recovery. Whether they happen *more often* after a crash than without one is
what R3 (crash-free control, pre-registered after these results) measures.
H1 stays falsified: it was written as "0 failures" and did not separate the
two.

### Other observations (exploratory)
- **memo, turn + args_hash, qwen, T = 0: 53% duplicates.** In 16 / 30
  re-asks at temperature 0 the model wrote a different `reason`, so the
  hash changed. R2 serves 16 concurrent requests per GPU; this round alone
  cannot say whether the T = 0 divergence comes from batched serving or
  from the change of seed between invocations.
- **two, turn + position: lost effects occur** (R2-H4 held), mostly with
  llama3.2:3b at T ≥ 0.7, as predicted: a re-asked model takes the cases in a
  different order, the ordinal key collides, and the ledger drops a real
  credit.
- **Wrong amounts are common in `two`** (amendment A1): both models often
  credit $50 (the sum) or a made-up figure for a case. The `correct` column
  does not check amounts; `wrong amount` does.
- llama3.2:3b frequently writes tool calls as plain text, as in R1, so many
  llama cells have few or no kill-triggered runs (see the "excl." column).

### Deviations
- The first R2 job (11037866) was aborted and discarded
  (amendment A2); the measured R2 is job 11038062. It ran on an **A100-SXM4-80GB** (node d1028). The prereg
  says "one NVIDIA A100" without a memory size; the GPU smoke tests ran on
  40 GB A100s.
