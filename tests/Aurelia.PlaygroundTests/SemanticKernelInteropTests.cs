// Both interop directions: an SK function running as a durable Aurelia tool
// (full two-phase events), and an Aurelia agent invoked as a KernelFunction.
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using Aurelia.SemanticKernel;
using Microsoft.SemanticKernel;
using Xunit;

namespace Aurelia.PlaygroundTests;

public class SemanticKernelInteropTests
{
    [Fact]
    public async Task Kernel_function_runs_as_a_durable_two_phase_tool()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-sk-");
        try
        {
            var kernel = Kernel.CreateBuilder().Build();
            var shout = KernelFunctionFactory.CreateFromMethod(
                (string text) => text.ToUpperInvariant(), functionName: "shout");

            var store = new FileEventStore(dir.FullName);
            var driver = new ScriptedAgentDriver(store,
                new Dictionary<string, ITool> { ["shout"] = shout.AsAureliaTool(kernel) },
                [new ScriptStep("shout", ctx => JsonSerializer.Serialize(new { text = ctx.UserMessage }))]);

            var result = await driver.RunToCompletionAsync("sk-session", "hello kernel");

            Assert.Contains("HELLO KERNEL", result.FinalStateJson);
            var log = await store.ReadAsync("sk-session");
            var intent = Assert.Single(log.OfType<ToolCallRequested>());
            Assert.Equal("shout", intent.ToolName);
            Assert.Single(log.OfType<ToolCallSucceeded>()); // two-phase, like any tool

            // Rerun substitutes the recorded outcome — the SK function is not re-invoked.
            var again = await driver.RunToCompletionAsync("sk-session", "hello kernel");
            Assert.Equal(result.FinalStateJson, again.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Aurelia_agent_is_invocable_as_a_kernel_function()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-sk-agent-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var agent = CaseResolutionAgent.CreateDriver(store, dir.FullName);
            var kernel = Kernel.CreateBuilder().Build();
            var fn = agent.AsKernelFunction("resolve_case", "Durably resolve a customer-service case");

            var result = await fn.InvokeAsync(kernel, new KernelArguments
            {
                ["goal"] = "credit for case 42",
                ["sessionId"] = "sk-agent-1",
            });

            using var doc = JsonDocument.Parse(result.ToString());
            Assert.Equal("sk-agent-1", doc.RootElement.GetProperty("sessionId").GetString());
            Assert.Contains("\"notified\":true", doc.RootElement.GetProperty("state").GetString());

            // The durable session exists independently of the SK caller.
            var log = await store.ReadAsync("sk-agent-1");
            Assert.Single(log.OfType<SessionCompleted>());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
