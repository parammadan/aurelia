// The harness core: owns task setup, fault injection, kills, recovery
// orchestration, and scoring. Framework-agnostic — it only speaks
// IFrameworkAdapter.
//
// Known limitation (documented in docs/benchmark/harness.md): fault injection
// currently rides environment variables understood by the in-repo tool
// implementations. Before third-party frameworks can be measured, the tools
// move behind a harness-owned HTTP boundary so injection is physical, not
// cooperative.
using System.Diagnostics;
using System.Text;

namespace Aurelia.Benchmark;

public static class HarnessRunner
{
    public static async Task<RunScore> RunOnceAsync(
        IFrameworkAdapter adapter, string fault, int runSeed,
        string? toolServerDll = null, CancellationToken ct = default)
    {
        var workDir = Directory.CreateTempSubdirectory("bench-");
        Process? toolServer = null;
        try
        {
            var storeDir = Path.Combine(workDir.FullName, "store");
            var signalDir = Path.Combine(workDir.FullName, "signals");
            Directory.CreateDirectory(storeDir);
            Directory.CreateDirectory(signalDir);

            // Fault configuration: on the worker when tools are in-process;
            // on the SERVER when the physical tool boundary is in play — the
            // worker then sees only a misbehaving HTTP dependency.
            var faultEnv = new Dictionary<string, string>();
            if (fault == "F1")
                faultEnv["AURELIA_CHAOS"] = $"seed={runSeed},tool_fail=0.3";
            if (fault == "F6")
            {
                faultEnv["AURELIA_SIGNAL_DIR"] = signalDir;
                faultEnv["AURELIA_SIGNAL_AFTER_EFFECT"] = "1";
                File.WriteAllText(Path.Combine(signalDir, "block"), "");
            }

            Dictionary<string, string> workerEnv;
            if (toolServerDll is null)
            {
                workerEnv = faultEnv;
            }
            else
            {
                string url;
                (toolServer, url) = await StartToolServerAsync(toolServerDll, storeDir, faultEnv, ct);
                workerEnv = new Dictionary<string, string> { ["AURELIA_TOOLSERVER"] = url };
            }

            var task = new TaskAssignment("bench", "credit for case 42", storeDir, signalDir, workerEnv);

            int exit;
            string stdout;
            if (fault == "F6")
            {
                // Kill in the after-effect window, then run the framework's
                // documented recovery (or nothing, if it has none).
                using (var victim = Start(adapter.StartInfo(task), out _))
                {
                    var deadline = DateTime.UtcNow.AddSeconds(60);
                    while (DateTime.UtcNow < deadline && !Directory.EnumerateFiles(signalDir, "*.started").Any())
                        await Task.Delay(100, ct);
                    victim.Kill(entireProcessTree: true);
                    victim.WaitForExit();
                }
                File.Delete(Path.Combine(signalDir, "block"));

                var recovery = adapter.RecoveryStartInfo(task);
                (exit, stdout) = recovery is null
                    ? (-1, "")
                    : await RunToExitAsync(recovery, ct);
            }
            else
            {
                (exit, stdout) = await RunToExitAsync(adapter.StartInfo(task), ct);
                // A non-kill failure also invokes the recovery story once.
                if (exit != 0 && adapter.RecoveryStartInfo(task) is { } retry)
                    (exit, stdout) = await RunToExitAsync(retry, ct);
            }

            var report = await adapter.QueryTerminalStateAsync(task, ct);
            return Scoring.Score(task, adapter, exit, stdout, report);
        }
        finally
        {
            if (toolServer is { HasExited: false })
                toolServer.Kill(entireProcessTree: true);
            toolServer?.Dispose();
            workDir.Delete(recursive: true);
        }
    }

    private static async Task<(Process Server, string Url)> StartToolServerAsync(
        string toolServerDll, string dataDir, IReadOnlyDictionary<string, string> faultEnv,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { toolServerDll, "--data-dir", dataDir },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var (k, v) in faultEnv)
            psi.Environment[k] = v;

        var process = Process.Start(psi)!;
        var listening = new TaskCompletionSource<string>();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null && e.Data.StartsWith("LISTENING "))
                listening.TrySetResult(e.Data["LISTENING ".Length..].Trim());
        };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var done = await Task.WhenAny(listening.Task, Task.Delay(30_000, ct));
        if (done != listening.Task)
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("tool server did not start within 30 s");
        }
        return (process, await listening.Task);
    }

    private static async Task<(int Exit, string Stdout)> RunToExitAsync(
        ProcessStartInfo psi, CancellationToken ct)
    {
        using var process = Start(psi, out var output);
        var exited = process.WaitForExit(120_000);
        if (!exited)
            process.Kill(entireProcessTree: true);
        return (exited ? process.ExitCode : -2, output.ToString());
    }

    private static Process Start(ProcessStartInfo psi, out StringBuilder output)
    {
        var captured = new StringBuilder();
        output = captured;
        var process = Process.Start(psi)!;
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (captured) captured.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (captured) captured.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }
}
