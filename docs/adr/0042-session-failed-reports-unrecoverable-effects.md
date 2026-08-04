# 0042 — SessionFailed reports the effects it could not undo

- **Status:** Accepted
- **Date:** 2026-08-04
- **Author:** Param Madan

## Context

The saga unwind (ADR-0015) reverses succeeded calls whose tools opt into
compensation (ADR-0016). Its terminal report lists what it compensated and
what it tried and failed to compensate. But it selects candidates like this:

```csharp
toCompensate = succeeded calls WHERE tools[intent.ToolName] is ICompensatableTool
```

A succeeded effect from a **non**-compensatable tool is filtered out before
that split, so it appears in neither `CompensatedKeys` nor
`UncompensatedKeys` — it is simply absent from the terminal event. For an
effect that genuinely cannot be reversed (a notification already sent), the
absence reads as "nothing happened", which is the opposite of the truth. A
downstream consumer (CaseOps) had to derive the list itself to tell an
operator what was left standing.

This is the first change under the owner's scoped freeze lift.

## Decision

`SessionFailed` gains a third list, `UnrecoverableKeys` (schema v2): the
idempotency keys of calls that **succeeded** and whose tool does not
implement `ICompensatableTool`. The unwind now partitions every succeeded
call three ways rather than filtering one class away:

- **compensated** — reversed successfully,
- **uncompensated** — compensation was attempted and did not succeed,
- **unrecoverable** — no compensation was ever possible.

The distinction matters operationally: "we tried and failed" and "this can
never be undone" call for different human responses, and conflating them
(or hiding the second) is how ledgers drift from reality.

Old logs read through the upcaster acquire v2 with an empty list — absent is
deserialized as empty, which is exactly right for a session that recorded no
such effects (ADR-0009).

## Consequences

A failed session's terminal event now describes the full residue of the
attempt, and CaseOps' derived workaround (`UnrecoverableEffects`) retires in
favour of the runtime's own report. Tool authors gain a real signal: if an
effect shows up here and shouldn't, the tool should implement
`ICompensatableTool`. The cost is one more required field on a core event —
paid once, through the standard upcaster path.

## Verified by

`tests/Aurelia.CrashTests/CaseResolutionTests.cs` (a non-compensatable
notification survives an unwind and is reported), `UpcasterTests` (v1 logs
read as v2 with an empty list).
