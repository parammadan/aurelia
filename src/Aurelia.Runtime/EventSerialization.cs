// Shared event (de)serialization for all store implementations: one type map,
// one payload shape, one upcaster hook (ADR-0009) — so the file store and the
// Cosmos store can never disagree about what an event looks like.
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public static class EventSerialization
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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

    public static string TypeName(SessionEvent evt) => evt.GetType().Name;

    public static JsonElement Payload(SessionEvent evt) =>
        JsonSerializer.SerializeToElement(evt, evt.GetType(), Json);

    public static SessionEvent Deserialize(string typeName, JsonElement payload, EventUpcasterRegistry upcasters)
    {
        if (!EventTypes.TryGetValue(typeName, out var type))
            throw new JsonException($"Unknown event type '{typeName}'.");
        var lifted = upcasters.Upcast(typeName, payload);
        return (SessionEvent)(lifted.Deserialize(type, Json)
            ?? throw new JsonException($"Null payload for event type '{typeName}'."));
    }
}

public static class SessionIds
{
    public static bool IsValid(string sessionId) =>
        sessionId.Length is > 0 and <= 128
        && sessionId.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    public static void Validate(string sessionId)
    {
        if (!IsValid(sessionId))
            throw new ArgumentException(
                $"invalid session id '{sessionId}': letters, digits, '.', '_', '-' only", nameof(sessionId));
    }
}
