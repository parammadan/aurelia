// LangGraph under test: StateGraph + Sqlite checkpointer, thread_id = task id,
// recovery = resume the thread (its documented durable-execution story).
// Requires the physical tool boundary (--tools http) — LangGraph gets no
// in-process tools, by design. Two variants: deterministic idempotency keys
// (best-effort per methodology §4) and uuid-keys (what most published
// examples do).
using System.Diagnostics;
using System.Text.Json;

namespace Aurelia.Benchmark;

public sealed class LangGraphAdapter(string python, string script, bool uuidKeys) : IFrameworkAdapter
{
    public string Name => uuidKeys ? "langgraph-uuidkeys" : "langgraph";

    public ProcessStartInfo StartInfo(TaskAssignment task)
    {
        var psi = new ProcessStartInfo(python)
        {
            ArgumentList =
            {
                script,
                "--task-id", task.TaskId,
                "--goal", task.Goal,
                "--server", task.Env["AURELIA_TOOLSERVER"],
                "--db", Path.Combine(task.StoreDir, "checkpoints.sqlite"),
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (uuidKeys)
            psi.ArgumentList.Add("--uuid-keys");
        return psi;
    }

    // Documented recovery: re-invoke the same thread; the checkpoint resumes it.
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
            [], []);
    }

    public bool ClaimsSuccess(int exitCode, string stdout) =>
        exitCode == 0 && stdout.Contains("FINAL_STATE") && stdout.Contains("\"notified\":true");
}
