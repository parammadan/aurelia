# 0016 — Tools opt into compensation via ICompensatableTool

- **Status:** Accepted
- **Date:** 2026-08-01
- **Author:** Param Madan

## Context

A refund tool knows how to reverse a refund; an order-lookup tool has nothing
to reverse. That asymmetry can live in a fatter `ITool` where every tool
answers a side-effect classification, in an orchestration-level saga map
(tool → compensating tool), or in an opt-in interface. The saga map separates
the reversal from the effect it reverses and rots silently when a tool's
semantics change; the fat interface taxes the many tools that are trivially
read-only.

## Decision

Compensability is opt-in: `ICompensatableTool : ITool` adds
`CompensateAsync(originalArguments, originalResult, compensationKey)`. The
runtime discovers compensability by type — during an unwind, succeeded calls
whose tool implements the interface are compensated; the rest are skipped.
The compensating logic lives in the same class as the effect it reverses.

## Consequences

Read-only tools stay one method. A side-effectful tool that *forgets* to
implement the interface is silently non-compensatable — this is the decision's
known risk, accepted for now and revisited if it bites (a lint or registration
check is the likely counter).
