# 0032 — Sessions decouple from channels

- **Status:** Accepted
- **Date:** 2026-08-02
- **Author:** Param Madan

> Backfill: this principle has been load-bearing since the mandate and appears
> in the architecture diagrams, but was never recorded as an ADR
> (audit 2026-08-02 §1). This records the as-built decision for review.

## Context

A durable session outlives any single connection. If session state lives in a
channel (a web socket, a bot conversation, a queue consumer), losing the
channel loses the session — which contradicts the durability contract
(ADR-0002). The runtime already serves five surfaces: CLI, playground, MCP,
Service Bus, and Semantic Kernel callers.

## Decision

The session — identified by its session id, embodied entirely by its event
log — is the unit of durability. Channels are stateless surfaces that do
exactly two things: start-or-re-drive a session, and read its derived state.
No channel holds session state; any channel can resume any session. A run
started in the playground can be approved from the CLI, resumed over MCP,
and replayed from Cosmos, because all of them speak to the same log.

## Consequences

Channel handoff (the Phase 4 goal: start in chat, continue by voice) is a
property of the architecture, not a feature to build — each new channel is
a thin adapter. The cost: every channel must derive status from the log
(`SessionStates`) rather than caching its own, and channel-specific context
(e.g., a Teams conversation reference for proactive messages) will need a
recorded home when the first real push channel lands.

## Verified by

Cross-surface demos: pause in one MCP server process, approve and complete in
another; Service Bus duplicate delivery re-driving the same session
(ADR-0026); `turn` CLI resuming a conversation from fresh processes.
