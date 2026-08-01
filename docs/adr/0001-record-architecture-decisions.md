# 0001 — Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

Aurelia's core design (execution model, event schema, replay semantics) is
developed incrementally, topic-by-topic in design conversation, rather than in
one big upfront document. Without a written trail, the reasoning behind each
decision is lost — and "how the architecture line was held while AI agents
built the perimeter" is itself part of the project's story.

## Decision

Every architectural decision is recorded as a numbered ADR in `docs/adr/`
before the code that depends on it is written. Accepted ADRs are immutable;
reversals are new ADRs that mark the old one superseded.

## Consequences

Design conversations must end with a written artifact, which adds a small tax
per decision. In exchange, the core remains defensible decision-by-decision,
and the log doubles as the project's design history.
