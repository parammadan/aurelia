# 0046 — Endpoint faults are injected as endpoint faults

- **Status:** Accepted
- **Date:** 2026-08-04
- **Author:** Param Madan

## Context

Routing already falls back: a latency-budget breach becomes an attempt
failure and the call is retried on the fallback backend under the same key
(ADR-0022), with the routing decision recorded on the intent (ADR-0020).
What was missing was a way to *cause* the failure honestly. The existing
chaos client (ADR-0035) injects random behaviour by seed — useful for
recovery-rate experiments, wrong for demonstrating failover, where the point
is a specific, repeatable fault at a specific moment.

Fifth and final change under the owner's scoped freeze lift.

## Decision

`FaultyModelClient` wraps a backend and injects **the faults a real endpoint
actually produces**, deterministically:

- **Rate limiting** — throws with a 429-shaped message, the way a provider
  refuses when you exceed quota.
- **Latency spike** — delays before delegating, so a configured latency
  budget trips exactly as it would against a slow provider.
- **Hard failure** — a transport-style exception.

Each fault is configured with the number of calls it affects
(`FailFirst: 2`), so a demo can show *"the primary fails, the fallback
serves, the task completes"* the same way every time. No seeds, no
probabilities: this is a fault switch, not a chaos monkey. Both exist because
they answer different questions.

**Nothing about the failover path changes.** The fault surfaces as an
ordinary attempt failure; the runtime's existing behaviour does the rest —
`CallAttemptFailed` is recorded, the fallback is invoked under the *same*
idempotency key, and `ModelCallSucceeded.ModelId` records **which backend
actually served it**, which is what makes "it rerouted" provable rather than
narrated.

**Language, fixed here:** this is *fallback to a second configured model
endpoint* — in the demo, a local one. It is not a cluster, not
high-availability, not multi-region. Any surface describing it says
"fallback to a local model".

## Consequences

Beat 9 becomes a repeatable, checkable demonstration: the trace shows the
failed attempt, the reroute, and the completion, with the served-by model id
recorded. The cost is a second injection mechanism alongside chaos — kept
distinct on purpose, and named so nobody mistakes a deterministic fault
switch for a statistical experiment.

## Verified by

`tests/Aurelia.CrashTests/EndpointFailoverTests.cs`: a 429 on the primary,
and separately a latency spike past the budget — in both cases the session
**completes**, the log holds the attempt failure, and the success records the
fallback's model id.
