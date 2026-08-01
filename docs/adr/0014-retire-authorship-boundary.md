# 0014 — Retire the core/perimeter authorship boundary

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

ADR-0003 split the codebase into a personally-authored core and a delegated
perimeter to bootstrap the project with maximum design control. With the event
model now fully specified in accepted ADRs (0004–0013) and the guardrails
moved into tooling — the crash-recovery CI contract (ADR-0002) and the AUR0006
determinism analyzer (ADR-0006) — the split's cost, core velocity serialized
behind a single author, exceeds its remaining benefit.

## Decision

The authorship boundary is retired; ADR-0003 is superseded. One standard
applies to all code, core included:

1. Design decisions land as accepted ADRs before the code that depends on them.
2. Changes to `src/Aurelia.Core` merge only through pull requests reviewed and
   merged by the project owner.
3. The automated contracts — the crash-recovery test and the determinism
   analyzer — gate every merge.

## Consequences

Core development is no longer bounded by one person's spare hours. The entire
design-control burden shifts to the ADR log and PR review: if either becomes a
rubber stamp, the risk ADR-0003 guarded against returns unmanaged, and this
decision should be revisited.
