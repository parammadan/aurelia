// Temporal under test: workflow + activities against the HTTP tool server,
// activity retries capped at 2 (parity), the documented saga pattern for
// compensation, recovery by re-awaiting the same workflow id. Requires
// TEMPORAL_ADDRESS (a `temporal server start-dev` instance) — the dev server
// is external, per-matrix, not harness-managed in the pilot.
using System.Diagnostics;
using System.Text.Json;

namespace Aurelia.Benchmark;

public sealed class TemporalAdapter(string python, string script, bool uuidKeys) : IFrameworkAdapter
{
    public string Name => uuidKeys ? "temporal-uuidkeys" : "temporal";

    public ProcessStartInfo StartInfo(TaskAssignment task)
    {
        var psi = new ProcessStartInfo(python)
        {
            ArgumentList =
            {
                script,
                "--task-id", $"{task.TaskId}-{Path.GetFileName(Path.GetDirectoryName(task.StoreDir))}",
                "--goal", task.Goal,
                "--server", task.Env["AURELIA_TOOLSERVER"],
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (uuidKeys)
            psi.ArgumentList.Add("--uuid-keys");
        return psi;
    }

    // Documented recovery: restart the worker, await the same workflow id;
    // the server re-dispatches from history.
    public ProcessStartInfo? RecoveryStartInfo(TaskAssignment task) => StartInfo(task);

    public async Task<TerminalReport?> QueryTerminalStateAsync(TaskAssignment task, CancellationToken ct)
    {
        var psi = StartInfo(task);
        psi.ArgumentList.Add("--query");
        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return null;
        using var doc = JsonDocument.Parse(stdout);
        return new TerminalReport(
            doc.RootElement.GetProperty("status").GetString()!,
            [.. doc.RootElement.GetProperty("compensatedEffects").EnumerateArray().Select(e => e.GetString()!)],
            [.. doc.RootElement.GetProperty("leakedEffects").EnumerateArray().Select(e => e.GetString()!)]);
    }

    public bool ClaimsSuccess(int exitCode, string stdout) =>
        exitCode == 0 && stdout.Contains("FINAL_STATE") && stdout.Contains("\"notified\":true");
}
