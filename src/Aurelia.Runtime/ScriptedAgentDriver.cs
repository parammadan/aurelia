// The scripted execution engine: runs a fixed sequence of steps with
// resumability from the event log at every point — fresh, mid-step after a
// crash, mid-retry, or mid-unwind. Implements the two-phase call protocol
// (ADR-0008), keyed retries (ADR-0010), durable model calls (ADR-0004), and
// the saga unwind (ADRs 0015-0017). LLM-planned (non-scripted) execution is
// the Phase 2 execution model; the reference agents run on this until then.
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public abstract record AgentStep;

/// <summary>One tool step: which tool to call and how to build its arguments
/// from the session goal and the results of prior steps (keyed by name).</summary>
public sealed record ScriptStep(
    string ToolName,
    Func<string, IReadOnlyDictionary<string, string>, string> BuildArguments) : AgentStep;

/// <summary>
/// One model step: builds a chat prompt from the goal and prior results. The
/// call is durable two-phase — the intent records the full prompt, model id,
/// parameters, and config version (ADR-0004); a completed call is substituted
/// from the log on resume and never re-executed.
/// </summary>
public sealed record ModelStep(
    string Name,
    Func<string, IReadOnlyDictionary<string, string>, string> BuildPromptJson) : AgentStep;

/// <summary>
/// A conditional human-approval gate (ADRs 0011-0013). The predicate is a
/// pure function over the goal and recorded results, so re-evaluation on
/// resume is deterministic. A non-null question pauses the session — no
/// terminal event, just an intent with no outcome — until an ApprovalResolved
/// outcome arrives from any process, however much later.
/// </summary>
public sealed record ApprovalGate(
    Func<string, IReadOnlyDictionary<string, string>, string?> QuestionIfRequired,
    string ApproverPolicy) : AgentStep;

