// The Aurelia MCP server: exposes durable agent sessions as MCP tools, which
// is the integration doorway for Copilot Studio (MCP connectors), Claude, and
// any other MCP client. Sessions are ordinary Aurelia sessions — durable,
// resumable, approvable — so the async pattern falls out of the runtime:
// start returns meaningful state immediately, get polls the log, approvals
// resolve from anywhere.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
// stdio transport: stdout belongs to the protocol; logs go to stderr.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
