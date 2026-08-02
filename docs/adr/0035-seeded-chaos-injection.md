# 0035 — Chaos injection is seeded, parseable, and wraps at the perimeter

- **Status:** Proposed
- **Date:** 2026-08-02
- **Author:** Param Madan

> Backfill: the chaos API shipped 2026-08-01 with no ADR (audit 2026-08-02
> §1). This records the as-built decision for review.

## Context

The crash harness (ADR-0002) proves recovery from one fault at a time.
Measuring recovery *rates* needs many faults, reproducibly — a chaos run
that can't be replayed with the same seed is an anecdote.

## Decision

Fault injection is a perimeter concern configured by a parseable spec:
`ChaosPolicy.Parse("seed=7,tool_fail=0.25,model_malformed=0.1,model_fail=0.1")`
drives a seeded `ChaosSource` (deterministic RNG — same seed, same fault
sequence). Injection wraps the real components: `ChaosTool` and
`ChaosCompensatableTool` are separate wrappers so wrapping can never *add*
compensability a tool doesn't have; `ChaosModelClient` emits digit-free
garbage (malformed output that can't accidentally satisfy digit-extraction
agents). Wiring: a `chaos` parameter on agent factories, `eval --chaos`, and
the `AURELIA_CHAOS` env var for spawned workers. Core knows nothing about
chaos — faults arrive as ordinary tool/model failures.

## Consequences

Recovery-rate experiments are reproducible (experiment 002's rates track the
retry-math prediction). Because injection is cooperative (in-process
wrappers), it can't fault the process boundary itself — that gap is why the
benchmark harness owns a *physical* HTTP tool boundary (separate decision).

## Verified by

`ChaosTests`, experiment `docs/experiments/002-chaos-recovery.md` (seeded,
reproducible), `eval --chaos` in experiments.
