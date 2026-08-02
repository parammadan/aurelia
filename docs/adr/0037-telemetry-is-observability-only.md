# 0037 — Telemetry is OpenTelemetry, and it is observability-only

- **Status:** Proposed
- **Date:** 2026-08-02
- **Author:** Param Madan

> Backfill: OTel instrumentation shipped 2026-08-01 with no ADR (audit
> 2026-08-02 §1). This records the as-built decision for review.

## Context

Operators need spans and traces; the runtime needs to never depend on them.
The mandate names OTel → App Insights as the Microsoft-native observability
path.

## Decision

The runtime emits OpenTelemetry spans from a single `ActivitySource`
("Aurelia.Runtime"): a `session.run` span per activation and child spans per
external call, tagged with GenAI semantic conventions where they apply
(model, tokens) plus session id and coordinates. Exporters are opt-in by
environment: OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, Azure Monitor
when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set, console via
`AURELIA_TRACE_CONSOLE=1`; with none set, tracing is inert.

The hard rule: **telemetry is derived, never load-bearing.** Recovery,
replay, and state derivation read only the event log; if every span is
dropped, nothing about correctness changes. Spans carry no data that isn't
in (or derivable from) the log.

## Consequences

Observability plugs into any OTel backend without touching the runtime; App
Insights worked by adding a connection string. The duplication (log events
and spans describe the same calls) is accepted as the price of keeping the
log the single source of truth. Test isolation requires filtering the global
`ActivitySource` by session-id tag (learned from parallel-test pollution).

## Verified by

`TracingTests` (in-memory exporter), live App Insights traces from the
all-Azure run.
