# 0045 — A fork records its lineage in its own log

- **Status:** Accepted
- **Date:** 2026-08-04
- **Author:** Param Madan

## Context

Counterfactual forks exist (CaseOps ADR-0020): a fork is an ordinary session
whose log begins as a copy of another session's prefix. Until now the
"where did this come from" fact lived in a sidecar file beside the store,
because the runtime was frozen. A sidecar can be lost, edited, or disagree
with the log — and provenance that can silently diverge from the events it
describes is worth very little.

Fourth change under the owner's scoped freeze lift.

## Decision

`ForkedFrom` (source session id, source sequence) is a session event,
appended **immediately after the copied prefix** — so the fork's log reads:

```
0..n   copied from the source, sequences preserved
n+1    ForkedFrom(source, n)      ← the divergence point, marked in place
n+2..  this fork's own history
```

Appending it after the prefix rather than before keeps copied sequences
intact (no renumbering) and puts the marker exactly where the two histories
part company, which is also where a reader looks for it.

`ForkedFrom` is **not** a `CallRequested`: it has no idempotency key, nothing
executes because of it, and it never affects replay's reconstruction. It is a
fact about the log's origin, recorded in the log.

The runtime provides the event type and nothing else. Forking itself — which
prefix to copy, what to do afterwards — stays in the host (CaseOps ADR-0020),
because the policy of what may be branched is a business question.

## Consequences

Provenance travels with the events it describes: copy the log and its
lineage comes along; read the log and you can see it is a fork without
consulting anything else. The host's sidecar retires. The cost is one more
event type that consumers must recognise, and a fork's log is one event
longer than the prefix it copied.

## Verified by

`tests/Aurelia.CrashTests/ForkLineageTests.cs` (the event round-trips through
the store, sits after the prefix, and does not disturb replay), plus CaseOps'
fork tests reading lineage from the log.
