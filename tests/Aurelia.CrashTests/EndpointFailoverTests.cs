// Beat 9 (ADR-0046): the primary model endpoint fails mid-task and the
// session does NOT fail — the call reroutes to the fallback under the same
// key and completes. What makes it provable rather than narrated is the
// recorded served-by model id.
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class EndpointFailoverTests
{
    [Fact]
    public async Task A_rate_limited_primary_reroutes_to_the_fallback_and_completes()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-failover-429-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var primary = new FaultyModelClient(
                new FakeModelClient(), EndpointFault.RateLimited, failFirst: 1);
            var fallback = new FakeModelClient();

            var driver = CaseResolutionAgent.CreateTriageDriver(
                store, dir.FullName, primary, fallback: fallback);

            var result = await driver.RunToCompletionAsync("failover-429", "credit for case 42");

            // The session completed — a refused endpoint is not a failed task.
            Assert.Contains("notified", result.FinalStateJson);

            var log = await store.ReadAsync("failover-429");

            // The primary really was tried, and really refused.
            Assert.Equal(1, primary.Calls);
            var attemptFailed = Assert.Single(log.OfType<CallAttemptFailed>());
            Assert.Contains("429", attemptFailed.Error);

            // The success records WHICH backend served it — that is the proof
            // of the reroute, not the narration of it.
            var served = Assert.Single(log.OfType<ModelCallSucceeded>());
            Assert.Equal(fallback.ModelId, served.ModelId);
            Assert.Equal(2, served.Attempt); // same key, second attempt

            // The routing decision was recorded on the intent all along.
            var intent = Assert.Single(log.OfType<ModelCallRequested>());
            Assert.Equal("classify", intent.RouteClass);
            Assert.False(string.IsNullOrWhiteSpace(intent.RoutingReason));
            Assert.Equal(intent.IdempotencyKey, served.IdempotencyKey);

            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync("failover-429");
            Assert.Equal(result.FinalStateJson, replayed.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_latency_spike_past_the_budget_reroutes_the_same_way()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-failover-slow-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var primary = new FaultyModelClient(
                new FakeModelClient(), EndpointFault.LatencySpike,
                failFirst: 1, spike: TimeSpan.FromMilliseconds(600));
            var fallback = new FakeModelClient();

            // A 100ms budget on the primary: the spike breaches it, which is
            // an attempt failure, which falls back (ADR-0022).
            var driver = CaseResolutionAgent.CreateTriageDriver(
                store, dir.FullName, primary, fallback: fallback,
                budget: TimeSpan.FromMilliseconds(100));

            var result = await driver.RunToCompletionAsync("failover-slow", "credit for case 42");
            Assert.Contains("notified", result.FinalStateJson);

            var log = await store.ReadAsync("failover-slow");
            var attemptFailed = Assert.Single(log.OfType<CallAttemptFailed>());
            Assert.Contains("budget", attemptFailed.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(fallback.ModelId, Assert.Single(log.OfType<ModelCallSucceeded>()).ModelId);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
