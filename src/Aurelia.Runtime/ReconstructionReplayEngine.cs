// Deterministic replay, reconstruction mode (ADR-0004): a fold over the event
// log with every external result substituted from the recording. Nothing is
// re-executed, so replay is deterministic by construction. The fold also
// verifies the log's structural invariants (ADRs 0007, 0008, 0010, 0012) and
// fails loudly on any violation — corruption must never reconstruct quietly.
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public sealed class ReconstructionReplayEngine(IEventStore store) : IReplayEngine
{
    public async Task<SessionResult> ReplayAsync(string sessionId, CancellationToken ct = default)
    {
        var log = await store.ReadAsync(sessionId, ct);
        if (log.Count == 0)
            throw new EventLogIntegrityException(sessionId, "event log is empty");

        long lastSequence = 0;
        SessionStarted? started = null;
        SessionCompleted? completed = null;
        SessionFailed? failed = null;
        var intents = new Dictionary<string, CallRequested>();
        var terminalOutcomes = new Dictionary<string, CallOutcome>();

        foreach (var evt in log)
        {
            if (evt.Sequence <= lastSequence)
                throw new EventLogIntegrityException(sessionId,
                    $"sequence not strictly increasing at {evt.Sequence} ({evt.GetType().Name})");
            lastSequence = evt.Sequence;

            switch (evt)
            {
                case SessionStarted s:
                    if (started is not null)
                        throw new EventLogIntegrityException(sessionId, "duplicate SessionStarted");
                    started = s;
                    break;

                case CallRequested request:
                    // ADR-0015: a compensation must reference a recorded intent.
                    if (request is CompensationRequested comp && !intents.ContainsKey(comp.CompensatesKey))
                        throw new EventLogIntegrityException(sessionId,
                            $"compensation references unknown key '{comp.CompensatesKey}'");
                    if (!intents.TryAdd(request.IdempotencyKey, request))
                        throw new EventLogIntegrityException(sessionId,
                            $"duplicate intent for key '{request.IdempotencyKey}'");
                    break;

                case CallOutcome outcome:
                    if (!intents.ContainsKey(outcome.IdempotencyKey))
                        throw new EventLogIntegrityException(sessionId,
                            $"outcome without intent for key '{outcome.IdempotencyKey}'");
                    // CallAttemptFailed is the only non-terminal outcome (ADR-0010).
                    if (evt is not CallAttemptFailed
                        && !terminalOutcomes.TryAdd(outcome.IdempotencyKey, outcome))
                        throw new EventLogIntegrityException(sessionId,
                            $"second terminal outcome for key '{outcome.IdempotencyKey}'");
                    break;

                case SessionCompleted c:
                    if (completed is not null)
                        throw new EventLogIntegrityException(sessionId, "duplicate SessionCompleted");
                    completed = c;
                    break;

                case SessionFailed f:
                    if (failed is not null)
                        throw new EventLogIntegrityException(sessionId, "duplicate SessionFailed");
                    failed = f;
                    break;
            }
        }

        if (started is null)
            throw new EventLogIntegrityException(sessionId, "no SessionStarted event");
        // ADR-0017: exactly one terminal state.
        if (completed is not null && failed is not null)
            throw new EventLogIntegrityException(sessionId,
                "both SessionCompleted and SessionFailed present");
        if (failed is not null)
            return new SessionResult(sessionId, SessionStates.TerminalJson(log, failed));
        if (completed is not null)
            return new SessionResult(sessionId, completed.FinalStateJson);

        // ADR-0031: an open conversation reconstructs to its last completed
        // turn — turns are deterministic units, so the cut is exact.
        var lastTurn = log.OfType<AgentMessageSent>().OrderBy(e => e.Sequence).LastOrDefault();
        if (lastTurn is not null)
            return new SessionResult(sessionId, JsonSerializer.Serialize(new
            {
                status = "open",
                asOfTurn = lastTurn.TurnOrdinal,
                reply = lastTurn.Content,
            }));
        throw new SessionNotCompletedException(sessionId);
    }
}

public sealed class EventLogIntegrityException(string sessionId, string violation)
    : Exception($"Event log for session '{sessionId}' violates an invariant: {violation}.");

public sealed class SessionNotCompletedException(string sessionId)
    : Exception($"Session '{sessionId}' has no SessionCompleted event; reconstruction of "
        + "in-flight sessions is not supported yet.");
