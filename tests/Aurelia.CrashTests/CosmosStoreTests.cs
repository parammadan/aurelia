// Integration test for the Cosmos event store (ADR-0019). Runs only where
// AURELIA_COSMOS_ENDPOINT is configured (locally, against the real account);
// in CI, where no Azure credentials exist, it no-ops by design — the contract
// itself is exercised continuously against the file store by
// StoreConcurrencyTests.
using Aurelia.Core;
using Aurelia.Runtime;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Aurelia.CrashTests;

public class CosmosStoreTests
{
    [Fact]
    public async Task Cosmos_store_honors_contiguity_and_round_trips_events_when_configured()
    {
        var endpoint = Environment.GetEnvironmentVariable("AURELIA_COSMOS_ENDPOINT");
        if (string.IsNullOrEmpty(endpoint))
            return; // not configured in this environment (e.g. CI)

        using var client = new CosmosClient(endpoint, EventStores.Credential());
        var container = client.GetContainer("aurelia", "events");
        var sessionId = $"itest-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(1000, 9999)}";

        var store = new CosmosEventStore(container);
        await store.AppendAsync(Started(sessionId, 1));

        // Contiguity: duplicates and gaps are rejected (ADR-0018).
        await Assert.ThrowsAsync<SequenceConflictException>(() => store.AppendAsync(Started(sessionId, 1)));
        await Assert.ThrowsAsync<SequenceConflictException>(() => store.AppendAsync(Message(sessionId, 3)));
        await store.AppendAsync(Message(sessionId, 2));

        // A second store instance (stand-in for another process) that folded a
        // stale view cannot append over the winner's position.
        var staleWriter = new CosmosEventStore(container);
        await staleWriter.AppendAsync(Message(sessionId, 3));
        await Assert.ThrowsAsync<SequenceConflictException>(() => store.AppendAsync(Message(sessionId, 3)));

        // Round trip: typed events, ordered, via the shared serialization.
        var log = await store.ReadAsync(sessionId);
        Assert.Equal([1L, 2L, 3L], log.Select(e => e.Sequence));
        Assert.IsType<SessionStarted>(log[0]);
        Assert.Equal("g", ((SessionStarted)log[0]).Goal);
        Assert.IsType<AgentMessageSent>(log[2]);
    }

    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static SessionStarted Started(string sessionId, long seq) => new()
    {
        SessionId = sessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = 0,
        RecordedAt = T0, Goal = "g",
    };

    private static AgentMessageSent Message(string sessionId, long seq) => new()
    {
        SessionId = sessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = 0,
        RecordedAt = T0, Content = "m",
    };
}
