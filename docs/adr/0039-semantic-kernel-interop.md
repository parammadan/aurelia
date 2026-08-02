# 0039 — Semantic Kernel interop goes both directions

- **Status:** Accepted
- **Date:** 2026-08-02
- **Author:** Param Madan

> Backfill: SK interop shipped 2026-08-02 with no ADR (audit 2026-08-02 §1).
> This records the as-built decision for review.

## Context

Aurelia complements the Microsoft agent stack; it must meet Semantic Kernel
(and the Agent Framework built on it) where it is, in both roles: consuming
SK's function ecosystem, and offering durable agents to SK hosts.

## Decision

`src/Aurelia.SemanticKernel` provides two adapters and no new concepts:

- **SK function as durable tool:** `KernelFunction.AsAureliaTool(kernel)`
  wraps any kernel function as an `ITool`, so its invocations become standard
  two-phase events — recorded, replayed by substitution (a rerun does *not*
  re-invoke the function), and crash-safe like any tool.
- **Aurelia agent as SK function:** `ISessionRuntime.AsKernelFunction(name,
  description)` exposes an agent to any SK/Agent Framework host; the durable
  session outlives the caller (session id in arguments, auto `sk-{guid}`
  when omitted).

## Consequences

SK plugins gain durability without modification; SK hosts gain
crash-surviving agents behind a plain function call. Compensation is not
mapped (a wrapped kernel function is never `ICompensatableTool` — wrapping
must not invent reversibility, same principle as the chaos wrappers).
Streaming and SK filters/planners are out of scope until a real consumer
demands them.

## Verified by

`SemanticKernelInteropTests`: two-phase events + substitution-not-reinvocation
for wrapped functions; durable session via `KernelFunction` invocation.
