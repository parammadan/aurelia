# Experiment 001 — Routing the classify step to a local model

- **Date:** 2026-08-01
- **Claim under test (Phase 2 exit gate):** a routing policy can cut median
  task latency and cloud-model cost at equal task success.
- **Verdict:** **supported.** Local-first routing cut median end-to-end task
  latency **82%** (1909 ms → 337 ms) and cloud-model invocations from
  20 to ~0, at equal task success (20/20 both policies).

## Hypothesis

The case-triage agent's model step is trivial extraction (`classify`: pull a
case number out of free text). Work of that class should not need a frontier
cloud model; routing it to a small local model (ADR-0021) should cut latency
and cloud cost without hurting task success, with the cloud model retained as
the fallback (ADR-0022).

## Setup

- **Runtime:** Aurelia @ `main` (ADRs 0020–0022 merged), file event store.
- **Hardware:** Apple M1 MacBook (the local model shares the machine with the
  harness — see threats below).
- **Scenario:** `triage-42-happy` from `evals/phase1.json` — full end-to-end
  task: model classify → case lookup → service credit issuance (ledger write)
  → customer notification, with ledger and replay assertions. Latency is
  whole-task, not model-call-only.
- **Repeats:** 20 per policy, sequential, same process.
- **Policy A — cloud-only:** `classify` → Azure OpenAI `model-router`
  (GlobalStandard, eastus2), no fallback, no budget.
- **Policy B — local-first:** `classify` → Ollama `llama3.2:1b` (local),
  fallback Azure `model-router`, no budget.

Reproduce (keys elided):

```bash
# Policy A
AURELIA_MODEL_ENDPOINT=https://<account>.cognitiveservices.azure.com/openai/v1 \
AURELIA_MODEL_ID=model-router AURELIA_MODEL_API_KEY=... \
dotnet run --project src/Aurelia.Runtime -- eval --suite evals/phase1.json --filter triage-42 --repeat 20

# Policy B
AURELIA_MODEL_ENDPOINT=http://localhost:11434/v1 AURELIA_MODEL_ID=llama3.2:1b \
AURELIA_MODEL_FALLBACK_ENDPOINT=https://<account>.cognitiveservices.azure.com/openai/v1 \
AURELIA_MODEL_FALLBACK_ID=model-router AURELIA_MODEL_FALLBACK_KEY=... \
dotnet run --project src/Aurelia.Runtime -- eval --suite evals/phase1.json --filter triage-42 --repeat 20
```

## Results

| Policy | Task success | p50 | p95 | min | max | Cloud-model invocations |
|---|---|---|---|---|---|---|
| A — cloud-only | 20/20 | **1909 ms** | 2862 ms | 1434 ms | 2862 ms | 20 |
| B — local-first | 20/20 | **337 ms** | 2858 ms | 316 ms | 2858 ms | ~0 (fallback only) |

- **Median task latency: −82%** (1909 → 337 ms).
- **Cloud invocations: −~100%** (20 → approximately 0; see threats — fallback
  usage was not separately counted in the aggregate output).
- **Task success: equal** (20/20 both).
- Policy B's p95 outlier (~2.9 s) is consistent with a one-time local model
  load; its steady state clusters at 316–400 ms.

## Threats to validity

1. **One scenario, one task class.** `classify` is trivial extraction — the
   easiest possible case for a 1B model. This result says nothing about
   routing *planning*-class work to small models; task success would be the
   casualty there, and the whole point of route classes is that it doesn't
   have to be all-or-nothing.
2. **Cost is proxied by invocation count**, not tokens or dollars. Absolute
   per-call cost of `model-router` on this workload is fractions of a cent;
   the ratio (~100% reduction) is the honest claim, not a dollar figure.
   Token capture on `ModelCallSucceeded` is queued as future work.
3. **N = 20, sequential, one machine, one evening.** No confidence intervals;
   the effect size (5.7× at p50, non-overlapping steady-state ranges) is large
   relative to the noise, which is why this thin design is reported at all.
4. **Fallback usage wasn't instrumented in aggregates.** Per-event `ModelId`
   on outcomes (ADR-0020) records who served each call; the eval aggregate
   doesn't surface it yet. Success 20/20 with sub-400 ms steady state implies
   the local model served essentially all runs.
5. **Local model competes with the harness for the same CPU** — flattering to
   neither side in an obvious direction, but unmeasured.

## Conclusion

For extraction-class model calls, local-first routing with a cloud fallback
dominated cloud-only routing on this workload: 82% lower median task latency,
~100% fewer cloud calls, no loss of task success. The route-class design
(ADR-0021) makes this a per-class decision rather than a bet on one model,
and the fallback (ADR-0022) bounds the quality risk. Follow-ups: token/cost
capture on outcomes, fallback-rate in eval aggregates, and a quality-sensitive
scenario class where the trade is expected to reverse.
