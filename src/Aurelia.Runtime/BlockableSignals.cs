// Crash-harness coordination protocol: a tool signals when execution starts
// (so the test knows when to kill the process) and blocks while a "block"
// file exists (so the kill lands mid-tool-call deterministically). No-op
// unless AURELIA_SIGNAL_DIR is set.
namespace Aurelia.Runtime;

public static class BlockableSignals
{
    public const string SignalDirVariable = "AURELIA_SIGNAL_DIR";

    public static async Task SignalAndWaitAsync(string idempotencyKey, CancellationToken ct = default)
    {
        var signalDir = Environment.GetEnvironmentVariable(SignalDirVariable);
        if (string.IsNullOrEmpty(signalDir))
            return;

        Directory.CreateDirectory(signalDir);
        var marker = Path.Combine(signalDir, $"{idempotencyKey.Replace('/', '_')}.started");
        await File.WriteAllTextAsync(marker, DateTimeOffset.UtcNow.ToString("O"), ct);

        var block = Path.Combine(signalDir, "block");
        while (File.Exists(block))
            await Task.Delay(100, ct);
    }
}
