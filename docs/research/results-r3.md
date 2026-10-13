# Paper 1 — round 3 results (crash-free control)

Sources: `data/r2.jsonl` (recovered) · `data/r3.jsonl` (1620 crash-free runs). Pooled over both models and all temperatures. Intervals: Newcombe hybrid score, 95%.

## step-recovered (R2) vs crash-free (R3)

| task | keys | dup recovered | dup crash-free | Δ dup [95% CI] | lost recovered | lost crash-free | Δ lost [95% CI] |
|---|---|---|---|---|---|---|---|
| credit | tool_call_id | 0/90 (0%) | 0/90 (0%) | +0 pp [-4, +4] | 0/90 (0%) | 0/90 (0%) | +0 pp [-4, +4] |
| credit | args_hash | 0/89 (0%) | 0/92 (0%) | +0 pp [-4, +4] | 0/89 (0%) | 0/92 (0%) | +0 pp [-4, +4] |
| credit | position | 0/89 (0%) | 0/92 (0%) | +0 pp [-4, +4] | 0/89 (0%) | 0/92 (0%) | +0 pp [-4, +4] |
| memo | tool_call_id | 3/90 (3%) | 0/93 (0%) | +3 pp [-1, +9] | 0/90 (0%) | 0/93 (0%) | +0 pp [-4, +4] |
| memo | args_hash | 0/94 (0%) | 0/91 (0%) | +0 pp [-4, +4] | 0/94 (0%) | 0/91 (0%) | +0 pp [-4, +4] |
| memo | position | 0/93 (0%) | 1/90 (1%) | -1 pp [-6, +3] | 0/93 (0%) | 0/90 (0%) | +0 pp [-4, +4] |
| two | tool_call_id | 7/166 (4%) | 6/168 (4%) | +1 pp [-4, +5] | 8/166 (5%) | 8/168 (5%) | +0 pp [-5, +5] |
| two | args_hash | 4/164 (2%) | 3/168 (2%) | +1 pp [-3, +5] | 8/164 (5%) | 8/168 (5%) | +0 pp [-5, +5] |
| two | position | 9/166 (5%) | 11/169 (7%) | -1 pp [-6, +4] | 8/166 (5%) | 7/169 (4%) | +1 pp [-4, +6] |

## turn-recovered (R2) vs crash-free (R3)

| task | keys | dup recovered | dup crash-free | Δ dup [95% CI] | lost recovered | lost crash-free | Δ lost [95% CI] |
|---|---|---|---|---|---|---|---|
| credit | tool_call_id | 89/92 (97%) | 0/90 (0%) | +97 pp [+90, +99] | 0/92 (0%) | 0/90 (0%) | +0 pp [-4, +4] |
| credit | args_hash | 0/89 (0%) | 0/92 (0%) | +0 pp [-4, +4] | 0/89 (0%) | 0/92 (0%) | +0 pp [-4, +4] |
| credit | position | 0/93 (0%) | 0/92 (0%) | +0 pp [-4, +4] | 0/93 (0%) | 0/92 (0%) | +0 pp [-4, +4] |
| memo | tool_call_id | 88/92 (96%) | 0/93 (0%) | +96 pp [+88, +98] | 0/92 (0%) | 0/93 (0%) | +0 pp [-4, +4] |
| memo | args_hash | 73/91 (80%) | 0/91 (0%) | +80 pp [+70, +87] | 0/91 (0%) | 0/91 (0%) | +0 pp [-4, +4] |
| memo | position | 0/91 (0%) | 1/90 (1%) | -1 pp [-6, +3] | 0/91 (0%) | 0/90 (0%) | +0 pp [-4, +4] |
| two | tool_call_id | 139/162 (86%) | 6/168 (4%) | +82 pp [+75, +87] | 5/162 (3%) | 8/168 (5%) | -2 pp [-6, +3] |
| two | args_hash | 37/163 (23%) | 3/168 (2%) | +21 pp [+14, +28] | 5/163 (3%) | 8/168 (5%) | -2 pp [-6, +3] |
| two | position | 15/168 (9%) | 11/169 (7%) | +2 pp [-3, +8] | 20/168 (12%) | 7/169 (4%) | +8 pp [+2, +14] |

## Pre-registered hypotheses

| # | prediction | held? | detail |
|---|---|---|---|
| R3-H1 | step-recovered vs crash-free: both Δ intervals include 0, every task × keys | ✅ held | all cells include 0 |
| R3-H2 | turn + tool_call_id: duplicates ≥ 30 pp above crash-free in credit and memo | ✅ held | |
| R3-H3 | crash-free: args_hash duplicate rate ≤ tool_call_id, every task | ✅ held | |
