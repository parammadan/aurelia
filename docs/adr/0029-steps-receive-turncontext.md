# 0029 — Steps receive an explicit TurnContext

- **Status:** Accepted
- **Date:** 2026-08-02
- **Author:** Param Madan

## Context

Step builders currently receive `(goal, results)`. Conversational steps need
history. Injecting history into model prompts behind the builder's back keeps
signatures stable but corrupts provenance: the recorded `PromptJson` would
contain content the builder never wrote, and a window-size change would
silently alter recorded prompts.

## Decision

Builders receive a `TurnContext`: the current user message (or goal), the
**window** (recent turns and any active summary, per ADR-0030), and this
turn's step results. What a builder constructs is exactly what the intent
records — provenance stays clean and replay-faithful. This is one mechanical
signature change across all step types and agents, taken once.

## Consequences

Prompt construction is explicit and testable; two agents can window the same
history differently. The signature migration touches every agent and test in
one sweep — the cost of not having magic.
