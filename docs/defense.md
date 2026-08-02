# The defense pack — questions this design must survive

The interrogation a senior reviewer (or a Copilot Studio architect) will run,
with the short answer and where each answer lives: **decided** (ADR),
**implemented** (code), **proven** (test/experiment). If you can give the
short answer cold and re-derive the *why*, you own this codebase.

## The core five (know these in your sleep)

**1. "Temporal already exists. Why does this?"**
Temporal's replay assumes deterministic orchestration code; an agent's
orchestration logic *contains LLM calls*, nondeterministic even at
temperature 0. Aurelia records every nondeterminism source as events and
makes replay a pure fold (reconstruction), reserving re-execution for
experiments. Decided: ADR-0004. Implemented: `ReconstructionReplayEngine`.
Proven: replay-equals-live assertions in every suite.

**2. "How is 'issue refund' exactly-once when the process can die anywhere?"**
Two-phase calls: the intent (full inputs + key) is durable *before* execution;
an intent without an outcome means "may have executed." Recovery re-executes
under the **same deterministic key** (`session/turn/step` — computed from
envelope stamps, never stored), and the downstream dedupes on it. Decided:
ADR-0008, 0005, 0010. Implemented: `ScriptedAgentDriver`,
`ServiceCreditTool.AppendIfAbsent`. Proven: SIGKILL-after-effect → exactly one
ledger credit (`CrashRecoveryTests`, benchmark F6: 10/10 vs the naive loop's
10/10 double-charges — experiment 003).

**3. "What stops two processes from corrupting one session?"**
The store enforces sequence contiguity atomically (exclusive file lock /
Cosmos ETag transactional batch): a stale writer's append physically cannot
land; it fails loudly and the caller re-folds. Decided: ADR-0018 (+0019 for
the Cosmos head-document pattern). Implemented: `FileEventStore.AppendAsync`,
`CosmosEventStore`. Proven: `StoreConcurrencyTests`, live Cosmos integration
test.

**4. "How do you pause for a human for a week without a scheduler?"**
There is no pause machinery. An approval is a slow external call; "paused" is
a *derived* condition — intent without outcome. Any process, any time later,
appends the resolution and re-drives. Decided: ADR-0011–0013. Implemented:
`ApprovalGate`, `Approvals.ResolveAsync`. Proven: pause in one process,
approve+complete in another (`ApprovalTests`, and across MCP server restarts).

**5. "Why should the log be trusted after a hard kill mid-write?"**
A torn, unterminated tail line was never durable: reads tolerate it, and the
next append truncates it under the exclusive lock before writing. Decided:
ADR-0018. Implemented: `FileEventStore.ScanTail`. Proven:
`StoreConcurrencyTests.Torn_tail_is_truncated_by_the_next_append`.

## The second ring

**6. Schema evolution** — per-event integer version + chained read-path
upcasters; log never migrates. ADR-0009; `EventUpcasterRegistry`,
`StandardUpcasters` (three real bumps shipped: model events v1→2→3);
proven live against v1 Cosmos history.

**7. Time and randomness** — banned in core *by the compiler* (`AUR0006`);
decision-relevant values arrive as recorded events; `RecordedAt` is
observability-only. ADR-0006; `DeterminismAnalyzer` + its 12 tests.

**8. Model routing** — route classes → named policy map (pure lookup =
replay-safe); the *intent is the routing record*; budgets bind the primary on
every attempt, breach falls back under the same key; the outcome records who
actually served. ADRs 0020–0022; `ModelRouting`, driver; `RoutingTests`,
experiment 001 (−82% median latency at equal success, real backends).

**9. Multi-agent** — a sub-agent is a child *session* behind a two-phase call
(deterministic child id `{parent}.{step}`); failure is terminal (child already
unwound); a paused child pauses the parent for free; explicit handoff only.
ADRs 0023–0025; `SubAgentStep`; `MultiAgentTests`.

**10. Sagas** — compensation is itself a two-phase call linked by
`CompensatesKey`; unwind in reverse-success order; exhausted compensation
parks the session with explicit compensated/uncompensated lists — nothing
silent, nothing infinite. ADRs 0015–0017; driver `UnwindAsync`;
`CaseResolutionTests` (including flaky and exhausted compensation).

**11. Queues** — message id *is* session id, so at-least-once delivery becomes
exactly-once execution by construction; approval-parked sessions complete the
message. ADR-0026; `ServiceBusIngestion`; live duplicate-delivery demo.

**12. "Where's the evidence culture?"** — eval suite as a CI gate (15
scenarios, tokens summed *from the log*), seeded chaos API, three experiments
with threats-to-validity sections, benchmark methodology published before any
scoreboard, pilot table (experiment 003). The one-liner: *recovery rate and
correctness-after-failure are different axes, and the second is where
frameworks quietly differ.*

## Weaknesses to own proactively (better volunteered than discovered)

- **Multi-turn is unexercised** — everything writes `TurnOrdinal 0`; the
  conversational loop and memory tiers are undesigned (the next real round).
- **Model-swap-on-resume semantics** are undecided: recovery re-executes with
  the recorded prompt but the *currently configured* client.
- **Retry policy is v1** (ADR-0027): deterministic backoff, no jitter, and
  all-exceptions-retryable — the retryable-vs-terminal error taxonomy on the
  tool contract is deferred until a real tool demands it.
- **Ledger dedupe is O(n) per write** (demo scale only); **approval expiry**
  still awaits the time-injection machinery.
- **The benchmark referee still lives in a contestant's repo** — the harness
  is adapter-based with a physical HTTP tool boundary and N=100 is done, but
  the neutral-repo extraction and third-party maintainer-reviewed rounds are
  owed; the methodology says so out loud.
- **Process honesty**: the core was AI-drafted under direction (ADR-0014
  supersedes 0003); the defense is the ADR trail plus review — which means
  the review has to actually happen.

## A 90-minute reading order

1. `docs/adr/0004`, `0005`, `0008`, `0010`, `0018` — the five load-bearing ones (20 min)
2. `src/Aurelia.Core/Events.cs` end to end (10 min)
3. `src/Aurelia.Runtime/ScriptedAgentDriver.cs` — the engine; read it against Q2/Q4/Q10 (25 min)
4. `tests/Aurelia.CrashTests/CrashRecoveryTests.cs` + `docs/experiments/003` (15 min)
5. `FileEventStore.AppendAsync` + `CosmosEventStore.AppendAsync` side by side — same contract, two mediums (10 min)
6. Skim the remaining ADRs in order; each is under a page (10 min)
