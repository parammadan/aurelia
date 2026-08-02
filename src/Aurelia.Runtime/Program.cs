// Provisional host wiring:
// single-silo Orleans on dynamic localhost ports, one session per invocation.
using Aurelia.Core;
using Aurelia.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Orleans.Configuration;
using System.Net;
using System.Net.Sockets;

string? sessionId = null, goal = null, storeDir = null, suite = null, approvalKey = null, filter = null, chaosSpec = null, queue = null, messageId = null, message = null;
var agent = "echo";
var approver = "cli";
var deny = false;
var repeat = 1;
var maxMessages = 100;
for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--session-id" when i + 1 < args.Length: sessionId = args[++i]; break;
        case "--goal" when i + 1 < args.Length: goal = args[++i]; break;
        case "--store-dir" when i + 1 < args.Length: storeDir = args[++i]; break;
        case "--agent" when i + 1 < args.Length: agent = args[++i]; break;
        case "--suite" when i + 1 < args.Length: suite = args[++i]; break;
        case "--key" when i + 1 < args.Length: approvalKey = args[++i]; break;
        case "--approver" when i + 1 < args.Length: approver = args[++i]; break;
        case "--deny": deny = true; break;
        case "--repeat" when i + 1 < args.Length: _ = int.TryParse(args[++i], out repeat); break;
        case "--filter" when i + 1 < args.Length: filter = args[++i]; break;
        case "--chaos" when i + 1 < args.Length: chaosSpec = args[++i]; break;
        case "--queue" when i + 1 < args.Length: queue = args[++i]; break;
        case "--message-id" when i + 1 < args.Length: messageId = args[++i]; break;
        case "--max" when i + 1 < args.Length: _ = int.TryParse(args[++i], out maxMessages); break;
        case "--message" when i + 1 < args.Length: message = args[++i]; break;
    }
}

if (sessionId is not null && !SessionIds.IsValid(sessionId))
{
    Console.Error.WriteLine($"invalid session id '{sessionId}': letters, digits, '.', '_', '-' only");
    return 2;
}

// Approving is appending an event; no host needed (ADR-0011).
if (args.Length > 0 && args[0] == "approve")
{
    if (sessionId is null || storeDir is null || approvalKey is null)
    {
        Console.Error.WriteLine("usage: Aurelia.Runtime approve --session-id <id> --store-dir <path> --key <approvalKey> [--approver <id>] [--deny]");
        return 2;
    }
    var resolved = await Approvals.ResolveAsync(
        EventStores.FromEnvironment(storeDir), sessionId, approvalKey, approved: !deny, approver);
    if (!resolved)
    {
        Console.Error.WriteLine($"no approval request '{approvalKey}' in session '{sessionId}'");
        return 3;
    }
    Console.WriteLine($"{(deny ? "DENIED" : "APPROVED")} {approvalKey} by {approver}");
    return 0;
}

// Eval needs no host: scenarios run the drivers in-process.
if (args.Length > 0 && args[0] == "eval")
{
    if (suite is null)
    {
        Console.Error.WriteLine("usage: Aurelia.Runtime eval --suite <path>");
        return 2;
    }
    if (chaosSpec is not null)
        Console.WriteLine($"chaos: {chaosSpec}");
    var outcomes = await EvalRunner.RunSuiteAsync(suite, repeat, filter, chaosSpec);
    if (repeat <= 1)
    {
        foreach (var o in outcomes)
            Console.WriteLine($"{(o.Passed ? "PASS" : "FAIL"),-4} {o.Name,-28} {o.DurationMs,7:F0} ms  {o.Failure}");
    }
    else
    {
        // Aggregate per scenario across repeats — the experiment view.
        foreach (var group in outcomes.GroupBy(o => o.Name))
        {
            var times = group.Select(o => o.DurationMs).OrderBy(d => d).ToArray();
            var tokens = (In: group.Sum(o => o.InputTokens), Out: group.Sum(o => o.OutputTokens));
            Console.WriteLine(
                $"{group.Key,-28} {group.Count(o => o.Passed)}/{group.Count()} passed · " +
                $"p50 {times[times.Length / 2],7:F0} ms · " +
                $"p95 {times[Math.Min(times.Length - 1, (int)(times.Length * 0.95))],7:F0} ms · " +
                $"min {times[0]:F0} · max {times[^1]:F0}" +
                (tokens.In + tokens.Out > 0 ? $" · tokens {tokens.In}in/{tokens.Out}out" : ""));
        }
    }
    var durations = outcomes.Select(o => o.DurationMs).OrderBy(d => d).ToArray();
    var p50All = durations[durations.Length / 2];
    var p95All = durations[Math.Min(durations.Length - 1, (int)(durations.Length * 0.95))];
    var totalTokens = (In: outcomes.Sum(o => o.InputTokens), Out: outcomes.Sum(o => o.OutputTokens));
    Console.WriteLine($"{outcomes.Count(o => o.Passed)}/{outcomes.Count} passed · p50 {p50All:F0} ms · p95 {p95All:F0} ms" +
        (totalTokens.In + totalTokens.Out > 0 ? $" · tokens {totalTokens.In}in/{totalTokens.Out}out" : ""));
    return outcomes.All(o => o.Passed) ? 0 : 1;
}

// Replay needs no host: reconstruction is a pure fold over the log (ADR-0004).
if (args.Length > 0 && args[0] == "replay")
{
    if (sessionId is null || storeDir is null)
    {
        Console.Error.WriteLine("usage: Aurelia.Runtime replay --session-id <id> --store-dir <path>");
        return 2;
    }
    var replayEngine = new ReconstructionReplayEngine(EventStores.FromEnvironment(storeDir));
    var replayed = await replayEngine.ReplayAsync(sessionId);
    Console.WriteLine($"REPLAYED_FINAL_STATE {replayed.FinalStateJson}");
    return 0;
}

