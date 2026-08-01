// Test tool for the crash
// harness: signals when execution starts (so the test knows when to kill the
// process) and blocks while a "block" file exists (so the kill lands
// mid-tool-call deterministically).
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public sealed class BlockableEchoTool : ITool
{
    public const string SignalDirVariable = "AURELIA_SIGNAL_DIR";

    public string Name => "echo";

    public async Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default)
    {
        var signalDir = Environment.GetEnvironmentVariable(SignalDirVariable);
        if (!string.IsNullOrEmpty(signalDir))
        {
            Directory.CreateDirectory(signalDir);
            var marker = Path.Combine(signalDir, $"{idempotencyKey.Replace('/', '_')}.started");
            await File.WriteAllTextAsync(marker, DateTimeOffset.UtcNow.ToString("O"), ct);

            var block = Path.Combine(signalDir, "block");
            while (File.Exists(block))
                await Task.Delay(100, ct);
        }
        return JsonSerializer.Serialize(new { echoed = argumentsJson });
    }
}
