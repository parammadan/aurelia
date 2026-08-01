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

            var sessionSpan = Assert.Single(exported, a => a.OperationName == "session.run");
            Assert.Equal("trace-session", sessionSpan.GetTagItem("aurelia.session.id"));

            var toolSpan = Assert.Single(exported, a => a.OperationName == "execute_tool");
            Assert.Equal("echo", toolSpan.GetTagItem("gen_ai.tool.name"));
            Assert.Equal(sessionSpan.SpanId, toolSpan.ParentSpanId);
        }
        finally
        {
            storeDir.Delete(recursive: true);
        }
    }
}
