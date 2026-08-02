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
/// parameters, config version, AND the routing decision (ADR-0004, ADR-0020);
/// a completed call is substituted from the log on resume and never
/// re-executed. <see cref="RouteClass"/> declares what kind of work this is
/// (ADR-0021); the policy decides which backend serves it.
/// </summary>
public sealed record ModelStep(
    string Name,
    Func<string, IReadOnlyDictionary<string, string>, string> BuildPromptJson,
    string RouteClass = "general") : AgentStep;

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

/// <summary>
/// A sub-agent step (ADR-0023): hands a goal to another agent, which runs as
/// a full child session under a deterministic id. The child's failure is
/// terminal for this call (ADR-0024); a paused child pauses this session too.
/// </summary>
public sealed record SubAgentStep(
    string AgentName,
    Func<string, IReadOnlyDictionary<string, string>, string> BuildGoal) : AgentStep;

/// <summary>Runs a named sub-agent to completion as its own session.</summary>
public delegate Task<SessionResult> SubAgentRunner(
    string agentName, string childSessionId, string goal, CancellationToken ct);

/// <summary>Retry pacing for external calls (ADR-0027): deterministic
/// exponential backoff, no jitter yet. Attempt failures are recorded events,
/// so a crash mid-backoff resumes the schedule from the logged attempt count.</summary>
public sealed record RetryPolicy(int MaxAttempts, TimeSpan InitialDelay, double BackoffFactor)
{
    public static readonly RetryPolicy Default = new(2, TimeSpan.FromMilliseconds(200), 2.0);

    /// <summary>Delay before the given (2-based and up) attempt number.</summary>
    public TimeSpan DelayBefore(int attempt) =>
        TimeSpan.FromMilliseconds(InitialDelay.TotalMilliseconds * Math.Pow(BackoffFactor, attempt - 2));
}

