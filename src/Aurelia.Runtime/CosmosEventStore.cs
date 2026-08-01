// Cosmos DB event store (ADR-0019). One logical partition per session
// (ADR-0007); contiguity (ADR-0018) is enforced by a per-session "head"
// document advanced with an ETag-conditional replace in the same
// transactional batch that creates the event — a stale writer's batch fails
// atomically. Stream APIs throughout so serialization is exactly
// EventSerialization's, not the SDK's.
using System.Net;
using System.Text.Json;
using Aurelia.Core;
using Microsoft.Azure.Cosmos;

namespace Aurelia.Runtime;

public sealed class CosmosEventStore(Container container, EventUpcasterRegistry? upcasters = null) : IEventStore
{
    private readonly EventUpcasterRegistry _upcasters = upcasters ?? new EventUpcasterRegistry();

    public async Task<long> AppendAsync(SessionEvent evt, CancellationToken ct = default)
    {
        SessionIds.Validate(evt.SessionId);
        var pk = new PartitionKey(evt.SessionId);

        var (lastSequence, headEtag) = await ReadHeadAsync(evt.SessionId, pk, ct);
        if (evt.Sequence != lastSequence + 1)
            throw new SequenceConflictException(evt.SessionId, evt.Sequence, lastSequence);

        var head = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = "head",
            sessionId = evt.SessionId,
            lastSequence = evt.Sequence,
        });
        var item = JsonSerializer.SerializeToUtf8Bytes(new
        {
            id = evt.Sequence.ToString("D12"),
            sessionId = evt.SessionId,
            sequence = evt.Sequence,
            type = EventSerialization.TypeName(evt),
            data = EventSerialization.Payload(evt),
        });

        var batch = container.CreateTransactionalBatch(pk);
        if (headEtag is null)
            batch.CreateItemStream(new MemoryStream(head));
        else
            batch.ReplaceItemStream("head", new MemoryStream(head),
                new TransactionalBatchItemRequestOptions { IfMatchEtag = headEtag });
        batch.CreateItemStream(new MemoryStream(item));

        using var response = await batch.ExecuteAsync(ct);
        if (response.IsSuccessStatusCode)
            return evt.Sequence;
        if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
        {
            // Another writer advanced the head between our read and our batch.
            var (current, _) = await ReadHeadAsync(evt.SessionId, pk, ct);
            throw new SequenceConflictException(evt.SessionId, evt.Sequence, current);
        }
        throw new InvalidOperationException(
            $"Cosmos batch append failed for session '{evt.SessionId}': {response.StatusCode} {response.ErrorMessage}");
    }

    public async Task<IReadOnlyList<SessionEvent>> ReadAsync(string sessionId, CancellationToken ct = default)
    {
        SessionIds.Validate(sessionId);
        var events = new List<SessionEvent>();
        var query = new QueryDefinition("SELECT c.type, c.data FROM c WHERE c.id != 'head' ORDER BY c.sequence");
        using var iterator = container.GetItemQueryStreamIterator(query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(sessionId) });

        while (iterator.HasMoreResults)
        {
            using var page = await iterator.ReadNextAsync(ct);
            if (!page.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Cosmos query failed for session '{sessionId}': {page.StatusCode} {page.ErrorMessage}");
            using var doc = await JsonDocument.ParseAsync(page.Content, cancellationToken: ct);
            foreach (var item in doc.RootElement.GetProperty("Documents").EnumerateArray())
                events.Add(EventSerialization.Deserialize(
                    item.GetProperty("type").GetString()!,
                    item.GetProperty("data").Clone(),
                    _upcasters));
        }
        return events;
    }

    private async Task<(long LastSequence, string? ETag)> ReadHeadAsync(
        string sessionId, PartitionKey pk, CancellationToken ct)
    {
        using var response = await container.ReadItemStreamAsync("head", pk, cancellationToken: ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return (0, null);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Cosmos head read failed for session '{sessionId}': {response.StatusCode} {response.ErrorMessage}");
        using var doc = await JsonDocument.ParseAsync(response.Content, cancellationToken: ct);
        return (doc.RootElement.GetProperty("lastSequence").GetInt64(), response.Headers.ETag);
    }
}
