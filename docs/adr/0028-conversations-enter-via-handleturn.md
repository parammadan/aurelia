# 0028 — Conversations enter via HandleTurnAsync, alongside single-shot

- **Status:** Accepted
- **Date:** 2026-08-02
- **Author:** Param Madan

## Context

Everything to date is single-activation: one goal, one run, `TurnOrdinal 0`.
The turn machinery (ADR-0007's ordinals, ADR-0010's keys) was designed for
conversations and has been waiting for them. Migrating every existing agent
to a turns-only model churns the queue and goal-driven agents for no gain;
modeling turns as child sessions (ADR-0023) would push conversational memory
across the session boundary that ADR-0025 deliberately seals.

## Decision

`ISessionRuntime` gains `HandleTurnAsync(sessionId, userMessage)`: each user
message opens turn N (`UserMessageReceived`), the agent's script executes
scoped to that turn — intents keyed `(session, N, step)` exactly as ADR-0007
defined — and the turn closes with `AgentMessageSent`, whose content is the
turn's result. `RunToCompletionAsync` survives unchanged for goal-driven and
queue agents. One log, one recovery story: re-driving an interrupted turn
resumes it mid-step like any other activation. Conversations have no
`SessionCompleted`; they are open until explicitly closed or failed.

## Consequences

Turn ordinals stop being decorative. Recovery, approval pauses, sagas, and
sub-agents all work per-turn with zero new machinery. Consumers deriving
status must treat an open conversation as a normal long-lived state, not an
anomaly (see ADR-0031 for replay).
