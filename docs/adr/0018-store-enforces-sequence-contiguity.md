# 0018 — The store enforces sequence contiguity

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Until now, single-writer-per-session was enforced by convention only: nothing
stopped two processes (a playground auto-restart racing a manual CLI run, an
approval racing a resuming worker) from folding the same log concurrently and
interleaving appends. Every event already carries the `Sequence` its writer
computed from the fold, which makes a stale writer detectable: its sequence
was derived from an outdated view of the log.

## Decision

`IEventStore.AppendAsync` atomically asserts that the event's `Sequence` is
exactly the last stored sequence plus one for that session; a violation throws
`SequenceConflictException` and nothing is written. On conflict the losing
writer **fails its activation loudly** — no auto-refold, no merge — and the
caller re-invokes against the fresh log. (`Approvals.ResolveAsync` gets a
small bounded retry, since re-reading there is trivially safe.) A torn,
unterminated tail line — possible after a hard kill mid-append — was never
durable and is truncated before the next append.

## Consequences

A stale writer physically cannot corrupt a session log, and genuine
double-driver bugs surface as errors instead of silent interleaving. The
contract maps directly onto Cosmos DB conditional writes (ETag /
expected-version) when the durable store lands. The file store pays an
exclusive-lock + tail-scan cost per append, accepted at demo scale.