// Queue-triggered autonomous ingestion (ADR-0026): messages become durable
// sessions; duplicate delivery is a no-op resume by construction.
if (args.Length > 0 && args[0] is "send" or "listen")
{
    var fqdn = Environment.GetEnvironmentVariable("AURELIA_SERVICEBUS");
    if (fqdn is null || queue is null)
    {
        Console.Error.WriteLine("usage: set AURELIA_SERVICEBUS=<ns>.servicebus.windows.net, then");
        Console.Error.WriteLine("       Aurelia.Runtime send   --queue <q> --goal <text> [--message-id <id>]");
        Console.Error.WriteLine("       Aurelia.Runtime listen --queue <q> --store-dir <path> [--max <n>]");
        return 2;
    }
    if (args[0] == "send")
    {
        if (goal is null) { Console.Error.WriteLine("send requires --goal"); return 2; }
        await ServiceBusIngestion.SendAsync(fqdn, queue, goal, messageId);
        return 0;
    }
    if (storeDir is null) { Console.Error.WriteLine("listen requires --store-dir"); return 2; }
    var handled = await ServiceBusIngestion.ListenAsync(
        fqdn, queue, storeDir, maxMessages, TimeSpan.FromSeconds(10));
    Console.WriteLine($"processed {handled} message(s)");
    return 0;
}

// Conversations (ADR-0028): one turn per invocation, session open across
// invocations, memory tiers per ADR-0030. No host needed.
if (args.Length > 0 && args[0] == "turn")
{
    if (sessionId is null || message is null || storeDir is null)
    {
        Console.Error.WriteLine("usage: Aurelia.Runtime turn --session-id <id> --message <text> --store-dir <path>");
        return 2;
    }
    var chatStore = EventStores.FromEnvironment(storeDir);
    var chat = CaseResolutionAgent.CreateChatDriver(
        chatStore, OpenAICompatibleModelClient.FromEnvironmentOrFake(),
        new MemoryPolicy(HotTurns: 4, SummarizeEveryTurns: 4));
    var turnResult = await chat.HandleTurnAsync(sessionId, message);
    Console.WriteLine(turnResult.FinalStateJson.Contains("\"status\":\"awaiting")
        ? $"AWAITING {turnResult.FinalStateJson}"
        : $"AGENT_REPLY {turnResult.FinalStateJson}");
    return 0;
}

// The plain (floor-baseline) agent needs no host, no store, no durability —
// that is the point of it.
if (args.Length > 0 && args[0] == "run" && agent == "plain")
{
    if (goal is null || storeDir is null)
    {
        Console.Error.WriteLine("usage: Aurelia.Runtime run --agent plain --goal <text> --store-dir <path>");
        return 2;
    }
    var plainChaosSpec = Environment.GetEnvironmentVariable("AURELIA_CHAOS");
    var plainChaos = plainChaosSpec is null ? null : new ChaosSource(ChaosPolicy.Parse(plainChaosSpec));
    var plainTools = CaseResolutionAgent.CreateTools(storeDir,
        Environment.GetEnvironmentVariable("AURELIA_FAIL_NOTIFY") == "1");
    if (plainChaos is not null)
        plainTools = plainChaos.WrapTools(plainTools);
    try
    {
        var plainFinal = await PlainLoopAgent.RunAsync(plainTools, goal);
        Console.WriteLine($"FINAL_STATE {plainFinal}");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"ERROR {ex.Message}");
        return 1;
    }
}

if (args.Length == 0 || args[0] != "run" || sessionId is null || goal is null || storeDir is null)
{
    Console.Error.WriteLine("usage: Aurelia.Runtime run --session-id <id> --goal <text> --store-dir <path> [--agent echo|case-resolution|case-triage|concierge]");
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
        var eventStore = EventStores.FromEnvironment(storeDir);
        services.AddSingleton(eventStore);
        var failNotify = Environment.GetEnvironmentVariable("AURELIA_FAIL_NOTIFY") == "1";
        var chaosEnv = Environment.GetEnvironmentVariable("AURELIA_CHAOS");
        var chaos = chaosEnv is null ? null : new ChaosSource(ChaosPolicy.Parse(chaosEnv));
        services.AddSingleton<ISessionRuntime>(agent switch
        {
            "case-resolution" => CaseResolutionAgent.CreateDriver(eventStore, storeDir, failNotify, chaos),
            "case-triage" => CaseResolutionAgent.CreateTriageDriver(eventStore, storeDir,
                OpenAICompatibleModelClient.FromEnvironmentOrFake(), failNotify,
                fallback: Environment.GetEnvironmentVariable("AURELIA_MODEL_FALLBACK") == "fake"
                    ? new FakeModelClient() : null),
            "concierge" => CaseResolutionAgent.CreateConciergeDriver(eventStore, storeDir,
                OpenAICompatibleModelClient.FromEnvironmentOrFake(), failNotify),
            _ => new ScriptedAgentDriver(eventStore,
                new Dictionary<string, ITool> { ["echo"] = new BlockableEchoTool() },
                [new ScriptStep("echo", ctx => ctx.UserMessage)]),
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
                if (Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING") is { Length: > 0 } aiConnection)
                    tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = aiConnection);
            });
    })
    .Build();

await host.StartAsync();

var session = host.Services.GetRequiredService<IGrainFactory>().GetGrain<IAgentSessionGrain>(sessionId);
var finalState = await session.RunToCompletionAsync(goal);
Console.WriteLine(finalState.Contains("\"status\":\"awaiting_approval\"")
    ? $"AWAITING_APPROVAL {finalState}"
    : $"FINAL_STATE {finalState}");

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
