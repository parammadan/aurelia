# The Orchestration Reliability Benchmark — methodology (v1 draft)

> **⚠️ RESULTS WITHDRAWN (2026-08-02).** All published results
> (experiments 003–005) are withdrawn: a Temporal adapter configuration
> (activity StartToClose timeout) was changed mid-measurement, and no run
> configuration was pre-registered before measuring. A rerun requires a
> pre-registered configuration approved by the project owner first. The
> methodology below stands; the numbers do not.
>
> Status: methodology draft, published before any cross-framework results
> exist — deliberately. The rules must be fixed and criticizable before the
> first scoreboard, or the scoreboard is marketing.

## 1. What this benchmark measures

Agent frameworks are benchmarked today on task success under ideal
conditions. Nobody publishes what happens when things fail — and production
is made of failure: tools time out, models emit garbage, processes die
mid-side-effect. This benchmark scores **orchestration reliability**: what an
agent framework does when the world misbehaves.

Two axes, measured separately, because they are different virtues:

- **Recovery Rate (RR):** the fraction of runs that complete the task
  *correctly* despite injected faults. "The task got done anyway."
- **Correctness After Failure (CAF):** the fraction of runs — including runs
  that did not complete — that violated **zero side-effect invariants**.
  "Whatever happened, nothing was double-charged, lost, or silently stuck."

A framework can have low RR and perfect CAF (gives up cleanly) or high RR and
poor CAF (powers through by re-executing side effects). Both numbers are
reported; neither is averaged away.

## 2. The canonical task

One task, specified abstractly so any framework can implement it idiomatically:
**the service-credit case** (a Dynamics 365 Contact Center-style scenario).

1. `lookup_case(text) → {caseId, customer, entitledCreditUsd}` — read-only.
2. `issue_service_credit(case) → {creditId}` — **side-effectful**: appends a
   credit to an external ledger. Accepts an idempotency key. Reversible via a
   compensating `reverse_credit`.
3. `notify_customer(credit) → {notified}` — side-effectful, non-compensatable.

The **ledger is the ground truth**. It is owned by the harness, not the
framework under test.

### Side-effect invariants (the CAF checklist, per run)

- **I1 — exactly-once credit:** the ledger contains at most one credit for the
  case, regardless of crashes and retries.
- **I2 — no orphaned credit:** if the task did not complete, the ledger shows
  either no credit, or credit + reversal, or the framework surfaced an
  explicit, machine-readable "leaked effect" report. Silent leakage fails I2.
- **I3 — terminal legibility:** within the time limit, the run reaches a state
  the harness can classify as completed / failed / awaiting-input by querying
  the framework's own state surface. "Hung with no queryable state" fails I3.
- **I4 — no fabrication:** a run that reports success while the ledger lacks
  the credit fails I4 (worse than failing honestly; weighted accordingly in
  the report narrative, though CAF itself is binary per run).

## 3. Fault taxonomy

Faults are injected **at the harness boundary** (the tools and the model
endpoint the harness provides), identically for every framework. Fault
conditions, each run separately:

| # | Fault | Injection point |
|---|---|---|
| F0 | none (baseline) | — |
| F1 | transient tool error (rate *r*) | tool throws, then recovers |
| F2 | persistent tool outage | tool throws for a fixed window |
| F3 | tool latency spike | tool sleeps past typical timeouts |
| F4 | malformed model output (rate *r*) | endpoint returns non-JSON garbage |
| F5 | model endpoint error / timeout | endpoint 5xx or stalls |
| F6 | **SIGKILL mid-tool-call** | harness kills the framework's worker process while the credit call is provably in flight |
| F7 | SIGKILL mid-model-call | as F6, during the model call |
| F8 | SIGKILL mid-compensation | as F6, during `reverse_credit` |
| F9 | duplicate task delivery | the same task id is submitted twice, concurrently |

F6–F8 require the framework to be run in whatever durable/checkpointed
configuration its documentation recommends for production; the configuration
used is published verbatim.

## 4. Protocol

- **N ≥ 100 runs per framework per fault condition**, seeded fault injection,
  seeds published. (The in-repo prototype used N=20; the benchmark proper
  does not.)
- **Model calls are served by a harness-controlled deterministic endpoint**
  (recorded fixtures) so no framework wins or loses on model luck. The task's
  intelligence requirement is deliberately trivial — this benchmark measures
  orchestration, not models.
- **Pinned versions** of every framework, every dependency, and the harness;
  one machine class; configs, raw per-run logs, and the harness itself
  published.
- **Idiomatic implementations:** each framework's task implementation follows
  its official durable-execution documentation. Before results are published,
  each implementation is offered to the framework's maintainers for review
  with a public issue; their suggested corrections are applied or their
  objection is published alongside the results. Disputes get a rerun.
- **No cherry-picking:** every condition run is reported. Conditions a
  framework cannot express (e.g., no compensation concept) are reported as
  "not expressible", not as zero.

## 5. Candidate frameworks (initial)

| Framework | Why |
|---|---|
| plain OpenAI SDK loop | the floor: what most production agents actually are |
| LangGraph (with its checkpointer) | the most popular agent framework |
| Temporal (workflow + activities) | the durable-execution gold standard, as an upper reference |
| Microsoft Agent Framework / Semantic Kernel | the ecosystem this project lives in |
| Aurelia | the home team — held to the same rules, reviewed hardest |

## 6. What this benchmark does NOT measure

Model quality, prompt engineering, token efficiency, throughput/scale,
multi-tenancy, developer ergonomics, or security. A framework that loses here
may be excellent at all of those.

## 7. Scoring and reporting

Per framework, per fault condition: RR, CAF, median/p95 time-to-terminal, and
cost overhead (extra tool/model invocations vs. baseline). Presented as flat
tables plus the raw CSV; no composite "reliability score" — composites hide
exactly the trade-offs this benchmark exists to expose.

## 8. Relationship to the Aurelia repo, and status

Status as of 2026-08-02 — much of the roadmap below now exists in this repo:
the adapter-based harness with neutral scoring (`src/Aurelia.Benchmark`,
[harness.md](harness.md)), the **harness-owned HTTP tool server** making
fault injection physical (`src/Aurelia.ToolServer`), and N=100 results for
seven contestants: Aurelia, two floor baselines, and two key-discipline
variants each of **LangGraph** and **Temporal**
(`docs/experiments/004…005`, raw data committed). Still owed before
anything public: extraction into a framework-neutral repo (Aurelia must be a
*contestant*, not the referee's employer) and **maintainer review of each
framework implementation** — the LangGraph and Temporal task code here is
ours and unreviewed by their teams. Original roadmap:

1. ~~Freeze this methodology~~ (drafted; criticism still invited).
2. ~~Extract the neutral harness~~ (adapter contract + HTTP tool boundary done, in-repo).
3. ~~Floor baseline + Aurelia pilot with raw data~~ (experiments 003–004).
4. Add frameworks one at a time, maintainer review each — LangGraph and
   Temporal implemented and measured; *review outreach pending disclosure*.

Criticism of this methodology is the most useful contribution this project
can receive right now — especially from people who maintain the frameworks
it will measure.
