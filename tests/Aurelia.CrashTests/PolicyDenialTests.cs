// The poison pill (ADR-0044): a model can be talked into asking for an
// outrageous effect; the deterministic boundary refuses it BEFORE an intent
// exists. The proof an auditor needs is threefold — the proposal, the denial,
// and the ABSENCE of a request event.
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class PolicyDenialTests
{
    /// <summary>A deterministic boundary: no clock, no randomness, no model —
    /// it refuses a credit beyond what any case can be owed.</summary>
    private static string? RefuseOutrageousCredits(string toolName, string argumentsJson)
    {
        if (toolName != "issue_service_credit")
            return null;
        using var args = JsonDocument.Parse(argumentsJson);
        return args.RootElement.TryGetProperty("entitledCreditUsd", out var amount)
               && amount.GetDecimal() > 1000m
            ? $"credit of ${amount.GetDecimal():0.00} exceeds the $1000.00 ceiling"
            : null;
    }

    [Fact]
    public async Task A_refused_call_leaves_a_proposal_a_denial_and_no_intent()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-poison-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            const string id = "poison-1";

            // The lookup tool reports whatever the case is "entitled" to; the
            // adversarial fixture inflates it far past any real entitlement.
            var tools = CaseResolutionAgent.CreateInProcessTools(dir.FullName);
            tools = new Dictionary<string, ITool>(tools)
            {
                ["lookup_case"] = new InflatedLookupTool(),
            };

            var driver = new ScriptedAgentDriver(
                store, tools, CaseResolutionAgent.Script,
                toolPolicy: RefuseOutrageousCredits);

            // Defence in depth: the amount first trips the human gate. A
            // manager approves it — and the deterministic ceiling STILL
            // refuses. Policy is not a suggestion a human can override.
            var parked = await driver.RunToCompletionAsync(id, "credit for case 42");
            using (var doc = JsonDocument.Parse(parked.FinalStateJson))
            {
                Assert.Equal("awaiting_approval", doc.RootElement.GetProperty("status").GetString());
                Assert.True(await Approvals.ResolveAsync(
                    store, id, doc.RootElement.GetProperty("approvalKey").GetString()!, true, "manager-7"));
            }

            var result = await driver.RunToCompletionAsync(id, "credit for case 42");

            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.Equal("failed", final.RootElement.GetProperty("status").GetString());
            Assert.Contains("policy denied", final.RootElement.GetProperty("reason").GetString());

            var log = await store.ReadAsync(id);

            // 1. What was asked for is on the record…
            var proposed = Assert.Single(log.OfType<ToolCallProposed>());
            Assert.Equal("issue_service_credit", proposed.ToolName);
            Assert.Contains("50000", proposed.ArgumentsJson);

            // 2. …so is the refusal, with its reason…
            var denied = Assert.Single(log.OfType<PolicyDenied>());
            Assert.Equal("issue_service_credit", denied.ToolName);
            Assert.Contains("exceeds the $1000.00 ceiling", denied.Reason);

            // 3. …and NO intent was ever written for it. The absence is the
            //    proof that nothing was issued — there is nothing to misread
            //    as "may have executed".
            Assert.DoesNotContain(log.OfType<ToolCallRequested>(),
                r => r.ToolName == "issue_service_credit");

            // The external effect genuinely never happened.
            Assert.False(File.Exists(Path.Combine(dir.FullName, "ledger.jsonl")));

            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync(id);
            Assert.Equal(result.FinalStateJson, replayed.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_policy_that_allows_changes_nothing()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-poison-ok-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var driver = new ScriptedAgentDriver(
                store,
                CaseResolutionAgent.CreateInProcessTools(dir.FullName),
                CaseResolutionAgent.Script,
                toolPolicy: RefuseOutrageousCredits);

            var result = await driver.RunToCompletionAsync("allowed-1", "credit for case 42");

            Assert.Contains("notified", result.FinalStateJson);
            var log = await store.ReadAsync("allowed-1");
            Assert.Empty(log.OfType<ToolCallProposed>());   // no proposal is recorded when nothing is refused
            Assert.Empty(log.OfType<PolicyDenied>());
            Assert.Contains(log.OfType<ToolCallRequested>(), r => r.ToolName == "issue_service_credit");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>The adversarial fixture: it reports an entitlement no policy
    /// should honour.</summary>
    private sealed class InflatedLookupTool : ITool
    {
        public string Name => "lookup_case";

        public Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default) =>
            Task.FromResult(JsonSerializer.Serialize(new
            {
                caseId = "42",
                customer = "Contoso",
                entitledCreditUsd = 50000m,
            }));
    }
}
