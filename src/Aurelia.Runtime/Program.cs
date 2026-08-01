// Provisional host wiring:
// single-silo Orleans on dynamic localhost ports, one session per invocation.
using Aurelia.Core;
using Aurelia.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orleans.Configuration;
using System.Net;
using System.Net.Sockets;

string? sessionId = null, goal = null, storeDir = null;
var agent = "echo";
for (var i = 1; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--session-id": sessionId = args[++i]; break;
        case "--goal": goal = args[++i]; break;
        case "--store-dir": storeDir = args[++i]; break;
        case "--agent": agent = args[++i]; break;
    }
}

// Replay needs no host: reconstruction is a pure fold over the log (ADR-0004).
if (args.Length > 0 && args[0] == "replay")
{
    if (sessionId is null || storeDir is null)
    {
        Console.Error.WriteLine("usage: Aurelia.Runtime replay --session-id <id> --store-dir <path>");
        return 2;
    }
    var replayEngine = new ReconstructionReplayEngine(new FileEventStore(storeDir));
    var replayed = await replayEngine.ReplayAsync(sessionId);
    Console.WriteLine($"REPLAYED_FINAL_STATE {replayed.FinalStateJson}");
    return 0;
}

if (args.Length == 0 || args[0] != "run" || sessionId is null || goal is null || storeDir is null)
{
    Console.Error.WriteLine("usage: Aurelia.Runtime run --session-id <id> --goal <text> --store-dir <path> [--agent echo|case-resolution]");
    Console.Error.WriteLine("       Aurelia.Runtime replay --session-id <id> --store-dir <path>");
    return 2;
}

var (siloPort, gatewayPort) = GetFreePortPair();

using var host = new HostBuilder()
    .UseOrleans(silo => silo
        .UseLocalhostClustering(siloPort, gatewayPort)
        .Configure<SiloMessagingOptions>(o => o.ResponseTimeout = TimeSpan.FromMinutes(5)))
    .ConfigureLogging(logging => logging.AddConsole().SetMinimumLevel(LogLevel.Warning))
    .ConfigureServices(services =>
    {
        var eventStore = new FileEventStore(storeDir);
        services.AddSingleton<IEventStore>(eventStore);
        services.AddSingleton<ISessionRuntime>(agent switch
        {
            "case-resolution" => CaseResolutionAgent.CreateDriver(eventStore, storeDir,
                failNotifications: Environment.GetEnvironmentVariable("AURELIA_FAIL_NOTIFY") == "1"),
            _ => new ScriptedAgentDriver(eventStore,
                new Dictionary<string, ITool> { ["echo"] = new BlockableEchoTool() },
                [new ScriptStep("echo", (sessionGoal, _) => sessionGoal)]),
        });

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("aurelia-runtime"))
            .WithTracing(tracing =>
            {
                tracing.AddSource(AureliaDiagnostics.SourceName);
                // Standard OTLP env vars drive the exporter; only attach it
                // when an endpoint is configured so local runs stay quiet.
                if (Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") is not null)
                    tracing.AddOtlpExporter();
                if (Environment.GetEnvironmentVariable("AURELIA_TRACE_CONSOLE") == "1")
                    tracing.AddConsoleExporter();
            });
    })
    .Build();

await host.StartAsync();

var session = host.Services.GetRequiredService<IGrainFactory>().GetGrain<IAgentSessionGrain>(sessionId);
var finalState = await session.RunToCompletionAsync(goal);
Console.WriteLine($"FINAL_STATE {finalState}");

await host.StopAsync();
return 0;

// Both listeners stay open while the ports are read so the pair is distinct.
static (int SiloPort, int GatewayPort) GetFreePortPair()
{
    var first = new TcpListener(IPAddress.Loopback, 0);
    var second = new TcpListener(IPAddress.Loopback, 0);
    first.Start();
    second.Start();
    var ports = (((IPEndPoint)first.LocalEndpoint).Port, ((IPEndPoint)second.LocalEndpoint).Port);
    first.Stop();
    second.Stop();
    return ports;
}
