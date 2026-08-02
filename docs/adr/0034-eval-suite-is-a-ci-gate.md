# 0034 — The eval suite is a CI gate

- **Status:** Proposed
- **Date:** 2026-08-02
- **Author:** Param Madan

> Backfill: the eval runner shipped 2026-08-01 with no ADR (audit 2026-08-02
> §1). This records the as-built decision for review.

## Context

Unit tests assert mechanisms; they don't assert that the reference agents
still accomplish their tasks end-to-end. Agent regressions are often
behavioral (a prompt change, a route change) and slip past mechanism tests.

## Decision

A scenario suite (`evals/phase1.json`, currently 15 scenarios: happy paths,
sagas, idempotent reruns, replay checks, goal parsing, approvals) runs as a
CI gate alongside the build and the crash harness — a red eval blocks merge
exactly like a red test. Scenarios declare an agent, a goal, and expectations
(`expectFinalContains`, ledger shape, replay equality). The runner
(`EvalRunner`, `eval --suite` CLI) supports `--repeat`/`--filter`/`--chaos`
for experiments, reporting p50/p95 latency and token sums read from the
event log. CI runs against `FakeModelClient` — deterministic and free; the
same suite runs unchanged against real backends (Ollama, Azure OpenAI) for
live validation.

## Consequences

Behavioral regressions fail loudly pre-merge. The fake-model gate means CI
proves orchestration behavior, not model quality — live-model runs remain a
manual/experiment concern (recorded in `docs/experiments/`). Scenario JSON
becomes an interface to maintain; expectation fragments must avoid
JSON-escaping traps (apostrophes, learned the hard way).

## Verified by

`.github/workflows/ci.yml` (eval step), `evals/phase1.json`,
experiments 001/002 built on `--repeat`/`--filter`/`--chaos`.
