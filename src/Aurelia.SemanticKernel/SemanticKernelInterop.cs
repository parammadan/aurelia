// Semantic Kernel interop (Phase 2 mandate), both directions:
//
//  SK → Aurelia: any KernelFunction becomes a durable Aurelia tool — its
//  executions get the full two-phase treatment (intent recorded before
//  execution, keyed retries, replayable outcomes) like any native tool.
//
//  Aurelia → SK: any Aurelia agent becomes a KernelFunction an SK-based app
//  (or the Microsoft Agent Framework, which shares the SDK surface) can
//  invoke — each invocation is a durable session that survives the caller.
using System.ComponentModel;
using System.Text.Json;
using Aurelia.Core;
using Microsoft.SemanticKernel;

namespace Aurelia.SemanticKernel;

public static class SemanticKernelInterop
{
    /// <summary>Wraps a Semantic Kernel function as an Aurelia tool. Arguments
    /// arrive as the tool's JSON arguments object; each property becomes a
    /// kernel argument.</summary>
    public static ITool AsAureliaTool(this KernelFunction function, Kernel kernel, string? name = null) =>
        new KernelFunctionTool(kernel, function, name ?? function.Name);

    /// <summary>Exposes an Aurelia agent as a Semantic Kernel function:
    /// invoking it runs a durable session (new or resumed by id) and returns
    /// the final state. The session outlives the SK caller — crash recovery,
    /// approvals, and sagas all apply.</summary>
    public static KernelFunction AsKernelFunction(this ISessionRuntime agent, string name, string description) =>
        KernelFunctionFactory.CreateFromMethod(
            async ([Description("The goal for the agent")] string goal,
                   [Description("Session id; reuse to resume a durable session")] string? sessionId) =>
            {
                sessionId ??= $"sk-{Guid.NewGuid():N}";
                var result = await agent.RunToCompletionAsync(sessionId, goal);
                return JsonSerializer.Serialize(new { sessionId = result.SessionId, state = result.FinalStateJson });
            },
            functionName: name,
            description: description);

    private sealed class KernelFunctionTool(Kernel kernel, KernelFunction function, string name) : ITool
    {
        public string Name => name;

        public async Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default)
        {
            var arguments = new KernelArguments();
            using (var doc = JsonDocument.Parse(argumentsJson))
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var property in doc.RootElement.EnumerateObject())
                        arguments[property.Name] = property.Value.ValueKind == JsonValueKind.String
                            ? property.Value.GetString()
                            : property.Value.GetRawText();
                else
                    arguments["input"] = argumentsJson;
            }

            var result = await function.InvokeAsync(kernel, arguments, ct);
            return JsonSerializer.Serialize(new { result = result.ToString() });
        }
    }
}
