# 0003 — Core/perimeter authorship boundary

- **Status:** Superseded by [0014](0014-retire-authorship-boundary.md)
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

AI coding agents build most of this project's perimeter: tests, storage
boilerplate, CI, tooling, docs, UI. The risk is AI-generated code drifting
into the runtime's semantic core — the parts whose every line must be
personally defensible in a design review.

## Decision

`src/Aurelia.Core` is the handwritten zone. It contains the execution model,
event schema, tool contract, and replay semantics, authored by Param
personally. AI agents may create interfaces and placeholder stubs there when
explicitly directed, marked `TODO(param)`, but may not add implementation
logic. Everything outside Core is perimeter: AI-built, human-reviewed at the
boundaries, and marked with a `PERIMETER` header comment. Provisional logic
that Core will eventually own (like `ProvisionalSessionDriver`) lives in the
perimeter and is deleted when the handwritten version lands — it is never
promoted into Core.

## Consequences

Core progress is bounded by Param's personal bandwidth, by design. The
perimeter can grow fast without contaminating what matters. Review effort
concentrates at the interface boundary, which stays small.
