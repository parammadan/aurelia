# 0019 — Cosmos DB event store: serverless, head-document concurrency, Entra-only

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

The durable store needs to implement the ADR-0018 contiguity contract on
Azure. A session is one flat, totally-ordered log (ADR-0007), which maps
naturally to one Cosmos DB logical partition per session — and Cosmos
transactional batches are atomic exactly within one logical partition. The
owner provisioned a fresh credit-capped subscription and delegated the
provisioning defaults; they are recorded here and can be superseded.

## Decision

- **Capacity:** serverless (pay-per-request; no idle cost at demo volume).
  Region **eastus2**. Consistency level **Session**.
- **Layout:** database `aurelia`, container `events`, partition key
  `/sessionId`. Each event is a document (`id` = zero-padded sequence, `type`,
  `data` = the same payload shape the file store writes, via the shared
  `EventSerialization`). Each session also has a **head document**
  (`id = "head"`, `lastSequence`).
- **Contiguity (ADR-0018):** an append is a transactional batch that
  ETag-conditionally replaces the head (or creates it for a new session) and
  creates the event document. A stale writer's batch fails atomically
  (412/409) and surfaces as `SequenceConflictException`. The store also
  pre-checks the head so ordinary staleness fails before the round trip.
- **Auth:** Entra ID only — `disableLocalAuth: true`, so account keys do not
  exist. The runtime uses `DefaultAzureCredential` (developer `az login`
  locally; managed identity in Azure with zero code change), with the
  built-in *Cosmos DB Data Contributor* data-plane role.
- **Selection:** `AURELIA_COSMOS_ENDPOINT` switches the runtime to Cosmos;
  unset means the JSONL file store. Tests and the eval suite stay on the file
  store; a Cosmos integration test runs only where the endpoint is configured.
- Infrastructure is declared in `infra/main.bicep` (Cosmos + Log Analytics +
  Application Insights).

## Consequences

Both stores enforce the same contract, verified by the same exception type,
so the runtime is oblivious to the backend. The head document costs one extra
write per append and serializes appends per session — which is the point.
Cross-partition queries (e.g., "all failed sessions") need a design round of
their own. Credit-capped subscription + serverless means worst-case spend is
bounded; the Consumption budget API rejected this offer type (free trial), so
the credit cap itself — the subscription suspends rather than charges — is
the operative guardrail until the subscription is upgraded.