public sealed class ScriptedAgentDriver(
    IEventStore store,
    IReadOnlyDictionary<string, ITool> tools,
    IReadOnlyList<AgentStep> script,
    ModelRoutingPolicy? modelPolicy = null,
    RetryPolicy? retryPolicy = null,
    SubAgentRunner? subAgents = null) : ISessionRuntime
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
            return new SessionResult(sessionId, SessionStates.FailedJson(alreadyFailed));

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
                        return new SessionResult(sessionId, SessionStates.AwaitingApprovalJson(approval));
                    }
                    if (!resolution.Approved)
                        return await UnwindAsync($"approval denied by {resolution.ApproverId}");
                    break;
                }

                case SubAgentStep sub:
                {
                    var runner = subAgents
                        ?? throw new InvalidOperationException($"script has sub-agent step '{sub.AgentName}' but no sub-agent runner is configured");
                    var request = log.OfType<AgentCallRequested>()
                        .FirstOrDefault(r => r.StepOrdinal == stepIndex);
                    if (request is null)
                    {
                        request = new AgentCallRequested
                        {
                            SessionId = sessionId, Sequence = ++seq, TurnOrdinal = 0, StepOrdinal = stepIndex,
                            RecordedAt = DateTimeOffset.UtcNow,
                            AgentName = sub.AgentName,
                            Goal = sub.BuildGoal(goal, results),
                            // Deterministic (ADR-0023): recovery re-derives the
                            // same child, whose own log resumes it.
                            ChildSessionId = $"{sessionId}.{stepIndex}",
                        };
                        await Append(request);
                    }

                    var childResult = log.OfType<AgentCallSucceeded>()
                        .FirstOrDefault(o => o.IdempotencyKey == request.IdempotencyKey)?.ResultJson;
                    if (childResult is null)
                    {
                        using var span = AureliaDiagnostics.Source.StartActivity("invoke_agent");
                        span?.SetTag("aurelia.subagent.name", request.AgentName);
                        span?.SetTag("aurelia.subagent.session", request.ChildSessionId);
                        var child = await runner(request.AgentName, request.ChildSessionId, request.Goal, ct);

                        switch (DerivedStatus(child.FinalStateJson))
                        {
                            case "failed":
                                // ADR-0024: the child already unwound its own
                                // effects; this call is terminal, no retries.
                                await Append(new CallFailed
                                {
                                    SessionId = sessionId, Sequence = ++seq,
                                    TurnOrdinal = request.TurnOrdinal, StepOrdinal = request.StepOrdinal,
                                    RecordedAt = DateTimeOffset.UtcNow, Attempt = 1,
                                    Error = $"sub-agent '{request.AgentName}' ({request.ChildSessionId}) failed",
                                });
                                return await UnwindAsync($"sub-agent '{request.AgentName}' failed");

                            case "awaiting_approval" or "awaiting_subagent":
                                // A paused child pauses the parent: no outcome,
                                // no terminal event; re-driving resumes both.
                                sessionSpan?.SetTag("aurelia.session.awaiting_subagent", true);
                                return new SessionResult(sessionId,
                                    SessionStates.AwaitingSubAgentJson(request, child.FinalStateJson));

                            default:
                                await Append(new AgentCallSucceeded
                                {
                                    SessionId = sessionId, Sequence = ++seq,
                                    TurnOrdinal = request.TurnOrdinal, StepOrdinal = request.StepOrdinal,
                                    RecordedAt = DateTimeOffset.UtcNow, Attempt = 1,
                                    ChildSessionId = request.ChildSessionId,
                                    ResultJson = child.FinalStateJson,
                                });
                                childResult = child.FinalStateJson;
                                break;
                        }
                    }

                    results[sub.AgentName] = childResult!;
                    lastResult = childResult;
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
                            (_, token) => tools[request.ToolName].ExecuteAsync(request.ArgumentsJson, request.IdempotencyKey, token));
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
                    var policy = modelPolicy
                        ?? throw new InvalidOperationException($"script has model step '{model.Name}' but no routing policy is configured");
                    var route = policy.Resolve(model.RouteClass);
                    var request = log.OfType<ModelCallRequested>()
                        .FirstOrDefault(r => r.StepOrdinal == stepIndex);
                    if (request is null)
                    {
                        // ADR-0020: the intent is the routing-decision record.
                        request = new ModelCallRequested
                        {
                            SessionId = sessionId, Sequence = ++seq, TurnOrdinal = 0, StepOrdinal = stepIndex,
                            RecordedAt = DateTimeOffset.UtcNow,
                            ModelId = route.Primary.ModelId,
                            PromptJson = model.BuildPromptJson(goal, results),
                            ParametersJson = route.Primary.ParametersJson,
                            ConfigVersion = route.Primary.ConfigVersion,
                            RouteClass = model.RouteClass,
                            RoutingPolicy = policy.Name,
                            RoutingReason = DescribeRoute(model.RouteClass, route),
                        };
                        await Append(request);
                    }

                    // ADR-0004: a recorded outcome is substituted, never re-run.
                    var recorded = log.OfType<ModelCallSucceeded>()
                        .FirstOrDefault(o => o.IdempotencyKey == request.IdempotencyKey)?.OutputJson;
                    if (recorded is null)
                    {
                        // ADR-0022: attempt 1 = primary under its budget; later
                        // attempts = the fallback (unbudgeted) when one exists.
                        string? servedBy = null;
                        ModelCompletion? completion = null;
                        var call = await ExecuteCallAsync(request, "invoke_model", request.ModelId,
                            async (attempt, token) =>
                            {
                                var client = attempt == 1 ? route.Primary : route.Fallback ?? route.Primary;
                                servedBy = client.ModelId;
                                // The budget binds the primary on every attempt;
                                // the fallback is the unbudgeted last resort.
                                var budget = ReferenceEquals(client, route.Primary) ? route.LatencyBudget : null;
                                completion = await InvokeWithBudgetAsync(client, request.PromptJson, budget, token);
                                return completion.Content;
                            });
                        if (call is null)
                            return await UnwindAsync(LastError(request.IdempotencyKey));
                        await Append(new ModelCallSucceeded
                        {
                            SessionId = sessionId, Sequence = ++seq,
                            TurnOrdinal = request.TurnOrdinal, StepOrdinal = request.StepOrdinal,
                            RecordedAt = DateTimeOffset.UtcNow,
                            Attempt = call.Value.Attempt, OutputJson = call.Value.Result,
                            ModelId = servedBy,
                            InputTokens = completion?.InputTokens,
                            OutputTokens = completion?.OutputTokens,
                        });
                        recorded = call.Value.Result;
                    }

                    results[model.Name] = recorded;
                    lastResult = recorded;
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
                        (_, token) => tool.CompensateAsync(
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
            return new SessionResult(sessionId, SessionStates.FailedJson(failed));
        }

        // Executes a call with keyed retries: each failed attempt appends a
        // non-terminal CallAttemptFailed; exhaustion appends a terminal
        // CallFailed and returns null. The caller appends the typed success.
        // The action receives the 1-based attempt number so callers can vary
        // the backend per attempt (ADR-0022 fallback).
        async Task<(string Result, int Attempt)?> ExecuteCallAsync(
            CallRequested intent, string spanName, string callee,
            Func<int, CancellationToken, Task<string>> action)
        {
            if (log.OfType<CallFailed>().Any(f => f.IdempotencyKey == intent.IdempotencyKey))
                return null;

            var retry = retryPolicy ?? RetryPolicy.Default;
            var attempts = log.OfType<CallAttemptFailed>()
                .Count(f => f.IdempotencyKey == intent.IdempotencyKey);
            // Demo pacing (playground): env-gated, perimeter-only slow-down so
            // a human can land the chaos button mid-call. Off unless set.
            var stepDelay = Environment.GetEnvironmentVariable("AURELIA_STEP_DELAY_MS");
            while (attempts < retry.MaxAttempts)
            {
                // ADR-0027: exponential backoff before every retry attempt —
                // including resumed ones, since attempts come from the log.
                if (attempts > 0)
                    await Task.Delay(retry.DelayBefore(attempts + 1), ct);
                if (int.TryParse(stepDelay, out var delayMs) && delayMs > 0)
                    await Task.Delay(delayMs, ct);
                using var span = AureliaDiagnostics.Source.StartActivity(spanName);
                span?.SetTag("gen_ai.operation.name", spanName);
                span?.SetTag("gen_ai.tool.name", callee);
                span?.SetTag("gen_ai.tool.call.id", intent.IdempotencyKey);
                try
                {
                    var result = await action(attempts + 1, ct);
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

    private static string DerivedStatus(string finalStateJson)
    {
        using var doc = JsonDocument.Parse(finalStateJson);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("status", out var status)
            ? status.GetString() ?? "completed"
            : "completed";
    }

    private static string DescribeRoute(string routeClass, ModelRoute route)
    {
        var reason = $"class '{routeClass}' → {route.Primary.ModelId}";
        if (route.LatencyBudget is { } budget)
            reason += $", budget {budget.TotalMilliseconds:F0} ms";
        if (route.Fallback is not null)
            reason += $", fallback {route.Fallback.ModelId}";
        return reason;
    }

    // A budget breach must read as an attempt failure, not a cancellation:
    // a real caller cancellation still aborts the activation (ADR-0022).
    private static async Task<ModelCompletion> InvokeWithBudgetAsync(
        IModelClient client, string promptJson, TimeSpan? budget, CancellationToken ct)
    {
        if (budget is null)
            return await client.CompleteAsync(promptJson, ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget.Value);
        try
        {
            return await client.CompleteAsync(promptJson, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"latency budget exceeded ({budget.Value.TotalMilliseconds:F0} ms)");
        }
    }
}
