// Builds the trace-viewer model from a session's event log: paired
// intent/outcome calls with timeline offsets, plus the full raw events.
// Everything here is a pure projection of the log — the same data replay
// folds over (ADR-0004).
using Aurelia.Core;
using Aurelia.Runtime;

namespace Aurelia.Playground;

public static class TraceBuilder
{
    public static object Build(string sessionId, IReadOnlyList<SessionEvent> events)
    {
        var t0 = events[0].RecordedAt;
        var tEnd = events[^1].RecordedAt;
        double Ms(DateTimeOffset t) => (t - t0).TotalMilliseconds;

        var (status, _) = SessionStates.Derive(events);

        var calls = events.OfType<CallRequested>().Select(intent =>
        {
            var outcomes = events.OfType<CallOutcome>()
                .Where(o => o.IdempotencyKey == intent.IdempotencyKey)
                .OrderBy(o => o.Sequence)
                .ToList();
            var terminal = outcomes.LastOrDefault(o => o is not CallAttemptFailed);

            var (kind, name) = intent switch
            {
                AgentCallRequested a => ("agent", $"{a.AgentName} → {a.ChildSessionId}"),
                ModelCallRequested m => ("model", m.ModelId),
                CompensationRequested c => ("compensation", $"{c.ToolName} reverses {c.CompensatesKey}"),
                ToolCallRequested t => ("tool", t.ToolName),
                ApprovalRequested => ("approval", "human approval"),
                TimeRequested => ("time", "injected time"),
                RandomRequested => ("random", "injected random"),
                _ => ("call", intent.GetType().Name),
            };

            return new
            {
                key = intent.IdempotencyKey,
                kind,
                name,
                childSessionId = (intent as AgentCallRequested)?.ChildSessionId,
                startMs = Ms(intent.RecordedAt),
                endMs = terminal is null ? (double?)null : Ms(terminal.RecordedAt),
                attempts = outcomes.Count == 0 ? 1 : outcomes.Max(o => o.Attempt),
                status = terminal switch
                {
                    null => "pending",
                    CallFailed => "failed",
                    ApprovalResolved { Approved: false } => "denied",
                    _ => "succeeded",
                },
                intent = new { type = intent.GetType().Name, data = EventSerialization.Payload(intent) },
                outcomes = outcomes.Select(o => new
                {
                    type = o.GetType().Name,
                    atMs = Ms(o.RecordedAt),
                    data = EventSerialization.Payload(o),
                }).ToArray(),
            };
        }).ToArray();

        return new
        {
            sessionId,
            status,
            goal = events.OfType<SessionStarted>().FirstOrDefault()?.Goal,
            startedAt = t0,
            totalMs = Math.Max(1, (tEnd - t0).TotalMilliseconds),
            calls,
            events = events.Select(e => new
            {
                seq = e.Sequence,
                type = e.GetType().Name,
                atMs = Ms(e.RecordedAt),
                data = EventSerialization.Payload(e),
            }).ToArray(),
        };
    }
}
