// The pilot's adapters. AureliaAdapter may use Aurelia libraries — adapters
// are framework-specific by definition; the harness core is not.
using System.Diagnostics;
using Aurelia.Core;
using Aurelia.Runtime;

namespace Aurelia.Benchmark;

public sealed class AureliaAdapter(string runtimeDll) : IFrameworkAdapter
{
    public string Name => "aurelia";

    public ProcessStartInfo StartInfo(TaskAssignment task) =>
        Worker(runtimeDll, task, "case-resolution");

    // Aurelia's documented recovery: re-drive the same session id; the event
    // log resumes it.
    public ProcessStartInfo? RecoveryStartInfo(TaskAssignment task) => StartInfo(task);

    public async Task<TerminalReport?> QueryTerminalStateAsync(TaskAssignment task, CancellationToken ct)
    {
        var log = await new FileEventStore(task.StoreDir).ReadAsync(task.TaskId, ct);
        if (log.Any(e => e is SessionCompleted))
            return new TerminalReport("completed", [], []);
        if (log.OfType<SessionFailed>().FirstOrDefault() is { } failed)
            return new TerminalReport("failed", failed.CompensatedKeys, failed.UncompensatedKeys);
        return null; // in flight — not terminal
    }

    public bool ClaimsSuccess(int exitCode, string stdout) =>
        exitCode == 0 && stdout.Contains("FINAL_STATE") && stdout.Contains("\"notified\":true");

    internal static ProcessStartInfo Worker(string runtimeDll, TaskAssignment task, string agent)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList =
            {
                runtimeDll, "run",
                "--session-id", task.TaskId,
                "--goal", task.Goal,
                "--store-dir", task.StoreDir,
                "--agent", agent,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (k, v) in task.Env)
            psi.Environment[k] = v;
        return psi;
    }
}

/// <summary>The floor: an in-memory loop with fresh random keys per attempt.
/// Its "recovery story" is resubmitting the task — or nothing at all.</summary>
public sealed class PlainAdapter(string runtimeDll, bool resubmitsOnFailure) : IFrameworkAdapter
{
    public string Name => resubmitsOnFailure ? "plain-rerun" : "plain-noretry";

    public ProcessStartInfo StartInfo(TaskAssignment task) =>
        AureliaAdapter.Worker(runtimeDll, task, "plain");

    public ProcessStartInfo? RecoveryStartInfo(TaskAssignment task) =>
        resubmitsOnFailure ? StartInfo(task) : null;

    public Task<TerminalReport?> QueryTerminalStateAsync(TaskAssignment task, CancellationToken ct) =>
        Task.FromResult<TerminalReport?>(null); // no durable state surface exists

    public bool ClaimsSuccess(int exitCode, string stdout) =>
        exitCode == 0 && stdout.Contains("FINAL_STATE") && stdout.Contains("\"notified\":true");
}
