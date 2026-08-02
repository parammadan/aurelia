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

public sealed record EvalOutcome(string Name, bool Passed, double DurationMs, string? Failure);

public static class EvalRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<IReadOnlyList<EvalOutcome>> RunSuiteAsync(
        string suitePath, int repeat = 1, string? filter = null, CancellationToken ct = default)
    {
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
                try
                {
                    failure = await RunScenarioAsync(scenario, ct);
                }
                catch (Exception ex)
                {
                    failure = $"{ex.GetType().Name}: {ex.Message}";
                }
                outcomes.Add(new EvalOutcome(scenario.Name, failure is null, clock.Elapsed.TotalMilliseconds, failure));
            }
        }
        return outcomes;
    }

    private static async Task<string?> RunScenarioAsync(EvalScenario s, CancellationToken ct)
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-eval-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            ISessionRuntime driver = s.Agent switch
            {
                "case-resolution" => CaseResolutionAgent.CreateDriver(store, dir.FullName, s.FailNotifications),
                // Uses the real endpoint when AURELIA_MODEL_ENDPOINT is set,
                // the deterministic fake otherwise — same suite either way.
                "case-triage" => CaseResolutionAgent.CreateTriageDriver(store, dir.FullName,
                    OpenAICompatibleModelClient.FromEnvironmentOrFake(), s.FailNotifications,
                    OpenAICompatibleModelClient.FallbackFromEnvironment()),
                "echo" => new ScriptedAgentDriver(store,
                    new Dictionary<string, ITool> { ["echo"] = new BlockableEchoTool() },
                    [new ScriptStep("echo", (goal, _) => goal)]),
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
                var ledger = LedgerTypes(Path.Combine(dir.FullName, "ledger.jsonl"));
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
        finally
        {
            dir.Delete(recursive: true);
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
