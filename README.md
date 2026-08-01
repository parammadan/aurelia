# Aurelia

**A durable agent orchestration runtime.** Checkpointed, replayable, observable
agent execution — an agent session that survives the death of the process
running it.

> Temporal for agents, built on Azure, that can lose a pod mid-refund and
> finish the refund anyway.

## Status

Phase 1 (durable execution core), day one. What exists today:

- A solution scaffold: core contracts, an Orleans-hosted runtime, and a
  crash-recovery harness.
- **The contract, enforced in CI from the first commit:** a test that SIGKILLs
  the host mid-tool-call, restarts it, and asserts the session resumes from
  the event log and completes with the correct final state
  ([ADR-0002](docs/adr/0002-crash-recovery-harness-is-the-ci-contract.md)).
  It currently passes against a provisional, deliberately disposable session
  driver; the handwritten execution model replaces it from the inside without
  the contract ever loosening.

## Layout

| Path | What it is |
|------|------------|
| `src/Aurelia.Core` | The handwritten zone: execution model, event schema, tool contract, replay semantics. Interfaces and stubs today. See [ADR-0003](docs/adr/0003-core-perimeter-authorship-boundary.md). |
| `src/Aurelia.Runtime` | Perimeter: Orleans host, provisional session driver, file-backed event store. |
| `tests/Aurelia.CrashTests` | The kill/resume harness — the CI contract. |
| `docs/adr` | Architecture Decision Records. Every core design decision lands here before code. |

## Run the contract

```bash
dotnet test
```

This builds everything and runs the crash test for real: it spawns the runtime
host, kills it with SIGKILL mid-tool-call, and verifies recovery.
