# 0013 — Approval requests carry policy and optional expiry

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

An approval intent could record only the question, with approver policy and
timeouts bolted on later via upcasters (ADR-0009) — or a full enterprise
workflow (delegation, escalation chains, quorums) could be modeled now, unused
and therefore guessed wrong. The Phase 1 reference agent needs "manager
approves the refund, or the request times out and escalates."

## Decision

The day-one approval intent records what is being asked, who may approve
(a policy reference, not a user table), and an optional expiry. Expiry firing
respects ADR-0006: the runtime, not core code, observes the clock and injects
a recorded time event that produces an `Expired` outcome for the approval's
key. Delegation, escalation chains, and quorums are explicitly out of scope
until a real requirement arrives.

## Consequences

The refund demo's timeout-and-escalate story works without a schema bump, at
the cost of two fields. Expiry becomes the first concrete use of the
injected-time rule, forcing that machinery to exist early. Anything fancier
than single-policy-plus-expiry waits for evidence.
