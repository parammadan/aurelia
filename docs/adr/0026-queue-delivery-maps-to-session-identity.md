# 0026 — Queue delivery maps to deterministic session identity

- **Status:** Accepted
- **Date:** 2026-08-02
- **Author:** Param Madan

## Context

The autonomous reference agent consumes work from an Azure Service Bus queue.
Queues deliver at-least-once: duplicate delivery is not an edge case, it is
the contract. Benchmark fault F9 exists precisely because most agent stacks
handle a redelivered task by re-executing it.

## Decision

A queue message's identity **is** its session identity: the listener derives
the session id deterministically from the message id
(`sb-{sanitized MessageId}`). A duplicate delivery therefore re-drives an
existing session, which — by the resumability contract (ADR-0002) — is a
no-op for completed sessions and a resume for interrupted ones. Exactly-once
task execution over an at-least-once queue falls out of session identity; no
broker-side dedup feature is required (the Basic tier suffices).

A session that pauses at an approval gate **parks**: the message is completed
(the durable log holds the state), and resolution arrives later through any
surface (CLI, MCP, playground) — no human in the loop until the agent asks.
A processing exception abandons the message for redelivery, which lands on
the same session and resumes it.

## Consequences

The queue is pure transport; all semantics live in the session log, keeping
one recovery model for interactive and autonomous work. Poison messages
(sessions that terminally fail on every redelivery) surface as `SessionFailed`
logs plus queue dead-lettering — that policy tuning is deferred. Message ids
must be meaningful (producer-assigned business ids give business-level dedup;
random ids give delivery-level dedup only).
