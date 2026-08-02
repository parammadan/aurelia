# 0031 — Replay reconstructs open conversations to the last completed turn

- **Status:** Accepted
- **Date:** 2026-08-02
- **Author:** Param Madan

## Context

ADR-0004's replay is terminal-only: in-flight sessions throw. Conversations
(ADR-0028) are *deliberately* non-terminal — with the old rule, "replay any
session" would stop being true for the flagship use case the moment it ships.

## Decision

For a session with at least one completed turn (an `AgentMessageSent`
present), replay reconstructs deterministically **up to the last completed
turn** and returns that turn's agent message as the state; integrity
invariants are enforced over the whole log as before, and an in-flight
current turn is reported, not an error. A session with zero completed turns
still throws `SessionNotCompletedException`. This amends the behavior
specified alongside ADR-0004 without weakening its determinism claim — turns
are deterministic units, so a turn-boundary cut is exact.

## Consequences

Replay and the trace viewer stay universal as conversations arrive. Consumers
must note that replay-of-open answers "as of turn N", which the result should
state explicitly.
