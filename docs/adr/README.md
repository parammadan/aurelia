# Architecture Decision Records

Every core design decision lands here before the code changes. Decisions are
made in design conversation, recorded by Param, and never edited after
acceptance — a reversed decision gets a new ADR that supersedes the old one.

| ADR | Title | Status |
|-----|-------|--------|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-crash-recovery-harness-is-the-ci-contract.md) | The crash-recovery harness is the CI contract | Accepted |
| [0003](0003-core-perimeter-authorship-boundary.md) | Core/perimeter authorship boundary | Accepted |
| [0004](0004-replay-is-reconstruction-first.md) | Replay is reconstruction first, re-execution later | Accepted |
| [0005](0005-results-match-by-key-checked-against-position.md) | Recorded results match by deterministic key, checked against position | Accepted |
| [0006](0006-core-may-not-read-time-or-randomness.md) | Core code may not read time or randomness directly | Accepted |

New entries: copy [template.md](template.md), number sequentially, add to this
table in the same commit.
