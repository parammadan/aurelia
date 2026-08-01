// Naive JSONL event store so the crash harness has durable state to resume
// from. The production store is Cosmos DB (its own design round); the schema
// persisted here is Core's.
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public sealed class FileEventStore(string directory, EventUpcasterRegistry? upcasters = null) : IEventStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, Type> EventTypes = new()
    {
        [nameof(SessionStarted)] = typeof(SessionStarted),
        [nameof(UserMessageReceived)] = typeof(UserMessageReceived),
        [nameof(AgentMessageSent)] = typeof(AgentMessageSent),
        [nameof(SessionCompleted)] = typeof(SessionCompleted),
        [nameof(SessionFailed)] = typeof(SessionFailed),
        [nameof(ToolCallRequested)] = typeof(ToolCallRequested),
        [nameof(CompensationRequested)] = typeof(CompensationRequested),
        [nameof(ModelCallRequested)] = typeof(ModelCallRequested),
        [nameof(ApprovalRequested)] = typeof(ApprovalRequested),
        [nameof(TimeRequested)] = typeof(TimeRequested),
        [nameof(RandomRequested)] = typeof(RandomRequested),
        [nameof(ToolCallSucceeded)] = typeof(ToolCallSucceeded),
        [nameof(ModelCallSucceeded)] = typeof(ModelCallSucceeded),
        [nameof(ApprovalResolved)] = typeof(ApprovalResolved),
        [nameof(ApprovalExpired)] = typeof(ApprovalExpired),
        [nameof(TimeProvided)] = typeof(TimeProvided),
        [nameof(RandomProvided)] = typeof(RandomProvided),
        [nameof(CallAttemptFailed)] = typeof(CallAttemptFailed),
        [nameof(CallFailed)] = typeof(CallFailed),
    };

    private readonly EventUpcasterRegistry _upcasters = upcasters ?? new EventUpcasterRegistry();
    private readonly object _gate = new();

    public Task<long> AppendAsync(SessionEvent evt, CancellationToken ct = default)
    {
        var envelope = new Envelope(evt.GetType().Name, JsonSerializer.SerializeToElement(evt, evt.GetType(), Json));
        var line = JsonSerializer.Serialize(envelope, Json) + Environment.NewLine;
        lock (_gate)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(PathFor(evt.SessionId), line);
        }
        return Task.FromResult(evt.Sequence);
    }

    public Task<IReadOnlyList<SessionEvent>> ReadAsync(string sessionId, CancellationToken ct = default)
    {
        var path = PathFor(sessionId);
        if (!File.Exists(path))
            return Task.FromResult<IReadOnlyList<SessionEvent>>([]);

        string[] lines;
        lock (_gate)
            lines = File.ReadAllLines(path);

        var events = new List<SessionEvent>(lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            try
            {
                events.Add(Deserialize(lines[i]));
            }
            catch (JsonException) when (i == lines.Length - 1)
            {
                // A torn final line is expected after a hard kill mid-append;
                // the event was never durable, so recovery ignores it.
            }
        }
        return Task.FromResult<IReadOnlyList<SessionEvent>>(events);
    }

    private string PathFor(string sessionId) => Path.Combine(directory, $"{sessionId}.jsonl");

    private SessionEvent Deserialize(string line)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(line, Json)
            ?? throw new JsonException("Null event envelope.");
        if (!EventTypes.TryGetValue(envelope.Type, out var type))
            throw new JsonException($"Unknown event type '{envelope.Type}'.");

        var payload = _upcasters.Upcast(envelope.Type, envelope.Data);
        return (SessionEvent)(payload.Deserialize(type, Json)
            ?? throw new JsonException($"Null payload for event type '{envelope.Type}'."));
    }

    private sealed record Envelope(string Type, JsonElement Data);
}
