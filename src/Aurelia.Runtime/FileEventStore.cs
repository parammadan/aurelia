// PERIMETER — AI-generated scaffolding (ADR-0003). Naive JSONL event store so
// the crash harness has durable state to resume from. The production store
// (Cosmos DB) is perimeter too, but the schema it persists is core design work.
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public sealed class FileEventStore(string directory) : IEventStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
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

    private static SessionEvent Deserialize(string line)
    {
        var envelope = JsonSerializer.Deserialize<Envelope>(line, Json)
            ?? throw new JsonException("Null event envelope.");
        var type = envelope.Type switch
        {
            nameof(SessionStarted) => typeof(SessionStarted),
            nameof(ToolCallRequested) => typeof(ToolCallRequested),
            nameof(ToolCallCompleted) => typeof(ToolCallCompleted),
            nameof(SessionCompleted) => typeof(SessionCompleted),
            _ => throw new JsonException($"Unknown event type '{envelope.Type}'."),
        };
        return (SessionEvent)(envelope.Data.Deserialize(type, Json)
            ?? throw new JsonException($"Null payload for event type '{envelope.Type}'."));
    }

    private sealed record Envelope(string Type, JsonElement Data);
}
