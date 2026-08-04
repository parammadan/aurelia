// The scripted execution engine: runs a fixed sequence of steps with
// resumability from the event log at every point — fresh, mid-step after a
// crash, mid-retry, mid-unwind, or mid-turn. Implements the two-phase call
// protocol (ADR-0008), keyed retries (ADR-0010, ADR-0027), durable model
// calls with routing (ADRs 0004, 0020-0022), approvals (ADRs 0011-0013),
// sub-agents (ADRs 0023-0025), the saga unwind (ADRs 0015-0017), and
// conversations with tiered memory (ADRs 0028-0030).
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public abstract record AgentStep;

/// <summary>One tool step: which tool to call and how to build its arguments
/// from the turn context (ADR-0029).</summary>
public sealed record ScriptStep(
    string ToolName,
    Func<TurnContext, string> BuildArguments) : AgentStep;

/// <summary>
/// One model step: builds a chat prompt from the turn context. Durable
/// two-phase — the intent records the full prompt, model id, parameters,
/// config version, and the routing decision (ADR-0004, ADR-0020); a recorded
/// outcome is substituted on resume and never re-executed.
/// </summary>
public sealed record ModelStep(
    string Name,
    Func<TurnContext, string> BuildPromptJson,
    string RouteClass = "general") : AgentStep;

/// <summary>
/// A conditional human-approval gate (ADRs 0011-0013). The predicate is a
/// pure function over the turn context; a non-null question pauses the
/// session — no terminal event, just an intent with no outcome — until an
/// ApprovalResolved outcome arrives from any process.
/// </summary>
public sealed record ApprovalGate(
    Func<TurnContext, string?> QuestionIfRequired,
    string ApproverPolicy,
    TimeSpan? Expiry = null) : AgentStep;

