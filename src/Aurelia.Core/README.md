# Aurelia.Core — handwritten zone

This project is the core boundary: the execution model, event schema, replay
semantics, and tool contract. Per [ADR-0003](../../docs/adr/0003-core-perimeter-authorship-boundary.md),
everything in this project is authored by Param personally.

**Current state: interfaces and placeholder stubs only.** Every type in here is
a sketch that exists so the perimeter (runtime host, crash harness, CI) has
something to compile against. Placeholders are marked `TODO(param)` and each
real design lands as an ADR in `docs/adr/` before the code changes.

Rules for this directory:

1. No AI-generated implementation logic. AI may not add, extend, or "improve"
   anything here beyond mechanical fixes explicitly requested.
2. Interface changes originate from a design conversation and an ADR entry,
   in that order.
3. The perimeter adapts to the core, never the reverse.
