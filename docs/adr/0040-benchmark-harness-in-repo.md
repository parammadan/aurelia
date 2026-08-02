# 0040 — The benchmark harness lives in-repo behind a neutral adapter contract

- **Status:** Proposed
- **Date:** 2026-08-02
- **Author:** Param Madan

> Backfill: the harness, adapters, and tool server shipped without ADRs —
> governed only by `docs/benchmark/methodology.md` and `harness.md`, which
> are specs, not decisions records (audit 2026-08-02 §1). This records the
> as-built engineering decisions for review. Note: all results produced by
> this harness to date are **withdrawn** (methodology header, 2026-08-02);
> this ADR covers the machinery, not the numbers.

## Context

The Orchestration Reliability Benchmark needs an executable harness. Building
it inside a contestant's repo is a declared conflict of interest; building it
elsewhere first would have meant designing the contract with no
implementation pressure.

## Decision

`src/Aurelia.Benchmark` hosts the harness for now, structured so extraction
is mechanical: contestants implement `IFrameworkAdapter` (start, recover,
query-state), scoring reads **only** the harness-owned ledger plus the
adapter's state claims, and all tools are served by a harness-owned HTTP
tool server (`src/Aurelia.ToolServer`) so fault injection is physical —
faults and kills hit real processes at a real network boundary, identically
for every contestant. Faults follow the methodology's taxonomy; runs are
seeded and emit raw per-run JSON to `docs/experiments/data/`. Framework
adapters (LangGraph, Temporal) live under `benchmark/adapters/` as Python
implementations of their documented durable patterns.

## Consequences

The conflict of interest is structural until the neutral-repo extraction
happens; the mitigations (adapter contract, physical boundary, raw data,
withdrawal of compromised results, maintainer review before any public
claim) are process, not proof. Rerun configurations must be pre-registered
and owner-approved before measurement — the Temporal mid-measurement config
change is the standing example of why.

## Verified by

Adapter-based reruns reproducing prior cells exactly after the neutral-core
refactor; the harness code and raw data committed for inspection.
