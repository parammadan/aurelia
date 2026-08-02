# 0041 — The plain loop is the benchmark's floor baseline

- **Status:** Proposed
- **Date:** 2026-08-02
- **Author:** Param Madan

> Backfill: `PlainLoopAgent` shipped 2026-08-01 with no ADR (audit
> 2026-08-02 §1). This records the as-built decision for review.

## Context

A reliability benchmark needs a floor: what the typical
tool-loop-in-a-script agent — no store, no keys, no compensation — actually
does under faults. Without a floor, framework scores have no scale.

## Decision

`PlainLoopAgent` (`--agent plain`) is a deliberately naive in-memory loop
over the same tools: fresh random idempotency keys per attempt, no event
log, no recovery beyond either resubmitting the whole task (`plain-rerun`)
or giving up (`plain-noretry`). Its naivety is the point and is
load-bearing: it must mirror the dominant published pattern, not a strawman
— so it may not be "improved" without a corresponding benchmark-methodology
note, and it must never gain durability features.

## Consequences

Benchmark tables get a meaningful zero. The risk is reading the floor as a
competitor — docs must always label it as the floor. It doubles as a
regression canary: if the floor ever scores as well as Aurelia on
kill-after-effect, the harness is broken, not the floor fixed.

## Verified by

Benchmark runs across F0/F1/F6 (results currently withdrawn; the *behavioral
signature* — double credits on resubmit, silent leaks on give-up — is also
reproduced at n=3 smoke scale).
