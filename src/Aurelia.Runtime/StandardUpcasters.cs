// The standard upcaster registry (ADR-0009): every store uses this unless a
// test injects its own. One entry per historical schema bump, never removed.
using System.Text.Json;
using System.Text.Json.Nodes;
using Aurelia.Core;

namespace Aurelia.Runtime;

public static class StandardUpcasters
{
    public static EventUpcasterRegistry Create() => new EventUpcasterRegistry()
        // v1 → v2 (ADR-0020): routing fields added, nullable — old events just
        // acquire the new version; absent fields deserialize as null.
        .Register(nameof(ModelCallRequested), fromVersion: 1, BumpTo(2))
        .Register(nameof(ModelCallRequested), fromVersion: 0, BumpTo(2))
        .Register(nameof(ModelCallSucceeded), fromVersion: 1, BumpTo(2))
        .Register(nameof(ModelCallSucceeded), fromVersion: 0, BumpTo(2))
        // v2 → v3: token counts added, nullable (chained after 0/1 → 2).
        .Register(nameof(ModelCallSucceeded), fromVersion: 2, BumpTo(3))
        // v1 → v2 (ADR-0042): SessionFailed gained UnrecoverableKeys. Absent
        // deserializes as an empty list, which is correct for a session that
        // recorded no such effect.
        .Register(nameof(SessionFailed), fromVersion: 1, BumpTo(2))
        .Register(nameof(SessionFailed), fromVersion: 0, BumpTo(2));

    private static Func<JsonElement, JsonElement> BumpTo(int version) => payload =>
    {
        var node = JsonNode.Parse(payload.GetRawText())!.AsObject();
        node["schemaVersion"] = version;
        return JsonSerializer.SerializeToElement(node);
    };
}
