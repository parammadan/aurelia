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
