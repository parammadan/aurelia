// JSONL event store. Appends are atomic and contiguity-checked (ADR-0018):
// the file is opened exclusively (cross-process), a torn unterminated tail —
// never durable — is truncated, and the event lands only if its sequence is
// exactly last + 1. The production store is Cosmos DB (its own design round);
// the schema persisted here is Core's.
using System.Text;
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public sealed class FileEventStore(string directory, EventUpcasterRegistry? upcasters = null) : IEventStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);
    private const int LockRetries = 60; // ~3 s of contention tolerance

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

    public static bool IsValidSessionId(string sessionId) =>
        sessionId.Length is > 0 and <= 128
        && sessionId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    public async Task<long> AppendAsync(SessionEvent evt, CancellationToken ct = default)
    {
        var envelope = new Envelope(evt.GetType().Name, JsonSerializer.SerializeToElement(evt, evt.GetType(), Json));
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, Json) + "\n");
        Directory.CreateDirectory(directory);

        using var stream = await OpenWithRetryAsync(
            PathFor(evt.SessionId), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, ct);

        var (lastSequence, durableLength) = ScanTail(stream);
        if (evt.Sequence != lastSequence + 1)
            throw new SequenceConflictException(evt.SessionId, evt.Sequence, lastSequence);

        // A torn, unterminated tail was never durable (ADR-0018); truncate it
        // so the new event starts on its own line.
        if (stream.Length > durableLength)
            stream.SetLength(durableLength);

        stream.Seek(0, SeekOrigin.End);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
        return evt.Sequence;
    }

    public async Task<IReadOnlyList<SessionEvent>> ReadAsync(string sessionId, CancellationToken ct = default)
    {
        var path = PathFor(sessionId);
        if (!File.Exists(path))
            return [];

        string[] lines;
        using (var stream = await OpenWithRetryAsync(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ct))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            lines = (await reader.ReadToEndAsync(ct)).Split('\n');

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
        return events;
    }

    private string PathFor(string sessionId) =>
        IsValidSessionId(sessionId)
            ? Path.Combine(directory, $"{sessionId}.jsonl")
            : throw new ArgumentException(
                $"invalid session id '{sessionId}': letters, digits, '.', '_', '-' only", nameof(sessionId));

    // Appends are short-lived, so contention resolves in milliseconds; the
    // retry loop rides out another process holding the exclusive lock.
    private static async Task<FileStream> OpenWithRetryAsync(
        string path, FileMode mode, FileAccess access, FileShare share, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return new FileStream(path, mode, access, share);
            }
            catch (IOException) when (attempt < LockRetries)
            {
                await Task.Delay(LockRetryDelay, ct);
            }
        }
    }

    /// <summary>Returns the last durable sequence and the byte length of the
    /// durable region (everything up to and including the final newline).</summary>
    private static (long LastSequence, long DurableLength) ScanTail(FileStream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        var text = reader.ReadToEnd();

        var lastNewline = text.LastIndexOf('\n');
        var durable = lastNewline < 0 ? "" : text[..(lastNewline + 1)];
        var durableLength = Encoding.UTF8.GetByteCount(durable);

        var lines = durable.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            var envelope = JsonSerializer.Deserialize<Envelope>(lines[i], Json)
                ?? throw new JsonException("Null event envelope in log.");
            return (envelope.Data.GetProperty("sequence").GetInt64(), durableLength);
        }
        return (0, durableLength);
    }

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