public sealed class ScriptedAgentDriver(
    IEventStore store,
    IReadOnlyDictionary<string, ITool> tools,
    IReadOnlyList<AgentStep> script,
    IModelClient? modelClient = null,
    int maxAttempts = 2) : ISessionRuntime
{
    public async Task<SessionResult> RunToCompletionAsync(string sessionId, string goal, CancellationToken ct = default)
    {
        using var sessionSpan = AureliaDiagnostics.Source.StartActivity("session.run");
        sessionSpan?.SetTag("aurelia.session.id", sessionId);

        var log = (await store.ReadAsync(sessionId, ct)).ToList();
        sessionSpan?.SetTag("aurelia.session.resumed", log.Count > 0);
        var seq = log.Count == 0 ? 0L : log[^1].Sequence;

        if (log.OfType<SessionCompleted>().FirstOrDefault() is { } alreadyDone)
            return new SessionResult(sessionId, alreadyDone.FinalStateJson);
        if (log.OfType<SessionFailed>().FirstOrDefault() is { } alreadyFailed)
            return new SessionResult(sessionId, FailedStateJson(alreadyFailed));

        if (log.OfType<SessionStarted>().FirstOrDefault() is { } started)
            goal = started.Goal; // resumed session: the recorded goal wins
        else
            await Append(new SessionStarted
            {
                SessionId = sessionId, Sequence = ++seq, TurnOrdinal = 0, StepOrdinal = 0,
                RecordedAt = DateTimeOffset.UtcNow, Goal = goal,
            });

        // Resuming into a session whose script phase already failed terminally
        // goes straight to the unwind.
        var priorFailure = log.OfType<CallFailed>()
            .FirstOrDefault(f => log.OfType<CallRequested>()
                .Any(r => r is not CompensationRequested && r.IdempotencyKey == f.IdempotencyKey));
        if (priorFailure is not null)
            return await UnwindAsync(priorFailure.Error);

        // Resuming into a session whose approval was denied also unwinds.
        if (log.OfType<ApprovalResolved>().FirstOrDefault(a => !a.Approved) is { } denied)
            return await UnwindAsync($"approval denied by {denied.ApproverId}");

        // ── Script phase ─────────────────────────────────────────────────────
        var results = new Dictionary<string, string>();
        string? lastResult = null;
        for (var stepIndex = 0; stepIndex < script.Count; stepIndex++)
        {
            switch (script[stepIndex])
            {
                case ApprovalGate gate:
                {
                    var question = gate.QuestionIfRequired(goal, results);
                    if (question is null)
                        break; // gate not triggered for this session

                    var approval = log.OfType<ApprovalRequested>()
                        .FirstOrDefault(r => r.StepOrdinal == stepIndex);
                    if (approval is null)
                    {
                        approval = new ApprovalRequested
                        {
                            SessionId = sessionId, Sequence = ++seq, TurnOrdinal = 0, StepOrdinal = stepIndex,
                            RecordedAt = DateTimeOffset.UtcNow,
                            Question = question,
                            ApproverPolicy = gate.ApproverPolicy,
                        };
                        await Append(approval);
                    }

                    var resolution = log.OfType<ApprovalResolved>()
                        .FirstOrDefault(o => o.IdempotencyKey == approval.IdempotencyKey);
                    if (resolution is null)
                    {
                        // ADR-0011: "paused" is derived — an intent with no
                        // outcome. No terminal event is written; any future
                        // process picks up from exactly here.
                        sessionSpan?.SetTag("aurelia.session.awaiting_approval", true);
                        return new SessionResult(sessionId, JsonSerializer.Serialize(new
                        {
                            status = "awaiting_approval",
                            approvalKey = approval.IdempotencyKey,
                            question = approval.Question,
                            approverPolicy = approval.ApproverPolicy,
                        }));
                    }
                    if (!resolution.Approved)
                        return await UnwindAsync($"approval denied by {resolution.ApproverId}");
                    break;
                }

                case ScriptStep tool:
                {
                    var request = log.OfType<ToolCallRequested>()
                        .FirstOrDefault(r => r.StepOrdinal == stepIndex);
                    if (request is null)
                    {
                        request = new ToolCallRequested
                        {
                            SessionId = sessionId, Sequence = ++seq, TurnOrdinal = 0, StepOrdinal = stepIndex,
                            RecordedAt = DateTimeOffset.UtcNow,
                            ToolName = tool.ToolName,
                            ArgumentsJson = tool.BuildArguments(goal, results),
                        };
                        await Append(request);
                    }

                    var resultJson = log.OfType<ToolCallSucceeded>()
                        .FirstOrDefault(o => o.IdempotencyKey == request.IdempotencyKey)?.ResultJson;
                    if (resultJson is null)
                    {
                        var call = await ExecuteCallAsync(request, "execute_tool", request.ToolName,
                            token => tools[request.ToolName].ExecuteAsync(request.ArgumentsJson, request.IdempotencyKey, token));
                        if (call is null)
                            return await UnwindAsync(LastError(request.IdempotencyKey));
                        await Append(new ToolCallSucceeded
                        {
                            SessionId = sessionId, Sequence = ++seq,
                            TurnOrdinal = request.TurnOrdinal, StepOrdinal = request.StepOrdinal,
                            RecordedAt = DateTimeOffset.UtcNow,
                            Attempt = call.Value.Attempt, ResultJson = call.Value.Result,
                        });
                        resultJson = call.Value.Result;
                    }

                    results[tool.ToolName] = resultJson;
                    lastResult = resultJson;
                    break;
                }

                case ModelStep model:
                {
                    var client = modelClient
                        ?? throw new InvalidOperationException($"script has model step '{model.Name}' but no model client is configured");
                    var request = log.OfType<ModelCallRequested>()
                        .FirstOrDefault(r => r.StepOrdinal == stepIndex);
                    if (request is null)
                    {
                        request = new ModelCallRequested
                        {
                            SessionId = sessionId, Sequence = ++seq, TurnOrdinal = 0, StepOrdinal = stepIndex,
                            RecordedAt = DateTimeOffset.UtcNow,
                            ModelId = client.ModelId,
                            PromptJson = model.BuildPromptJson(goal, results),
                            ParametersJson = client.ParametersJson,
                            ConfigVersion = client.ConfigVersion,
                        };
                        await Append(request);
                    }

                    // ADR-0004: a recorded outcome is substituted, never re-run.
                    var outputJson = log.OfType<ModelCallSucceeded>()
                        .FirstOrDefault(o => o.IdempotencyKey == request.IdempotencyKey)?.OutputJson;
                    if (outputJson is null)
                    {
                        var call = await ExecuteCallAsync(request, "invoke_model", request.ModelId,
                            token => client.CompleteAsync(request.PromptJson, token));
                        if (call is null)
                            return await UnwindAsync(LastError(request.IdempotencyKey));
                        await Append(new ModelCallSucceeded
                        {
                            SessionId = sessionId, Sequence = ++seq,
                            TurnOrdinal = request.TurnOrdinal, StepOrdinal = request.StepOrdinal,
                            RecordedAt = DateTimeOffset.UtcNow,
                            Attempt = call.Value.Attempt, OutputJson = call.Value.Result,
                        });
                        outputJson = call.Value.Result;
                    }

                    results[model.Name] = outputJson;
                    lastResult = outputJson;
                    break;
                }
            }
        }

        var final = lastResult ?? "{}";
        await Append(new SessionCompleted
        {
            SessionId = sessionId, Sequence = ++seq, TurnOrdinal = 0, StepOrdinal = script.Count,
            RecordedAt = DateTimeOffset.UtcNow, FinalStateJson = final,
        });
        return new SessionResult(sessionId, final);

        // ── Saga unwind (ADRs 0015-0017) ─────────────────────────────────────
        async Task<SessionResult> UnwindAsync(string reason)
        {
            sessionSpan?.SetTag("aurelia.session.unwinding", true);

            // Succeeded, compensatable tool calls, newest first.
            var toCompensate = log.OfType<ToolCallSucceeded>()
                .Select(ok => (Outcome: ok, Intent: log.OfType<ToolCallRequested>()
                    .FirstOrDefault(r => r.IdempotencyKey == ok.IdempotencyKey)))
                .Where(pair => pair.Intent is not null
                    && tools[pair.Intent!.ToolName] is ICompensatableTool)
                .OrderByDescending(pair => pair.Outcome.Sequence)
                .ToList();

            var compensated = new List<string>();
            var uncompensated = new List<string>();

            foreach (var (outcome, intent) in toCompensate)
            {
                var compensation = log.OfType<CompensationRequested>()
                    .FirstOrDefault(c => c.CompensatesKey == intent!.IdempotencyKey);
                if (compensation is null)
                {
                    compensation = new CompensationRequested
                    {
                        SessionId = sessionId, Sequence = ++seq, TurnOrdinal = 0,
                        StepOrdinal = NextStepOrdinal(),
                        RecordedAt = DateTimeOffset.UtcNow,
                        ToolName = intent!.ToolName,
                        ArgumentsJson = intent.ArgumentsJson,
                        CompensatesKey = intent.IdempotencyKey,
                    };
                    await Append(compensation);
                }

                var compensationDone = log.OfType<ToolCallSucceeded>()
                    .Any(o => o.IdempotencyKey == compensation.IdempotencyKey);
                if (!compensationDone)
                {
                    var tool = (ICompensatableTool)tools[compensation.ToolName];
                    var call = await ExecuteCallAsync(compensation, "compensate_tool", compensation.ToolName,
                        token => tool.CompensateAsync(
                            compensation.ArgumentsJson, outcome.ResultJson,
                            compensation.IdempotencyKey, token));
                    if (call is not null)
                    {
                        await Append(new ToolCallSucceeded
                        {
                            SessionId = sessionId, Sequence = ++seq,
                            TurnOrdinal = compensation.TurnOrdinal, StepOrdinal = compensation.StepOrdinal,
                            RecordedAt = DateTimeOffset.UtcNow,
                            Attempt = call.Value.Attempt, ResultJson = call.Value.Result,
                        });
                        compensationDone = true;
                    }
                }

                (compensationDone ? compensated : uncompensated).Add(intent!.IdempotencyKey);
            }

            var failed = new SessionFailed
            {
                SessionId = sessionId, Sequence = ++seq, TurnOrdinal = 0,
                StepOrdinal = NextStepOrdinal(),
                RecordedAt = DateTimeOffset.UtcNow,
                Reason = reason,
                CompensatedKeys = compensated,
                UncompensatedKeys = uncompensated,
            };
            await Append(failed);
            return new SessionResult(sessionId, FailedStateJson(failed));
        }

        // Executes a call with keyed retries: each failed attempt appends a
        // non-terminal CallAttemptFailed; exhaustion appends a terminal
        // CallFailed and returns null. The caller appends the typed success.
        async Task<(string Result, int Attempt)?> ExecuteCallAsync(
            CallRequested intent, string spanName, string callee,
            Func<CancellationToken, Task<string>> action)
        {
            if (log.OfType<CallFailed>().Any(f => f.IdempotencyKey == intent.IdempotencyKey))
                return null;

            var attempts = log.OfType<CallAttemptFailed>()
                .Count(f => f.IdempotencyKey == intent.IdempotencyKey);
            while (attempts < maxAttempts)
            {
                using var span = AureliaDiagnostics.Source.StartActivity(spanName);
                span?.SetTag("gen_ai.operation.name", spanName);
                span?.SetTag("gen_ai.tool.name", callee);
                span?.SetTag("gen_ai.tool.call.id", intent.IdempotencyKey);
                try
                {
                    var result = await action(ct);
                    return (result, attempts + 1);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    attempts++;
                    await Append(new CallAttemptFailed
                    {
                        SessionId = sessionId, Sequence = ++seq,
                        TurnOrdinal = intent.TurnOrdinal, StepOrdinal = intent.StepOrdinal,
                        RecordedAt = DateTimeOffset.UtcNow,
                        Attempt = attempts, Error = ex.Message,
                    });
                }
            }

            await Append(new CallFailed
            {
                SessionId = sessionId, Sequence = ++seq,
                TurnOrdinal = intent.TurnOrdinal, StepOrdinal = intent.StepOrdinal,
                RecordedAt = DateTimeOffset.UtcNow,
                Attempt = attempts, Error = LastError(intent.IdempotencyKey),
            });
            return null;
        }

        async Task Append(SessionEvent evt)
        {
            await store.AppendAsync(evt, ct);
            log.Add(evt);
        }

        int NextStepOrdinal() =>
            log.OfType<CallRequested>().Max(r => r.StepOrdinal) + 1;

        string LastError(string key) =>
            log.OfType<CallAttemptFailed>().Where(f => f.IdempotencyKey == key)
                .OrderBy(f => f.Sequence).LastOrDefault()?.Error ?? "unknown error";
    }

    private static string FailedStateJson(SessionFailed failed) =>
        JsonSerializer.Serialize(new
        {
            status = "failed",
            reason = failed.Reason,
            compensatedKeys = failed.CompensatedKeys,
            uncompensatedKeys = failed.UncompensatedKeys,
        });
}
