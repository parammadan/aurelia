// Read-path schema evolution (ADR-0009): upcasters lift stored events to the
// current shape, chained one version at a time, so the fold, recovery, and
// replay only ever see the latest schema.
using System.Text.Json;

namespace Aurelia.Runtime;

public sealed class EventUpcasterRegistry
{
    private readonly Dictionary<(string EventType, int FromVersion), Func<JsonElement, JsonElement>> _upcasters = [];

    /// <summary>Registers a pure function lifting <paramref name="eventType"/>
    /// payloads from <paramref name="fromVersion"/> to the next version. The
    /// function must set the payload's schemaVersion to a higher value.</summary>
    public EventUpcasterRegistry Register(string eventType, int fromVersion, Func<JsonElement, JsonElement> upcast)
    {
        _upcasters.Add((eventType, fromVersion), upcast);
        return this;
    }

    public JsonElement Upcast(string eventType, JsonElement payload)
    {
        while (_upcasters.TryGetValue((eventType, VersionOf(payload)), out var upcast))
        {
            var before = VersionOf(payload);
            payload = upcast(payload);
            if (VersionOf(payload) <= before)
                throw new InvalidOperationException(
                    $"Upcaster for '{eventType}' v{before} did not increase schemaVersion.");
        }
        return payload;
    }

    // Payloads written before versioning existed carry no schemaVersion; they
    // are treated as version 0.
    private static int VersionOf(JsonElement payload) =>
        payload.TryGetProperty("schemaVersion", out var v) ? v.GetInt32() : 0;
}