/// <summary>
/// A sub-agent step (ADR-0023): hands a goal to another agent, which runs as
/// a full child session under a deterministic id. The child's failure is
/// terminal for this call (ADR-0024); a paused child pauses this session too.
/// </summary>
public sealed record SubAgentStep(
    string AgentName,
    Func<TurnContext, string> BuildGoal) : AgentStep;

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
    SubAgentRunner? subAgents = null,
    MemoryPolicy? memory = null,
    Func<DateTimeOffset>? clock = null) : ISessionRuntime
{
    private readonly RetryPolicy _retry = retryPolicy ?? RetryPolicy.Default;
    private readonly MemoryPolicy _memory = memory ?? MemoryPolicy.Default;
    // The clock the runtime OBSERVES for decisions (expiry). Decisions still
    // ride the RECORDED TimeProvided value (ADRs 0006/0013); injecting the
    // observation lets tests use virtual time. RecordedAt stamps stay
    // wall-clock — they are observability metadata, never decision inputs.
    private readonly Func<DateTimeOffset> _now = clock ?? (static () => DateTimeOffset.UtcNow);
    private IReadOnlyList<AgentStep> Script => script;

    // ── Single-shot entry (goal-driven / queue agents): turn 0, terminal ────
    public async Task<SessionResult> RunToCompletionAsync(string sessionId, string goal, CancellationToken ct = default)
    {
        using var sessionSpan = AureliaDiagnostics.Source.StartActivity("session.run");
        sessionSpan?.SetTag("aurelia.session.id", sessionId);
        var a = await ActivationAsync(sessionId, ct);
        sessionSpan?.SetTag("aurelia.session.resumed", a.Resumed);
        if (a.Terminal is { } terminal)
            return terminal;

        if (a.Log.OfType<SessionStarted>().FirstOrDefault() is { } started)
            goal = started.Goal; // resumed session: the recorded goal wins
        else
            await a.AppendAsync(new SessionStarted
            {
                SessionId = sessionId, Sequence = a.NextSeq(), TurnOrdinal = 0, StepOrdinal = 0,
                RecordedAt = DateTimeOffset.UtcNow, Goal = goal,
            });

        if (await a.ResumeFailureAsync() is { } unwound)
            return unwound;

        var context = new TurnContext(goal, [], null, new Dictionary<string, string>());
        var run = await ExecuteScriptAsync(a, turn: 0, context);
        if (run.Interrupted is { } interrupted)
            return interrupted;

        var final = run.LastResult ?? "{}";
        await a.AppendAsync(new SessionCompleted
        {
            SessionId = sessionId, Sequence = a.NextSeq(), TurnOrdinal = 0, StepOrdinal = script.Count,
            RecordedAt = DateTimeOffset.UtcNow, FinalStateJson = final,
        });
        return new SessionResult(sessionId, final);
    }

    // ── Conversation entry (ADR-0028): one user message = one turn ──────────
    public async Task<SessionResult> HandleTurnAsync(string sessionId, string userMessage, CancellationToken ct = default)
    {
        using var sessionSpan = AureliaDiagnostics.Source.StartActivity("session.run");
        sessionSpan?.SetTag("aurelia.session.id", sessionId);
        var a = await ActivationAsync(sessionId, ct);
        sessionSpan?.SetTag("aurelia.session.resumed", a.Resumed);
        if (a.Terminal is { } terminal)
            return terminal;

        if (!a.Log.OfType<SessionStarted>().Any())
            await a.AppendAsync(new SessionStarted
            {
                SessionId = sessionId, Sequence = a.NextSeq(), TurnOrdinal = 0, StepOrdinal = 0,
                RecordedAt = DateTimeOffset.UtcNow, Goal = userMessage,
            });

        if (await a.ResumeFailureAsync() is { } unwound)
            return unwound;

        // An interrupted summarization from the previous turn finishes first.
        await MaybeSummarizeAsync(a);

        // Resume an interrupted turn (recorded message wins) or open the next.
        var lastUser = a.Log.OfType<UserMessageReceived>().OrderBy(e => e.TurnOrdinal).LastOrDefault();
        var lastAgent = a.Log.OfType<AgentMessageSent>().OrderBy(e => e.TurnOrdinal).LastOrDefault();
        int turn;
        if (lastUser is not null && (lastAgent is null || lastAgent.TurnOrdinal < lastUser.TurnOrdinal))
        {
            turn = lastUser.TurnOrdinal;
            userMessage = lastUser.Content;
        }
        else
        {
            turn = (lastUser?.TurnOrdinal ?? 0) + 1;
            await a.AppendAsync(new UserMessageReceived
            {
                SessionId = sessionId, Sequence = a.NextSeq(), TurnOrdinal = turn, StepOrdinal = 0,
                RecordedAt = DateTimeOffset.UtcNow, Content = userMessage,
            });
        }

        var (window, summary) = BuildWindow(a.Log);
        var context = new TurnContext(userMessage, window, summary, new Dictionary<string, string>());
        var run = await ExecuteScriptAsync(a, turn, context);
        if (run.Interrupted is { } interrupted)
            return interrupted;

        var reply = run.LastResult ?? "{}";
        if (!a.Log.OfType<AgentMessageSent>().Any(e => e.TurnOrdinal == turn))
            await a.AppendAsync(new AgentMessageSent
            {
                SessionId = sessionId, Sequence = a.NextSeq(), TurnOrdinal = turn, StepOrdinal = script.Count,
                RecordedAt = DateTimeOffset.UtcNow, Content = reply,
            });

        await MaybeSummarizeAsync(a);
        return new SessionResult(sessionId, reply);
    }

    // ── The turn-scoped script executor: shared by both entries ─────────────
    private sealed record ScriptRun(string? LastResult, SessionResult? Interrupted);

    private async Task<ScriptRun> ExecuteScriptAsync(Activation a, int turn, TurnContext context)
    {
        var results = (Dictionary<string, string>)context.Results;
        string? lastResult = null;

        for (var stepIndex = 0; stepIndex < script.Count; stepIndex++)
        {
            switch (script[stepIndex])
            {
                case ApprovalGate gate:
                {
                    var question = gate.QuestionIfRequired(context);
                    if (question is null)
                        break;

                    var approval = a.Log.OfType<ApprovalRequested>()
                        .FirstOrDefault(r => r.TurnOrdinal == turn && r.StepOrdinal == stepIndex);
                    if (approval is null)
                    {
                        approval = new ApprovalRequested
                        {
                            SessionId = a.SessionId, Sequence = a.NextSeq(), TurnOrdinal = turn, StepOrdinal = stepIndex,
                            RecordedAt = DateTimeOffset.UtcNow,
                            Question = question,
                            ApproverPolicy = gate.ApproverPolicy,
                            ExpiresAt = gate.Expiry is { } expiry ? _now() + expiry : null,
                        };
                        await a.AppendAsync(approval);
                    }

                    var resolution = a.Log.OfType<ApprovalResolved>()
                        .FirstOrDefault(o => o.IdempotencyKey == approval.IdempotencyKey);
                    if (resolution is not null)
                    {
                        if (!resolution.Approved)
                            return new ScriptRun(null,
                                await UnwindAsync(a, $"approval denied by {resolution.ApproverId}"));
                        break;
                    }

                    // Expiry (ADRs 0006/0013): a previously RECORDED injected
                    // time decides first (replay determinism); otherwise the
                    // runtime observes the clock, records it, then decides on
                    // the recorded value.
                    if (a.Log.OfType<ApprovalExpired>()
                        .Any(o => o.IdempotencyKey == approval.IdempotencyKey))
                        return new ScriptRun(null,
                            await UnwindAsync(a, "approval expired"));
                    if (approval.ExpiresAt is { } deadline)
                    {
                        var timeKey = a.Log.OfType<TimeRequested>()
                            .Where(t => t.TurnOrdinal == turn)
                            .OrderByDescending(t => t.Sequence).FirstOrDefault();
                        var recordedNow = timeKey is null ? (DateTimeOffset?)null
                            : a.Log.OfType<TimeProvided>()
                                .FirstOrDefault(o => o.IdempotencyKey == timeKey.IdempotencyKey)?.Value;
                        if (recordedNow is null && _now() >= deadline)
                        {
                            var timeRequest = new TimeRequested
                            {
                                SessionId = a.SessionId, Sequence = a.NextSeq(), TurnOrdinal = turn,
                                StepOrdinal = a.NextStepOrdinal(turn),
                                RecordedAt = DateTimeOffset.UtcNow,
                            };
                            await a.AppendAsync(timeRequest);
                            await a.AppendAsync(new TimeProvided
                            {
                                SessionId = a.SessionId, Sequence = a.NextSeq(),
                                TurnOrdinal = timeRequest.TurnOrdinal, StepOrdinal = timeRequest.StepOrdinal,
                                RecordedAt = DateTimeOffset.UtcNow, Attempt = 1,
                                Value = _now(),
                            });
                            recordedNow = a.Log.OfType<TimeProvided>().Last().Value;
                        }
                        if (recordedNow is { } now && now >= deadline)
                        {
                            await a.AppendAsync(new ApprovalExpired
                            {
                                SessionId = a.SessionId, Sequence = a.NextSeq(),
                                TurnOrdinal = approval.TurnOrdinal, StepOrdinal = approval.StepOrdinal,
                                RecordedAt = DateTimeOffset.UtcNow, Attempt = 1,
                            });
                            return new ScriptRun(null,
                                await UnwindAsync(a, "approval expired"));
                        }
                    }
                    return new ScriptRun(null,
                        new SessionResult(a.SessionId, SessionStates.AwaitingApprovalJson(approval)));
                }

                case SubAgentStep sub:
                {
                    var runner = subAgents
                        ?? throw new InvalidOperationException($"script has sub-agent step '{sub.AgentName}' but no sub-agent runner is configured");
                    var request = a.Log.OfType<AgentCallRequested>()
                        .FirstOrDefault(r => r.TurnOrdinal == turn && r.StepOrdinal == stepIndex);
                    if (request is null)
                    {
                        request = new AgentCallRequested
                        {
                            SessionId = a.SessionId, Sequence = a.NextSeq(), TurnOrdinal = turn, StepOrdinal = stepIndex,
                            RecordedAt = DateTimeOffset.UtcNow,
                            AgentName = sub.AgentName,
                            Goal = sub.BuildGoal(context),
                            ChildSessionId = $"{a.SessionId}.{turn}.{stepIndex}",
                        };
                        await a.AppendAsync(request);
                    }

                    var childResult = a.Log.OfType<AgentCallSucceeded>()
                        .FirstOrDefault(o => o.IdempotencyKey == request.IdempotencyKey)?.ResultJson;
                    if (childResult is null)
                    {
                        using var span = AureliaDiagnostics.Source.StartActivity("invoke_agent");
                        span?.SetTag("aurelia.subagent.name", request.AgentName);
                        span?.SetTag("aurelia.subagent.session", request.ChildSessionId);
                        var child = await runner(request.AgentName, request.ChildSessionId, request.Goal, a.Ct);

                        switch (SessionStates.StatusOf(child.FinalStateJson))
                        {
                            case "failed":
                                await a.AppendAsync(new CallFailed
                                {
                                    SessionId = a.SessionId, Sequence = a.NextSeq(),
                                    TurnOrdinal = request.TurnOrdinal, StepOrdinal = request.StepOrdinal,
                                    RecordedAt = DateTimeOffset.UtcNow, Attempt = 1,
                                    Error = $"sub-agent '{request.AgentName}' ({request.ChildSessionId}) failed",
                                });
                                return new ScriptRun(null,
                                    await UnwindAsync(a, $"sub-agent '{request.AgentName}' failed"));

                            case "awaiting_approval" or "awaiting_subagent":
                                return new ScriptRun(null, new SessionResult(a.SessionId,
                                    SessionStates.AwaitingSubAgentJson(request, child.FinalStateJson)));

                            default:
                                await a.AppendAsync(new AgentCallSucceeded
                                {
                                    SessionId = a.SessionId, Sequence = a.NextSeq(),
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
                    var request = a.Log.OfType<ToolCallRequested>()
                        .FirstOrDefault(r => r.TurnOrdinal == turn && r.StepOrdinal == stepIndex);
                    if (request is null)
                    {
                        request = new ToolCallRequested
                        {
                            SessionId = a.SessionId, Sequence = a.NextSeq(), TurnOrdinal = turn, StepOrdinal = stepIndex,
                            RecordedAt = DateTimeOffset.UtcNow,
                            ToolName = tool.ToolName,
                            ArgumentsJson = tool.BuildArguments(context),
                        };
                        await a.AppendAsync(request);
                    }

                    var resultJson = a.Log.OfType<ToolCallSucceeded>()
                        .FirstOrDefault(o => o.IdempotencyKey == request.IdempotencyKey)?.ResultJson;
                    if (resultJson is null)
                    {
                        var call = await ExecuteCallAsync(a, request, "execute_tool", request.ToolName,
                            (_, token) => tools[request.ToolName].ExecuteAsync(request.ArgumentsJson, request.IdempotencyKey, token));
                        if (call is null)
                            return new ScriptRun(null, await UnwindAsync(a, a.LastError(request.IdempotencyKey)));
                        await a.AppendAsync(new ToolCallSucceeded
                        {
                            SessionId = a.SessionId, Sequence = a.NextSeq(),
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
                    var invoke = await InvokeModelAsync(a, turn, stepIndex, model.RouteClass,
                        () => model.BuildPromptJson(context));
                    if (invoke.Failed)
                        return new ScriptRun(null, await UnwindAsync(a, invoke.Error!));
                    results[model.Name] = invoke.Output!;
                    lastResult = invoke.Output;
                    break;
                }
            }
        }

        return new ScriptRun(lastResult, null);
    }

    // ── Durable model invocation (shared by steps and summarization) ────────
    private sealed record ModelInvoke(string? Output, bool Failed, string? Error);

    private async Task<ModelInvoke> InvokeModelAsync(
        Activation a, int turn, int stepIndex, string routeClass, Func<string> buildPrompt)
    {
        var policy = modelPolicy
            ?? throw new InvalidOperationException("script requires a model routing policy, but none is configured");
        var route = policy.Resolve(routeClass);

        var request = a.Log.OfType<ModelCallRequested>()
            .FirstOrDefault(r => r.TurnOrdinal == turn && r.StepOrdinal == stepIndex);
        if (request is null)
        {
            request = new ModelCallRequested
            {
                SessionId = a.SessionId, Sequence = a.NextSeq(), TurnOrdinal = turn, StepOrdinal = stepIndex,
                RecordedAt = DateTimeOffset.UtcNow,
                ModelId = route.Primary.ModelId,
                PromptJson = buildPrompt(),
                ParametersJson = route.Primary.ParametersJson,
                ConfigVersion = route.Primary.ConfigVersion,
                RouteClass = routeClass,
                RoutingPolicy = policy.Name,
                RoutingReason = DescribeRoute(routeClass, route),
            };
            await a.AppendAsync(request);
        }

        // ADR-0004: a recorded outcome is substituted, never re-run.
        var recorded = a.Log.OfType<ModelCallSucceeded>()
            .FirstOrDefault(o => o.IdempotencyKey == request.IdempotencyKey)?.OutputJson;
        if (recorded is not null)
            return new ModelInvoke(recorded, false, null);

        string? servedBy = null;
        ModelCompletion? completion = null;
        var call = await ExecuteCallAsync(a, request, "invoke_model", request.ModelId,
            async (attempt, token) =>
            {
                var client = attempt == 1 ? route.Primary : route.Fallback ?? route.Primary;
                servedBy = client.ModelId;
                var budget = ReferenceEquals(client, route.Primary) ? route.LatencyBudget : null;
                completion = await InvokeWithBudgetAsync(client, request.PromptJson, budget, token);
                return completion.Content;
            });
        if (call is null)
            return new ModelInvoke(null, true, a.LastError(request.IdempotencyKey));

        await a.AppendAsync(new ModelCallSucceeded
        {
            SessionId = a.SessionId, Sequence = a.NextSeq(),
            TurnOrdinal = request.TurnOrdinal, StepOrdinal = request.StepOrdinal,
            RecordedAt = DateTimeOffset.UtcNow,
            Attempt = call.Value.Attempt, OutputJson = call.Value.Result,
            ModelId = servedBy,
            InputTokens = completion?.InputTokens,
            OutputTokens = completion?.OutputTokens,
        });
        return new ModelInvoke(call.Value.Result, false, null);
    }

    // ── Memory tiers (ADR-0030) ──────────────────────────────────────────────
    private (IReadOnlyList<TurnRecord> Window, string? Summary) BuildWindow(IReadOnlyList<SessionEvent> log)
    {
        var checkpoint = log.OfType<SummaryCheckpointed>().OrderBy(s => s.Sequence).LastOrDefault();
        var through = checkpoint?.ThroughTurn ?? 0;
        var turns = log.OfType<AgentMessageSent>()
            .Where(agentMsg => agentMsg.TurnOrdinal > through)
            .Select(agentMsg => new TurnRecord(
                agentMsg.TurnOrdinal,
                log.OfType<UserMessageReceived>().FirstOrDefault(u => u.TurnOrdinal == agentMsg.TurnOrdinal)?.Content ?? "",
                agentMsg.Content))
            .OrderBy(t => t.Turn)
            .ToList();
        if (turns.Count > _memory.HotTurns)
            turns = [.. turns.Skip(turns.Count - _memory.HotTurns)];
        return (turns, checkpoint?.Summary);
    }

    /// <summary>Refreshes the warm summary when due (ADR-0030): a durable
    /// 'summarize' model call at (turn, step > script length) followed by a
    /// SummaryCheckpointed. Idempotent by log — safe to call on resume.</summary>
    private async Task MaybeSummarizeAsync(Activation a)
    {
        if (modelPolicy is null || _memory.SummarizeEveryTurns <= 0)
            return;
        var lastAgent = a.Log.OfType<AgentMessageSent>().OrderBy(e => e.TurnOrdinal).LastOrDefault();
        if (lastAgent is null)
            return;
        var turn = lastAgent.TurnOrdinal;
        var checkpoint = a.Log.OfType<SummaryCheckpointed>().OrderBy(s => s.Sequence).LastOrDefault();
        var lastThrough = checkpoint?.ThroughTurn ?? 0;
        var newThrough = turn - _memory.HotTurns;
        var pending = a.Log.OfType<ModelCallRequested>()
            .FirstOrDefault(r => r.TurnOrdinal == turn && r.StepOrdinal == script.Count + 1);
        var due = turn - lastThrough >= _memory.SummarizeEveryTurns + _memory.HotTurns && newThrough > lastThrough;
        if (!due && pending is null)
            return;
        if (pending is null && a.Log.OfType<SummaryCheckpointed>().Any(s => s.TurnOrdinal == turn))
            return;

        var toSummarize = a.Log.OfType<AgentMessageSent>()
            .Where(m => m.TurnOrdinal > lastThrough && m.TurnOrdinal <= (pending is null ? newThrough : int.MaxValue))
            .OrderBy(m => m.TurnOrdinal)
            .Select(m => new
            {
                turn = m.TurnOrdinal,
                user = a.Log.OfType<UserMessageReceived>().FirstOrDefault(u => u.TurnOrdinal == m.TurnOrdinal)?.Content ?? "",
                agent = m.Content,
            })
            .ToArray();

        var prompt = JsonSerializer.Serialize(new object[]
        {
            new { role = "system", content = "Summarize this conversation compactly, preserving every "
                + "fact, decision, and open item. Output plain text only." },
            new { role = "user", content = JsonSerializer.Serialize(new
                { priorSummary = checkpoint?.Summary, turns = toSummarize }) },
        });

        var invoke = await InvokeModelAsync(a, turn, script.Count + 1, "summarize", () => prompt);
        if (invoke.Failed)
            return; // best-effort: memory keeps working off the old checkpoint

        if (!a.Log.OfType<SummaryCheckpointed>().Any(s => s.TurnOrdinal == turn))
            await a.AppendAsync(new SummaryCheckpointed
            {
                SessionId = a.SessionId, Sequence = a.NextSeq(), TurnOrdinal = turn, StepOrdinal = script.Count + 2,
                RecordedAt = DateTimeOffset.UtcNow,
                Summary = invoke.Output!,
                ThroughTurn = pending is null ? newThrough : Math.Max(lastThrough, pending.TurnOrdinal - _memory.HotTurns),
            });
    }

    // ── Saga unwind (ADRs 0015-0017): terminal for the whole session ────────
    private async Task<SessionResult> UnwindAsync(Activation a, string reason)
    {
        // Every succeeded call is accounted for; none is filtered away silently
        // (ADR-0042). Compensatable ones are unwound in reverse order of
        // success; the rest are reported as unrecoverable.
        var succeeded = a.Log.OfType<ToolCallSucceeded>()
            .Select(ok => (Outcome: ok, Intent: a.Log.OfType<ToolCallRequested>()
                .FirstOrDefault(r => r.IdempotencyKey == ok.IdempotencyKey)))
            .Where(pair => pair.Intent is not null)
            .OrderByDescending(pair => pair.Outcome.Sequence)
            .ToList();

        var toCompensate = succeeded
            .Where(pair => tools[pair.Intent!.ToolName] is ICompensatableTool)
            .ToList();
        var unrecoverable = succeeded
            .Where(pair => tools[pair.Intent!.ToolName] is not ICompensatableTool)
            .Select(pair => pair.Intent!.IdempotencyKey)
            .ToList();

        var compensated = new List<string>();
        var uncompensated = new List<string>();

        foreach (var (outcome, intent) in toCompensate)
        {
            var compensation = a.Log.OfType<CompensationRequested>()
                .FirstOrDefault(c => c.CompensatesKey == intent!.IdempotencyKey);
            if (compensation is null)
            {
                compensation = new CompensationRequested
                {
                    SessionId = a.SessionId, Sequence = a.NextSeq(), TurnOrdinal = intent!.TurnOrdinal,
                    StepOrdinal = a.NextStepOrdinal(intent.TurnOrdinal),
                    RecordedAt = DateTimeOffset.UtcNow,
                    ToolName = intent.ToolName,
                    ArgumentsJson = intent.ArgumentsJson,
                    CompensatesKey = intent.IdempotencyKey,
                };
                await a.AppendAsync(compensation);
            }

            var compensationDone = a.Log.OfType<ToolCallSucceeded>()
                .Any(o => o.IdempotencyKey == compensation.IdempotencyKey);
            if (!compensationDone)
            {
                var tool = (ICompensatableTool)tools[compensation.ToolName];
                var call = await ExecuteCallAsync(a, compensation, "compensate_tool", compensation.ToolName,
                    (_, token) => tool.CompensateAsync(
                        compensation.ArgumentsJson, outcome.ResultJson,
                        compensation.IdempotencyKey, token));
                if (call is not null)
                {
                    await a.AppendAsync(new ToolCallSucceeded
                    {
                        SessionId = a.SessionId, Sequence = a.NextSeq(),
                        TurnOrdinal = compensation.TurnOrdinal, StepOrdinal = compensation.StepOrdinal,
                        RecordedAt = DateTimeOffset.UtcNow,
                        Attempt = call.Value.Attempt, ResultJson = call.Value.Result,
                    });
                    compensationDone = true;
                }
            }

            (compensationDone ? compensated : uncompensated).Add(intent!.IdempotencyKey);
        }

        var lastTurn = a.Log.OfType<CallRequested>().Max(r => (int?)r.TurnOrdinal) ?? 0;
        var failed = new SessionFailed
        {
            SessionId = a.SessionId, Sequence = a.NextSeq(), TurnOrdinal = lastTurn,
            StepOrdinal = a.NextStepOrdinal(lastTurn),
            RecordedAt = DateTimeOffset.UtcNow,
            Reason = reason,
            CompensatedKeys = compensated,
            UncompensatedKeys = uncompensated,
            UnrecoverableKeys = unrecoverable,
        };
        await a.AppendAsync(failed);
        return new SessionResult(a.SessionId, SessionStates.FailedJson(failed));
    }

    // ── Keyed retries with backoff (ADRs 0010, 0027) ─────────────────────────
    private async Task<(string Result, int Attempt)?> ExecuteCallAsync(
        Activation a, CallRequested intent, string spanName, string callee,
        Func<int, CancellationToken, Task<string>> action)
    {
        if (a.Log.OfType<CallFailed>().Any(f => f.IdempotencyKey == intent.IdempotencyKey))
            return null;

        var attempts = a.Log.OfType<CallAttemptFailed>()
            .Count(f => f.IdempotencyKey == intent.IdempotencyKey);
        var stepDelay = Environment.GetEnvironmentVariable("AURELIA_STEP_DELAY_MS");
        while (attempts < _retry.MaxAttempts)
        {
            if (attempts > 0)
                await Task.Delay(_retry.DelayBefore(attempts + 1), a.Ct);
            // Demo pacing (playground): env-gated, perimeter-only slow-down.
            if (int.TryParse(stepDelay, out var delayMs) && delayMs > 0)
                await Task.Delay(delayMs, a.Ct);
            using var span = AureliaDiagnostics.Source.StartActivity(spanName);
            span?.SetTag("gen_ai.operation.name", spanName);
            span?.SetTag("gen_ai.tool.name", callee);
            span?.SetTag("gen_ai.tool.call.id", intent.IdempotencyKey);
            try
            {
                var result = await action(attempts + 1, a.Ct);
                return (result, attempts + 1);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                attempts++;
                await a.AppendAsync(new CallAttemptFailed
                {
                    SessionId = a.SessionId, Sequence = a.NextSeq(),
                    TurnOrdinal = intent.TurnOrdinal, StepOrdinal = intent.StepOrdinal,
                    RecordedAt = DateTimeOffset.UtcNow,
                    Attempt = attempts, Error = ex.Message,
                });
            }
        }

        await a.AppendAsync(new CallFailed
        {
            SessionId = a.SessionId, Sequence = a.NextSeq(),
            TurnOrdinal = intent.TurnOrdinal, StepOrdinal = intent.StepOrdinal,
            RecordedAt = DateTimeOffset.UtcNow,
            Attempt = attempts, Error = a.LastError(intent.IdempotencyKey),
        });
        return null;
    }

    // ── Per-activation state: the fold, the sequence, the guards ────────────
    private sealed class Activation(ScriptedAgentDriver driver, string sessionId, List<SessionEvent> log, CancellationToken ct)
    {
        private long _seq = log.Count == 0 ? 0 : log[^1].Sequence;
        private readonly ScriptedAgentDriver _driver = driver;

        public string SessionId => sessionId;
        public List<SessionEvent> Log => log;
        public bool Resumed { get; } = log.Count > 0;
        public CancellationToken Ct => ct;
        public SessionResult? Terminal { get; init; }

        public long NextSeq() => ++_seq;

        public async Task AppendAsync(SessionEvent evt)
        {
            await _driver.RecordAsync(evt, ct);
            log.Add(evt);
        }

        /// <summary>Resuming into an already-failed script (terminal call
        /// failure or denied approval) goes straight to the unwind.</summary>
        public async Task<SessionResult?> ResumeFailureAsync()
        {
            var priorFailure = log.OfType<CallFailed>()
                .FirstOrDefault(f => log.OfType<CallRequested>()
                    .Any(r => r is not CompensationRequested && r.IdempotencyKey == f.IdempotencyKey));
            if (priorFailure is not null)
                return await _driver.UnwindAsync(this, priorFailure.Error);
            if (log.OfType<ApprovalResolved>().FirstOrDefault(x => !x.Approved) is { } denied)
                return await _driver.UnwindAsync(this, $"approval denied by {denied.ApproverId}");
            return null;
        }

        public int NextStepOrdinal(int turn) =>
            log.OfType<CallRequested>().Where(r => r.TurnOrdinal == turn)
                .Max(r => (int?)r.StepOrdinal) is { } max
                ? Math.Max(max, _driver.Script.Count) + 1
                : _driver.Script.Count + 1;

        public string LastError(string key) =>
            log.OfType<CallAttemptFailed>().Where(f => f.IdempotencyKey == key)
                .OrderBy(f => f.Sequence).LastOrDefault()?.Error ?? "unknown error";
    }

    private Task RecordAsync(SessionEvent evt, CancellationToken ct) => store.AppendAsync(evt, ct);

    private async Task<Activation> ActivationAsync(string sessionId, CancellationToken ct)
    {
        var log = (await store.ReadAsync(sessionId, ct)).ToList();

        SessionResult? terminal = null;
        if (log.OfType<SessionCompleted>().FirstOrDefault() is { } done)
            terminal = new SessionResult(sessionId, done.FinalStateJson);
        else if (log.OfType<SessionFailed>().FirstOrDefault() is { } failed)
            terminal = new SessionResult(sessionId, SessionStates.FailedJson(failed));
        return new Activation(this, sessionId, log, ct) { Terminal = terminal };
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
