// Verifies ADR-0009: a stored event written in an old shape is lifted to the
// current shape by a registered upcaster on read.
using System.Text.Json;
using System.Text.Json.Nodes;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class UpcasterTests
{
    [Fact]
    public async Task Old_event_shape_is_upcast_on_read()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-upcast-");
        try
        {
            const string sessionId = "upcast-session";

            // A hypothetical v0 ToolCallRequested: no schemaVersion field and
            // the arguments under an old property name.
            const string v0Line = """
                {"type":"ToolCallRequested","data":{"sessionId":"upcast-session","sequence":1,"turnOrdinal":0,"stepOrdinal":0,"recordedAt":"2026-08-01T00:00:00+00:00","toolName":"echo","args":"hello"}}
                """;
            File.WriteAllText(Path.Combine(dir.FullName, $"{sessionId}.jsonl"), v0Line + Environment.NewLine);

            var upcasters = new EventUpcasterRegistry()
                .Register(nameof(ToolCallRequested), fromVersion: 0, payload =>
                {
                    var node = JsonNode.Parse(payload.GetRawText())!.AsObject();
                    node["argumentsJson"] = node["args"]!.GetValue<string>();
                    node.Remove("args");
                    node["schemaVersion"] = 1;
                    return JsonSerializer.SerializeToElement(node);
                });

            var store = new FileEventStore(dir.FullName, upcasters);
            var events = await store.ReadAsync(sessionId);

            var evt = Assert.IsType<ToolCallRequested>(Assert.Single(events));
            Assert.Equal("hello", evt.ArgumentsJson);
            Assert.Equal(1, evt.SchemaVersion);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Upcaster_that_does_not_bump_version_is_rejected()
    {
        var registry = new EventUpcasterRegistry()
            .Register(nameof(ToolCallRequested), fromVersion: 0, payload => payload);

        var broken = JsonSerializer.SerializeToElement(new { toolName = "echo" });
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Task.FromResult(registry.Upcast(nameof(ToolCallRequested), broken)));
    }
}
