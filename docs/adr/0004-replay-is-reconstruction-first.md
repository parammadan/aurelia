# 0004 — Replay is reconstruction first, re-execution later

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Temporal and Durable Task achieve deterministic replay by re-running
deterministic workflow code and substituting recorded activity results at each
call site. An agent runtime cannot borrow that model wholesale: the
orchestration logic includes LLM calls, which are nondeterministic even at
temperature zero. "Deterministic replay" therefore has two candidate meanings —
pure reconstruction (a fold over the event log with every external result
substituted) and re-execution with substitution (re-run the loop, substitute
recorded results, detect divergence).

## Decision

Phase 1 `IReplayEngine` promises pure reconstruction: replay is a fold over
the event log in which every external result — model output, tool result,
injected time and randomness — is substituted from the recording and nothing
is re-executed. This is deterministic by construction and is what powers the
trace viewer and crash-recovery debugging.

The event schema nevertheless records the **full inputs** of every external
call from day one — exact prompt, model identifier, request parameters, and
config/prompt versions — so that re-execution and counterfactual modes can be
added later without a schema migration. A counterfactual run ("same session,
different model") is a distinct *experiment* concept, not a mode of replay.

## Consequences

Replay determinism is provable rather than aspirational, and the trace viewer
becomes cheap to build. Recording full inputs costs storage before anything
consumes them, which is accepted as insurance. The matching and
divergence-detection machinery that re-execution requires is deferred until a
real execution model exists to exercise it (its ground rules are already fixed
by ADR-0005).
