# 0044 — A refused call is recorded as a proposal and a denial, never as an intent

- **Status:** Accepted
- **Date:** 2026-08-04
- **Author:** Param Madan

## Context

A model can be talked into asking for something outrageous — a $50,000
credit, an approval bypass. The deterministic boundary must refuse it, and
the refusal must be *provable* afterwards: an auditor needs to see both that
the thing was asked for and that no call was ever issued.

Recording a denial as a failed `ToolCallRequested` would be actively wrong.
An intent means "this call is about to execute, and if you find no outcome
you must assume it may have"; that is the contract recovery is built on
(ADR-0008). A refused call never approached execution, and must never be
confused with one that might have.

Third change under the owner's scoped freeze lift.

## Decision

Two new events, neither of which is a `CallRequested`:

- **`ToolCallProposed`** (tool, arguments, proposedBy) — what was asked for.
  It carries **no idempotency key**, because it is not a call: nothing
  downstream may deduplicate against it or retry it.
- **`PolicyDenied`** (tool, reason, deniedBy) — the boundary's refusal.

When a policy refuses a step, the runtime appends the proposal and the
denial, and **no `ToolCallRequested` is ever written**. The proof is
therefore an *absence*: a trace can show the proposal, the denial, and the
gap where an intent would be. Absence is a stronger claim than a failure
record, because there is nothing to misread.

The session then parks in Aurelia's existing sense (ADR-0017): the saga
unwinds whatever already committed and the terminal event is
`SessionFailed`, reason `policy denied: …`. Effects that came before the
refusal are reversed or named (ADR-0042); nothing is left dangling.

The policy itself is a **pure predicate supplied by the host**
(`ToolCallPolicy`: tool + arguments → deny reason or null). It is
deterministic, it sees no clock and no randomness, and it runs *before* the
intent — so the boundary cannot be moved by anything the model says.

## Consequences

"The model asked; the boundary refused; nothing executed" becomes a
three-part fact in the log rather than a claim in a slide. Auditors get the
attempt as well as the refusal — a denial that leaves no trace of what was
attempted is a weaker audit trail, not a stronger one. The cost: hosts that
want a policy must supply one, and a policy that throws is a host bug, not a
runtime one.

## Verified by

`tests/Aurelia.CrashTests/PolicyDenialTests.cs` — an adversarial amount is
proposed and denied; the log holds `ToolCallProposed` + `PolicyDenied` and
**no** `ToolCallRequested` for that tool; the ledger is untouched; earlier
effects are unwound; replay reproduces the terminal state.
