# 0033 — Token usage is recorded on model outcomes (schema v3)

- **Status:** Proposed
- **Date:** 2026-08-02
- **Author:** Param Madan

> Backfill: schema v3 shipped in code (PR #6) with no ADR — flagged by both
> architecture audits as the most serious governance gap, because it changed
> the core event schema. This records the as-built decision for review.

## Context

Cost questions ("what did this session spend?") should be answerable from the
event log alone — if cost lives only in a metrics pipeline, the log stops
being the complete record of the run. Model backends report token usage on
their responses; some (local models, older gateways) do not.

## Decision

`ModelCallSucceeded` v3 adds nullable `InputTokens`/`OutputTokens`.
`IModelClient.CompleteAsync` returns `ModelCompletion(Content, InputTokens?,
OutputTokens?)` instead of a bare string; clients pass through what the
backend reports (`FakeModelClient` estimates `length/4`, clearly a fake).
Nullable because absence of usage data is a fact worth recording, not an
error. The upcaster chain (ADR-0009) extends 0/1→2→3; old logs replay
unchanged with null token fields. The eval runner sums tokens **from the
log** per scenario — the log is the cost record.

## Consequences

Cost reporting needs no side channel and survives replay. The `IModelClient`
signature change was breaking for implementors (acceptable pre-1.0, noted).
Precedent set: any future per-call economics (price, cache hits) belong on
the outcome event, versioned the same way.

## Verified by

`UpcasterTests` (v1→v3 chain), live Azure OpenAI capture (120 in / 288 out
observed), eval aggregate `· tokens Nin/Mout` sourced from stored events.
