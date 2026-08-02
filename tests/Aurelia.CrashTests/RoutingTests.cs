// Model routing (ADRs 0020-0022): the intent records the routing decision at
// schema v2, old v1 events upcast on read, and a latency-budget breach falls
// back to the secondary backend under the same key — with the outcome
// recording who actually served.
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class RoutingTests
{
    [Fact]
    public async Task Intent_records_the_routing_decision_at_schema_v2()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-route-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var fake = new FakeModelClient();
            var driver = CaseResolutionAgent.CreateTriageDriver(store, dir.FullName, fake);
            await driver.RunToCompletionAsync("route-meta", "credit case 42");

            var log = await store.ReadAsync("route-meta");
            var intent = Assert.Single(log.OfType<ModelCallRequested>());
            Assert.Equal(2, intent.SchemaVersion);
            Assert.Equal("classify", intent.RouteClass);
            Assert.Equal("triage-v1", intent.RoutingPolicy);
            Assert.Contains(fake.ModelId, intent.RoutingReason);

            var outcome = Assert.Single(log.OfType<ModelCallSucceeded>());
            Assert.Equal(2, outcome.SchemaVersion);
            Assert.Equal(fake.ModelId, outcome.ModelId);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task V1_model_events_upcast_to_v2_on_read()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-route-upcast-");
        try
        {
            const string v1Line = """
                {"type":"ModelCallRequested","data":{"sessionId":"old","sequence":1,"turnOrdinal":0,"stepOrdinal":0,"schemaVersion":1,"recordedAt":"2026-08-01T00:00:00+00:00","modelId":"m","promptJson":"[]","parametersJson":"{}","configVersion":"v"}}
                """;
            File.WriteAllText(Path.Combine(dir.FullName, "old.jsonl"), v1Line + Environment.NewLine);

            var store = new FileEventStore(dir.FullName); // standard upcasters by default
            var evt = Assert.IsType<ModelCallRequested>(Assert.Single(await store.ReadAsync("old")));
            Assert.Equal(2, evt.SchemaVersion);
            Assert.Null(evt.RouteClass);
            Assert.Equal("m", evt.ModelId);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Budget_breach_falls_back_and_outcome_records_who_served()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-route-budget-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var slow = new SlowModelClient(delayMs: 10_000);
            var fallback = new FakeModelClient();
            var policy = new ModelRoutingPolicy("budget-test", new Dictionary<string, ModelRoute>
            {
                ["classify"] = new(slow, fallback, TimeSpan.FromMilliseconds(150)),
                ["general"] = new(slow),
            });
            var driver = new ScriptedAgentDriver(store,
                CaseResolutionAgent.CreateTools(dir.FullName),
                CaseResolutionAgent.TriageScript, policy);

            var result = await driver.RunToCompletionAsync("route-budget", "credit case 42");

            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.True(final.RootElement.GetProperty("notified").GetBoolean());

            var log = await store.ReadAsync("route-budget");
            var intent = Assert.Single(log.OfType<ModelCallRequested>());
            Assert.Equal(slow.ModelId, intent.ModelId); // routed choice

            var breach = Assert.Single(log.OfType<CallAttemptFailed>(),
                f => f.IdempotencyKey == intent.IdempotencyKey);
            Assert.Contains("latency budget exceeded", breach.Error);

            var outcome = Assert.Single(log.OfType<ModelCallSucceeded>());
            Assert.Equal(2, outcome.Attempt);
            Assert.Equal(fallback.ModelId, outcome.ModelId); // who actually served
            Assert.NotEqual(intent.ModelId, outcome.ModelId);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Budget_breach_without_fallback_is_terminal_and_unwinds()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-route-dead-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var slow = new SlowModelClient(delayMs: 10_000);
            var policy = new ModelRoutingPolicy("no-fallback", new Dictionary<string, ModelRoute>
            {
                ["classify"] = new(slow, Fallback: null, TimeSpan.FromMilliseconds(100)),
                ["general"] = new(slow),
            });
            var driver = new ScriptedAgentDriver(store,
                CaseResolutionAgent.CreateTools(dir.FullName),
                CaseResolutionAgent.TriageScript, policy);

            var result = await driver.RunToCompletionAsync("route-dead", "credit case 42");

            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.Equal("failed", final.RootElement.GetProperty("status").GetString());
            Assert.Contains("latency budget exceeded", final.RootElement.GetProperty("reason").GetString());
            Assert.False(File.Exists(Path.Combine(dir.FullName, "ledger.jsonl")));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private sealed class SlowModelClient(int delayMs) : IModelClient
    {
        public string ModelId => "slow-primary";
        public string ParametersJson => "{}";
        public string ConfigVersion => "slow-v1";

        public async Task<string> CompleteAsync(string promptJson, CancellationToken ct = default)
        {
            await Task.Delay(delayMs, ct);
            return """{"caseId":"42"}""";
        }
    }
}
