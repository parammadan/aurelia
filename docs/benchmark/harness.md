# The benchmark harness — adding a framework adapter

The harness core (`src/Aurelia.Benchmark`: `Contracts.cs`, `HarnessRunner.cs`,
`Scoring.cs`) is framework-neutral. It owns the task, the ledger (ground
truth), fault injection, kills, recovery orchestration, and the invariant
scoring (I1–I4). A framework under test provides one `IFrameworkAdapter`:

| Member | Meaning |
|---|---|
| `StartInfo(task)` | how to launch a worker for the task, in the framework's **documented production configuration** |
| `RecoveryStartInfo(task)` | the framework's documented recovery action after its worker dies (null = no recovery story) |
| `QueryTerminalStateAsync(task)` | the framework's durable answer to "what happened?" — a `TerminalReport` with compensated/leaked effect accounting, or null if no such surface exists |
| `ClaimsSuccess(exit, stdout)` | whether the framework claims the task succeeded (checked against the ledger for the no-fabrication invariant) |

Scoring reads **only** the harness-owned ledger plus the adapter's claims and
report — never framework internals. Adapters may use their framework's
libraries freely (that's the point of them).

## Known gap before third-party adapters (be honest about this)

Fault injection currently rides environment variables understood by the
in-repo tool implementations (`AURELIA_CHAOS`, the signal/block kill
protocol). That's fine for the pilot's contestants, which share those tools —
but a LangGraph or Temporal implementation won't. **Before any third-party
round, the task's tools move behind a harness-owned HTTP boundary** (a small
tool server exposing `lookup_case` / `issue_service_credit` /
`notify_customer` + the ledger), so fault injection is physical — the server
misbehaves — rather than cooperative. The adapter contract above does not
change; only where the tools live does. This is also the extraction point for
the eventual framework-neutral repo, where Aurelia becomes one adapter among
several rather than the referee's employer.
