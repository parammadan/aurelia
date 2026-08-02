// The thin eval runner (Phase 1, "measurement culture starts immediately"):
// declarative scenarios, in-proc runs against the shipped agents, honest
// pass/fail with durations. Grows into the eval harness and the
// Orchestration Reliability Benchmark in Phase 3.
using System.Diagnostics;
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public sealed record EvalScenario
{
    public required string Name { get; init; }
    public required string Agent { get; init; }              // echo | case-resolution
    public required string Goal { get; init; }
    public bool FailNotifications { get; init; }
    public bool RunTwice { get; init; }                       // rerun must be a no-op
    public bool ReplayCheck { get; init; } = true;            // replay must equal live
    public string ExpectStatus { get; init; } = "completed";  // completed | failed
    public string[] ExpectFinalContains { get; init; } = [];
    public string[]? ExpectLedger { get; init; }              // null = skip check
    public string? OnApproval { get; init; }                  // approve | deny | null
}

public sealed record EvalOutcome(
    string Name, bool Passed, double DurationMs, string? Failure,
    long InputTokens = 0, long OutputTokens = 0);

public static class EvalRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<EvalOutcome>> RunSuiteAsync(
        string suitePath, int repeat = 1, string? filter = null, string? chaosSpec = null,
        CancellationToken ct = default)
    {
        // One seeded source for the whole run: the fault sequence is
        // reproducible from the spec (scenarios execute sequentially).
        var chaos = chaosSpec is null ? null : new ChaosSource(ChaosPolicy.Parse(chaosSpec));
        var scenarios = JsonSerializer.Deserialize<List<EvalScenario>>(
            await File.ReadAllTextAsync(suitePath, ct), Json)
            ?? throw new InvalidOperationException($"empty suite: {suitePath}");
        if (!string.IsNullOrEmpty(filter))
            scenarios = [.. scenarios.Where(s => s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))];
        if (scenarios.Count == 0)
            throw new InvalidOperationException($"no scenarios match filter '{filter}'");

        var outcomes = new List<EvalOutcome>(scenarios.Count * repeat);
        for (var run = 0; run < repeat; run++)
        {
            foreach (var scenario in scenarios)
            {
                var clock = Stopwatch.StartNew();
                string? failure;
                var tokens = (In: 0L, Out: 0L);
                try
                {
                    (failure, tokens) = await RunScenarioAsync(scenario, chaos, ct);
                }
                catch (Exception ex)
                {
                    failure = $"{ex.GetType().Name}: {ex.Message}";
                }
                outcomes.Add(new EvalOutcome(scenario.Name, failure is null, clock.Elapsed.TotalMilliseconds,
                    failure, tokens.In, tokens.Out));
            }
        }
        return outcomes;
    }

    private static async Task<(string? Failure, (long In, long Out) Tokens)> RunScenarioAsync(
        EvalScenario s, ChaosSource? chaos, CancellationToken ct)
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-eval-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var failure = await RunChecksAsync(s, chaos, store, dir.FullName, ct);
            // Token usage summed from the event log — the log is the cost record.
            var modelOutcomes = (await store.ReadAsync("eval", ct)).OfType<ModelCallSucceeded>().ToList();
            return (failure,
                (modelOutcomes.Sum(o => (long)(o.InputTokens ?? 0)),
                 modelOutcomes.Sum(o => (long)(o.OutputTokens ?? 0))));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static async Task<string?> RunChecksAsync(
        EvalScenario s, ChaosSource? chaos, FileEventStore store, string dirPath, CancellationToken ct)
    {
        {
            var echoTools = new Dictionary<string, ITool> { ["echo"] = new BlockableEchoTool() };
            ISessionRuntime driver = s.Agent switch
            {
                "case-resolution" => CaseResolutionAgent.CreateDriver(store, dirPath, s.FailNotifications, chaos),
                // Uses the real endpoint when AURELIA_MODEL_ENDPOINT is set,
                // the deterministic fake otherwise — same suite either way.
                "case-triage" => CaseResolutionAgent.CreateTriageDriver(store, dirPath,
                    OpenAICompatibleModelClient.FromEnvironmentOrFake(), s.FailNotifications,
                    OpenAICompatibleModelClient.FallbackFromEnvironment(), chaos),
                "concierge" => CaseResolutionAgent.CreateConciergeDriver(store, dirPath,
                    OpenAICompatibleModelClient.FromEnvironmentOrFake(), s.FailNotifications, chaos),
                "echo" => new ScriptedAgentDriver(store,
                    chaos is null ? echoTools : chaos.WrapTools(echoTools),
                    [new ScriptStep("echo", ctx => ctx.UserMessage)]),
                _ => throw new InvalidOperationException($"unknown agent '{s.Agent}'"),
            };

            var result = await driver.RunToCompletionAsync("eval", s.Goal, ct);

            // Resolve approval pauses when the scenario says how.
            for (var guard = 0; guard < 3 && s.OnApproval is not null
                && StatusOf(result.FinalStateJson) == "awaiting_approval"; guard++)
            {
                using var pausedState = JsonDocument.Parse(result.FinalStateJson);
                var approvalKey = pausedState.RootElement.GetProperty("approvalKey").GetString()!;
                await Approvals.ResolveAsync(store, "eval", approvalKey,
                    approved: s.OnApproval == "approve", "eval-runner", ct);
                result = await driver.RunToCompletionAsync("eval", s.Goal, ct);
            }

            if (s.RunTwice)
            {
                var again = await driver.RunToCompletionAsync("eval", s.Goal, ct);
                if (again.FinalStateJson != result.FinalStateJson)
                    return "rerun diverged from first run";
            }

            var status = StatusOf(result.FinalStateJson);
            if (status != s.ExpectStatus)
                return $"expected status '{s.ExpectStatus}', got '{status}': {result.FinalStateJson}";

            foreach (var fragment in s.ExpectFinalContains)
                if (!result.FinalStateJson.Contains(fragment, StringComparison.Ordinal))
                    return $"final state missing '{fragment}': {result.FinalStateJson}";

            if (s.ExpectLedger is not null)
            {
                var ledger = LedgerTypes(Path.Combine(dirPath, "ledger.jsonl"));
                if (!ledger.SequenceEqual(s.ExpectLedger))
                    return $"expected ledger [{string.Join(",", s.ExpectLedger)}], got [{string.Join(",", ledger)}]";
            }

            if (s.ReplayCheck)
            {
                var replayed = await new ReconstructionReplayEngine(store).ReplayAsync("eval", ct);
                if (replayed.FinalStateJson != result.FinalStateJson)
                    return "replay diverged from live run";
            }

            return null;
        }
    }

    private static string StatusOf(string finalStateJson)
    {
        using var doc = JsonDocument.Parse(finalStateJson);
        return doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("status", out var status)
            ? status.GetString() ?? "completed"
            : "completed";
    }

    private static string[] LedgerTypes(string ledgerPath) =>
        !File.Exists(ledgerPath)
            ? []
            : [.. File.ReadLines(ledgerPath)
                .Select(l => JsonDocument.Parse(l).RootElement.GetProperty("type").GetString()!)];
}
