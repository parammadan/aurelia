# Aurelia.Core

The core of the runtime: the event schema, tool contract, and the execution
and replay interfaces.

Operating rule (ADR-0001, ADR-0014): nothing lands here without an accepted
ADR in [`docs/adr`](../../docs/adr/README.md), and changes merge only through
reviewed pull requests. The automated contracts — the crash-recovery test and
the AUR0006 determinism analyzer — gate every merge.

Current state: the event model implements ADRs 0004–0013. The execution model
(`ISessionRuntime`) and replay engine (`IReplayEngine`) are interface sketches
pending their own design rounds.
