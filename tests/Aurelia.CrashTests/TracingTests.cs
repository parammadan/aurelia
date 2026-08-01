// PERIMETER — verifies the roadmap's "OTel from the first commit" rule: a
// session run emits a session span with a child tool span.
using System.Diagnostics;
using Aurelia.Runtime;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Aurelia.CrashTests;

public class TracingTests
{
    [Fact]
    public async Task Session_run_emits_session_and_tool_spans()
    {
        var exported = new List<Activity>();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(AureliaDiagnostics.SourceName)
            .AddInMemoryExporter(exported)
            .Build();

        var storeDir = Directory.CreateTempSubdirectory("aurelia-trace-");
        try
        {
            var driver = new ProvisionalSessionDriver(
                new FileEventStore(storeDir.FullName), new BlockableEchoTool());
            await driver.RunToCompletionAsync("trace-session", "hello");
            tracerProvider.ForceFlush();

            // Filter by session id: the ActivitySource is global, so spans
            // from other tests running in parallel land in the same exporter.
            Activity[] snapshot;
            lock (exported)
                snapshot = [.. exported];
            var sessionSpan = Assert.Single(snapshot, a => a.OperationName == "session.run"
                && (string?)a.GetTagItem("aurelia.session.id") == "trace-session");

            var toolSpan = Assert.Single(snapshot, a => a.OperationName == "execute_tool"
                && a.ParentSpanId == sessionSpan.SpanId);
            Assert.Equal("echo", toolSpan.GetTagItem("gen_ai.tool.name"));
        }
        finally
        {
            storeDir.Delete(recursive: true);
        }
    }
}
