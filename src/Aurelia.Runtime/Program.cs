// PERIMETER — AI-generated scaffolding (ADR-0003). Provisional host wiring:
// single-silo Orleans on dynamic localhost ports, one session per invocation.
using Aurelia.Core;
using Aurelia.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using System.Net;
using System.Net.Sockets;

string? sessionId = null, goal = null, storeDir = null;
for (var i = 1; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--session-id": sessionId = args[++i]; break;
        case "--goal": goal = args[++i]; break;
        case "--store-dir": storeDir = args[++i]; break;
    }
}

if (args.Length == 0 || args[0] != "run" || sessionId is null || goal is null || storeDir is null)
{
    Console.Error.WriteLine("usage: Aurelia.Runtime run --session-id <id> --goal <text> --store-dir <path>");
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
        services.AddSingleton<IEventStore>(new FileEventStore(storeDir));
        services.AddSingleton<ITool, BlockableEchoTool>();
        services.AddSingleton<ISessionRuntime, ProvisionalSessionDriver>();
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
