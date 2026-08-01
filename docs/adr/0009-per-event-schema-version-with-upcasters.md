# 0009 — Per-event schema version with upcasters

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

The event log is immutable and must replay years after it was written, while
the code that reads it keeps evolving. "Additive-only changes forever" is a
promise nobody keeps, and versioned type names (`ToolCallRequestedV2`) leak
historical clutter into every consumer including replay logic.

## Decision

The envelope carries the event type discriminator and an integer
`schemaVersion`. When an event's shape changes, its version increments and an
**upcaster** — a pure function from version N to N+1 — is registered on the
read path. Stored events are upcast to the current shape at read time, in one
place, so the fold, recovery, and replay only ever see the latest schema.
Upcasters are chained, never skipped, and covered by tests that pin down
historical shapes.

## Consequences

Every event shape change costs an upcaster and its test, which keeps schema
changes deliberate. In exchange the log never needs migration, old sessions
stay replayable indefinitely, and no historical shape ever appears outside the
read path. Upcasting machinery is perimeter code; the shapes and versions it
lifts between are core.
