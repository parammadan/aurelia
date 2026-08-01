// Minimal test tool: echoes its arguments, participating in the crash
// harness's signal/block protocol.
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public sealed class BlockableEchoTool : ITool
{
    public const string SignalDirVariable = BlockableSignals.SignalDirVariable;

    public string Name => "echo";

    public async Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default)
    {
        await BlockableSignals.SignalAndWaitAsync(idempotencyKey, ct);
        return JsonSerializer.Serialize(new { echoed = argumentsJson });
    }
}
