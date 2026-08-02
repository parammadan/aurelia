# Checkpointing won't save you

> **DRAFT — DO NOT PUBLISH.** The benchmark results this post cites were
> **withdrawn 2026-08-02** pending a pre-registered rerun (see
> `docs/benchmark/methodology.md`). Every number below must be replaced with
> rerun data before this goes anywhere. Also still pending: Param's rewrite,
> and maintainer review of the LangGraph/Temporal implementations.

Ask an agent-framework vendor what happens when a process dies mid-task and
you'll hear the same word: *checkpointing*. State is persisted, execution
resumes, nothing is lost. It's true, and it's dangerously incomplete —
because the failure that costs money isn't losing state. It's the side
effect that happened **twice**.

## The experiment

One task: look up a customer case, issue a service credit to a ledger,
notify the customer. One fault: kill the worker process — SIGKILL, no
cleanup — in the worst possible window: *after* the credit hits the ledger,
*before* the process learns it did. Then run each framework's documented
recovery story and check the ledger. A hundred times each, seeded,
reproducible.

We measured two things separately, because they are different virtues:
**recovery rate** (did the task finish correctly anyway?) and **correctness
after failure** (whatever happened, was anything double-charged, lost, or
silently stuck?).

## The result that generalizes

Every framework with durable state — LangGraph with its checkpointer,
Temporal with its event history, our own runtime — *recovered*. The
checkpoint did its job: the resumed execution knew where it was and re-ran
the interrupted step.

And that's exactly the problem. Re-running the interrupted step re-fires the
side effect. Whether the customer gets credited once or twice comes down to
one thing checkpointing doesn't provide: **the idempotency key on the retry.**

- Implementations that derive the key deterministically (same logical call →
  same key, so the downstream deduplicates) were exactly-once in **100/100**
  kill runs — LangGraph, Temporal, and our runtime alike.
- The *same* implementations with a fresh key per attempt — which is what
  most published examples do, because generating a uuid inside the tool call
  is the path of least resistance — **double-charged the customer in 100/100
  kill runs.** Same framework, same checkpointer, same recovery. One line of
  key discipline apart.

The naive baseline (an in-memory loop whose recovery is "resubmit the
task") also managed a perverse trick under ordinary transient tool failures:
its retries created duplicate charges that doing *nothing* would have
avoided. Naive retry converts availability failures into correctness
failures.

## So whose job is the key?

In every framework we measured, key discipline is the application author's
job — a convention, unenforced, invisible in review, and absent from most
examples. That's not a bug in those frameworks; it's a boundary they chose.

We chose the other boundary. In Aurelia, the idempotency key is not
something you write: it's **derived from the durable log's structure**
(session/turn/step), recorded on the call's intent event before execution,
and reused by recovery mechanically. There is no fresh-key-per-attempt
failure mode because there is no per-attempt key decision. The property
survived a hundred SIGKILLs not because our code is careful but because the
architecture doesn't offer the mistake.

There's a second boundary worth naming: **compensation**. Under transient
faults, tasks that permanently fail after the credit landed need the credit
reversed. Temporal's documented saga pattern handled this cleanly — the
reference framework earning its reputation. LangGraph has no compensation
concept, so its failed runs leaked credits silently. Our runtime compensates
via the same event-sourced machinery that does everything else, and reports
what it couldn't compensate, explicitly, in the log.

## Takeaways

1. Benchmark your agent stack under *kills*, not just prompts. Recovery rate
   and correctness-after-failure are different axes; the second is where
   frameworks quietly differ.
2. If you use LangGraph or Temporal: derive your idempotency keys from the
   workflow/thread identity, never mint them per attempt. It's one line, and
   it's the whole ballgame.
3. If you're choosing infrastructure: ask what the framework makes
   *impossible*, not what it makes possible. Exactly-once that depends on
   vigilance isn't a guarantee; it's a habit.

*Methodology, harness, adapters, and raw per-run data are in the repo. The
LangGraph and Temporal implementations follow their documented durable
patterns; corrections from their maintainers are invited and will be
published alongside the results.*
