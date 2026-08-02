// The harness-owned tool server (benchmark methodology §4, harness.md): the
// task's tools and ledger live HERE, behind HTTP, so fault injection is
// physical — this server errors, stalls, or blocks mid-effect — and the
// framework under test just sees a misbehaving dependency. Faults and the
// kill-window signal protocol are configured on THIS process (AURELIA_CHAOS,
// AURELIA_SIGNAL_DIR, AURELIA_SIGNAL_AFTER_EFFECT, AURELIA_FAIL_NOTIFY);
// workers get none of it.
using Aurelia.Core;
using Aurelia.Runtime;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

string? dataDir = null;
for (var i = 0; i < args.Length; i++)
    if (args[i] == "--data-dir" && i + 1 < args.Length)
        dataDir = args[++i];
if (dataDir is null)
{
    Console.Error.WriteLine("usage: Aurelia.ToolServer --data-dir <path>");
    return 2;
}
Directory.CreateDirectory(dataDir);

// The real tool implementations run inside the harness's process; the chaos
// wrapper and signal protocol act here, at the dependency, not in the client.
var chaosSpec = Environment.GetEnvironmentVariable("AURELIA_CHAOS");
var tools = CaseResolutionAgent.CreateInProcessTools(dataDir,
    Environment.GetEnvironmentVariable("AURELIA_FAIL_NOTIFY") == "1");
if (chaosSpec is not null)
    tools = new ChaosSource(ChaosPolicy.Parse(chaosSpec)).WrapTools(tools);

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();
app.Urls.Add("http://127.0.0.1:0"); // dynamic port, announced on stdout

app.MapPost("/tools/{name}", async (string name, ToolCall call) =>
{
    if (!tools.TryGetValue(name, out var tool))
        return Results.NotFound($"no tool '{name}'");
    try
    {
        var result = await tool.ExecuteAsync(call.ArgumentsJson, call.IdempotencyKey);
        return Results.Json(new { resultJson = result });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

app.MapPost("/tools/{name}/compensate", async (string name, CompensationCall call) =>
{
    if (!tools.TryGetValue(name, out var tool) || tool is not ICompensatableTool compensatable)
        return Results.NotFound($"no compensatable tool '{name}'");
    try
    {
        var result = await compensatable.CompensateAsync(
            call.OriginalArgumentsJson, call.OriginalResultJson, call.CompensationKey);
        return Results.Json(new { resultJson = result });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>()
    .Features.Get<IServerAddressesFeature>()!.Addresses.First();
Console.WriteLine($"LISTENING {address}");
await app.WaitForShutdownAsync();
return 0;

record ToolCall(string ArgumentsJson, string IdempotencyKey);
record CompensationCall(string OriginalArgumentsJson, string OriginalResultJson, string CompensationKey);
