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

New entries: copy [template.md](template.md), number sequentially, add to this
table in the same commit.
