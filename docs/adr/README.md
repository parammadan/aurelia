# Architecture Decision Records

Every core design decision lands here before the code changes. Decisions are
made in design conversation, recorded by Param, and never edited after
acceptance — a reversed decision gets a new ADR that supersedes the old one.

| ADR | Title | Status |
|-----|-------|--------|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-crash-recovery-harness-is-the-ci-contract.md) | The crash-recovery harness is the CI contract | Accepted |
| [0003](0003-core-perimeter-authorship-boundary.md) | Core/perimeter authorship boundary | Superseded by 0014 |
| [0004](0004-replay-is-reconstruction-first.md) | Replay is reconstruction first, re-execution later | Accepted |
| [0005](0005-results-match-by-key-checked-against-position.md) | Recorded results match by deterministic key, checked against position | Accepted |
| [0006](0006-core-may-not-read-time-or-randomness.md) | Core code may not read time or randomness directly | Accepted |
| [0007](0007-flat-log-with-turn-step-attributes.md) | Flat log with turn/step attributes | Accepted |
| [0008](0008-external-calls-are-two-phase-events.md) | External calls are two-phase: intent then outcome | Accepted |
| [0009](0009-per-event-schema-version-with-upcasters.md) | Per-event schema version with upcasters | Accepted |
| [0010](0010-keys-identify-logical-calls-attempts-numbered.md) | Keys identify logical calls; attempts are numbered | Accepted |
| [0011](0011-approval-is-a-slow-external-call.md) | Human approval is a slow external call | Accepted |
| [0012](0012-outcomes-inherit-intent-coordinates.md) | Outcome events inherit their intent's turn/step coordinates | Accepted |
| [0013](0013-approvals-carry-policy-and-expiry.md) | Approval requests carry policy and optional expiry | Accepted |
| [0014](0014-retire-authorship-boundary.md) | Retire the core/perimeter authorship boundary | Accepted |
| [0015](0015-compensation-is-an-external-call.md) | Compensation is an external call in the log | Accepted |
| [0016](0016-tools-opt-into-compensation.md) | Tools opt into compensation via ICompensatableTool | Accepted |
| [0017](0017-failed-compensation-parks-the-session.md) | Failed compensation parks the session as SessionFailed | Accepted |
| [0018](0018-store-enforces-sequence-contiguity.md) | The store enforces sequence contiguity | Accepted |
| [0019](0019-cosmos-event-store.md) | Cosmos DB event store: serverless, head-document concurrency, Entra-only | Accepted |
| [0020](0020-routing-decision-recorded-on-intent.md) | The routing decision is recorded on the model intent | Accepted |
| [0021](0021-route-classes-and-policy-map.md) | Routing is route classes resolved by a policy map | Accepted |
| [0022](0022-latency-budget-breach-falls-back.md) | A latency budget breach is an attempt failure that falls back | Accepted |
| [0023](0023-subagents-are-child-sessions.md) | Sub-agents are child sessions behind a two-phase call | Accepted |
| [0024](0024-child-failure-is-terminal-for-the-call.md) | A failed child is immediately terminal for the parent's call | Accepted |
| [0025](0025-explicit-handoff-only.md) | Parent and child share state by explicit handoff only | Accepted |
| [0026](0026-queue-delivery-maps-to-session-identity.md) | Queue delivery maps to deterministic session identity | Accepted |
| [0027](0027-retry-policy.md) | Retry policy: declared attempts with exponential backoff | Accepted |
| [0028](0028-conversations-enter-via-handleturn.md) | Conversations enter via HandleTurnAsync, alongside single-shot | Accepted |
| [0029](0029-steps-receive-turncontext.md) | Steps receive an explicit TurnContext | Accepted |
| [0030](0030-memory-tiers-hot-window-and-summaries.md) | Memory tiers: hot window plus durable summary checkpoints | Accepted |
| [0031](0031-replay-open-conversations-to-last-turn.md) | Replay reconstructs open conversations to the last completed turn | Accepted |
| [0032](0032-sessions-decouple-from-channels.md) | Sessions decouple from channels | Accepted |
| [0033](0033-token-usage-recorded-on-model-outcomes.md) | Token usage is recorded on model outcomes (schema v3) | Accepted |
| [0034](0034-eval-suite-is-a-ci-gate.md) | The eval suite is a CI gate | Accepted |
| [0035](0035-seeded-chaos-injection.md) | Chaos injection is seeded, parseable, and wraps at the perimeter | Accepted |
| [0036](0036-mcp-server-surface.md) | The MCP server is a stateless channel over durable sessions | Accepted |
| [0037](0037-telemetry-is-observability-only.md) | Telemetry is OpenTelemetry, and it is observability-only | Accepted |
| [0038](0038-playground-and-trace-viewer.md) | The playground demos real processes; the trace viewer is a log projection | Accepted |
| [0039](0039-semantic-kernel-interop.md) | Semantic Kernel interop goes both directions | Accepted |
| [0040](0040-benchmark-harness-in-repo.md) | The benchmark harness lives in-repo behind a neutral adapter contract | Accepted |
| [0041](0041-plain-loop-floor-baseline.md) | The plain loop is the benchmark's floor baseline | Accepted |
| [0042](0042-session-failed-reports-unrecoverable-effects.md) | SessionFailed reports the effects it could not undo | Accepted |
| [0043](0043-cancellation-is-a-durable-signal.md) | Cancellation is a durable signal honoured at safe points | Accepted |
| [0044](0044-proposals-and-policy-denials-are-recorded.md) | A refused call is recorded as a proposal and a denial, never as an intent | Accepted |
| [0045](0045-forks-record-their-lineage.md) | A fork records its lineage in its own log | Accepted |

New entries: copy [template.md](template.md), number sequentially, add to this
table in the same commit.
