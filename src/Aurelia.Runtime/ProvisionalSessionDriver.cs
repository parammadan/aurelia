// A hard-coded one-tool session script whose only virtue is resumability from
// the event log. It exists so the crash-recovery harness (ADR-0002) has
// something real to kill, and it is deleted the day the real execution model
// lands in Core.
using Aurelia.Core;

namespace Aurelia.Runtime;

public sealed class ProvisionalSessionDriver(IEventStore store, ITool tool) : ISessionRuntime
{
    public async Task<SessionResult> RunToCompletionAsync(string sessionId, string goal, CancellationToken ct = default)
    {
        using var sessionSpan = AureliaDiagnostics.Source.StartActivity("session.run");
        sessionSpan?.SetTag("aurelia.session.id", sessionId);

        var log = await store.ReadAsync(sessionId, ct);
        sessionSpan?.SetTag("aurelia.session.resumed", log.Count > 0);
        var seq = log.Count == 0 ? 0L : log[^1].Sequence;

        if (log.OfType<SessionCompleted>().FirstOrDefault() is { } alreadyDone)
            return new SessionResult(sessionId, alreadyDone.FinalStateJson);

        if (log.OfType<SessionStarted>().FirstOrDefault() is { } started)
        {
            goal = started.Goal; // resumed session: the recorded goal wins
        }
        else
        {
            await store.AppendAsync(new SessionStarted
            {
                SessionId = sessionId,
                Sequence = ++seq,
                TurnOrdinal = 0,
                StepOrdinal = 0,
                RecordedAt = DateTimeOffset.UtcNow,
                Goal = goal,
            }, ct);
        }

        // The script's single external call: step 0 of turn 0.
        var request = log.OfType<ToolCallRequested>().FirstOrDefault();
        if (request is null)
        {
            request = new ToolCallRequested
            {
                SessionId = sessionId,
                Sequence = ++seq,
                TurnOrdinal = 0,
                StepOrdinal = 0,
                RecordedAt = DateTimeOffset.UtcNow,
                ToolName = tool.Name,
                ArgumentsJson = goal,
            };
            await store.AppendAsync(request, ct);
        }

        var success = log.OfType<ToolCallSucceeded>()
            .FirstOrDefault(o => o.IdempotencyKey == request.IdempotencyKey);
        if (success is null)
        {
            // Crash recovery lands here: the intent is durable but no terminal
            // outcome is, so the call re-executes under the same key (ADR-0010).
            var attempt = log.OfType<CallAttemptFailed>()
                .Count(f => f.IdempotencyKey == request.IdempotencyKey) + 1;

            using var toolSpan = AureliaDiagnostics.Source.StartActivity("execute_tool");
            toolSpan?.SetTag("gen_ai.operation.name", "execute_tool");
            toolSpan?.SetTag("gen_ai.tool.name", request.ToolName);
            toolSpan?.SetTag("gen_ai.tool.call.id", request.IdempotencyKey);
            var resultJson = await tool.ExecuteAsync(request.ArgumentsJson, request.IdempotencyKey, ct);

            success = new ToolCallSucceeded
            {
                SessionId = sessionId,
                Sequence = ++seq,
                // ADR-0012: the outcome inherits its intent's coordinates.
                TurnOrdinal = request.TurnOrdinal,
                StepOrdinal = request.StepOrdinal,
                RecordedAt = DateTimeOffset.UtcNow,
                Attempt = attempt,
                ResultJson = resultJson,
            };
            await store.AppendAsync(success, ct);
        }

        var done = new SessionCompleted
        {
            SessionId = sessionId,
            Sequence = ++seq,
            TurnOrdinal = 0,
            StepOrdinal = 1,
            RecordedAt = DateTimeOffset.UtcNow,
            FinalStateJson = success.ResultJson,
        };
        await store.AppendAsync(done, ct);
        return new SessionResult(sessionId, done.FinalStateJson);
    }
}
