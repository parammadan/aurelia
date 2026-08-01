// PERIMETER — AI-generated scaffolding (ADR-0003). A hard-coded one-tool
// session script whose only virtue is resumability from the event log. It
// exists so the crash-recovery harness (ADR-0002) has something real to kill,
// and it is deleted the day the handwritten execution model lands in Core.
using Aurelia.Core;

namespace Aurelia.Runtime;

public sealed class ProvisionalSessionDriver(IEventStore store, ITool tool) : ISessionRuntime
{
    public async Task<SessionResult> RunToCompletionAsync(string sessionId, string goal, CancellationToken ct = default)
    {
        var log = await store.ReadAsync(sessionId, ct);
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
                RecordedAt = DateTimeOffset.UtcNow,
                Goal = goal,
            }, ct);
        }

        var request = log.OfType<ToolCallRequested>().FirstOrDefault();
        if (request is null)
        {
            request = new ToolCallRequested
            {
                SessionId = sessionId,
                Sequence = ++seq,
                RecordedAt = DateTimeOffset.UtcNow,
                ToolName = tool.Name,
                ArgumentsJson = goal,
                IdempotencyKey = $"{sessionId}/tool/1",
            };
            await store.AppendAsync(request, ct);
        }

        var completion = log.OfType<ToolCallCompleted>()
            .FirstOrDefault(c => c.IdempotencyKey == request.IdempotencyKey);
        if (completion is null)
        {
            // Crash recovery lands here: the request is durable but no result
            // is, so the call re-executes under the same idempotency key.
            var resultJson = await tool.ExecuteAsync(request.ArgumentsJson, request.IdempotencyKey, ct);
            completion = new ToolCallCompleted
            {
                SessionId = sessionId,
                Sequence = ++seq,
                RecordedAt = DateTimeOffset.UtcNow,
                IdempotencyKey = request.IdempotencyKey,
                ResultJson = resultJson,
            };
            await store.AppendAsync(completion, ct);
        }

        var done = new SessionCompleted
        {
            SessionId = sessionId,
            Sequence = ++seq,
            RecordedAt = DateTimeOffset.UtcNow,
            FinalStateJson = completion.ResultJson,
        };
        await store.AppendAsync(done, ct);
        return new SessionResult(sessionId, done.FinalStateJson);
    }
}
