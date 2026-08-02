// The Orchestration Reliability Benchmark — in-repo pilot runner.
// Implements the methodology draft (docs/benchmark/methodology.md) at pilot
// scale: frameworks × fault conditions × N runs, scored on Recovery Rate and
// the Correctness-After-Failure invariants (I1-I4) against the harness-owned
// ledger. The neutral-harness extraction comes later; this labels itself a
// pilot and behaves like one.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;

var n = 10;
var seed = 7;
string? outPath = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--n" when i + 1 < args.Length: _ = int.TryParse(args[++i], out n); break;
        case "--seed" when i + 1 < args.Length: _ = int.TryParse(args[++i], out seed); break;
        case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
    }
}

string[] frameworks = ["aurelia", "plain-rerun", "plain-noretry"];
string[] faults = ["F0", "F1", "F6"];
var runtimeDll = Path.Combine(AppContext.BaseDirectory, "Aurelia.Runtime.dll");
if (!File.Exists(runtimeDll))
{
    Console.Error.WriteLine($"runtime not found at {runtimeDll}");
    return 2;
}

var allRuns = new List<object>();
Console.WriteLine($"pilot: N={n} per cell, seed={seed}, goal='credit for case 42'");
Console.WriteLine($"{"framework",-14} {"fault",-5} {"RR",-7} {"CAF",-7} violations");

foreach (var fault in faults)
{
    foreach (var framework in frameworks)
    {
        var cell = new List<RunScore>();
        for (var i = 0; i < n; i++)
            cell.Add(await RunOnceAsync(framework, fault, seed + i));
        var rr = cell.Count(r => r.Correct);
        var caf = cell.Count(r => r is { I1: true, I2: true, I3: true, I4: true });
        var violations = string.Join(" ",
            new[] { ("I1", cell.Count(r => !r.I1)), ("I2", cell.Count(r => !r.I2)),
                    ("I3", cell.Count(r => !r.I3)), ("I4", cell.Count(r => !r.I4)) }
            .Where(v => v.Item2 > 0).Select(v => $"{v.Item1}×{v.Item2}"));
        Console.WriteLine($"{framework,-14} {fault,-5} {rr,2}/{n,-4} {caf,2}/{n,-4} {violations}");
        allRuns.AddRange(cell.Select(r => new
        {
            framework, fault, r.Correct, r.I1, r.I2, r.I3, r.I4, r.Credits, r.Reversals, r.Completed,
        }));
    }
}

if (outPath is not null)
{
    File.WriteAllText(outPath, JsonSerializer.Serialize(allRuns,
        new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"raw runs → {outPath}");
}
return 0;

async Task<RunScore> RunOnceAsync(string framework, string fault, int runSeed)
{
    var workDir = Directory.CreateTempSubdirectory("aurelia-bench-");
    try
    {
        var storeDir = Path.Combine(workDir.FullName, "store");
        var signalDir = Path.Combine(workDir.FullName, "signals");
        Directory.CreateDirectory(storeDir);
        Directory.CreateDirectory(signalDir);
        const string sessionId = "bench";
        const string goal = "credit for case 42";
        var agentArg = framework == "aurelia" ? "case-resolution" : "plain";

        var env = new Dictionary<string, string>();
        if (fault == "F1")
            env["AURELIA_CHAOS"] = $"seed={runSeed},tool_fail=0.3";
        if (fault == "F6")
        {
            env[BlockableSignals.SignalDirVariable] = signalDir;
            env["AURELIA_SIGNAL_AFTER_EFFECT"] = "1";
            File.WriteAllText(Path.Combine(signalDir, "block"), "");
        }

        var (exit, stdout) = fault == "F6"
            ? await RunWithKillAsync(agentArg, sessionId, goal, storeDir, signalDir, env, framework)
            : await RunWorkerAsync(agentArg, sessionId, goal, storeDir, env);

        // plain-rerun's recovery story for non-kill failures: one resubmit.
        if (fault == "F1" && framework == "plain-rerun" && exit != 0)
            (exit, stdout) = await RunWorkerAsync(agentArg, sessionId, goal, storeDir, env);

        return Score(framework, storeDir, sessionId, exit, stdout);
    }
    finally
    {
        workDir.Delete(recursive: true);
    }
}

async Task<(int Exit, string Stdout)> RunWithKillAsync(
    string agentArg, string sessionId, string goal, string storeDir, string signalDir,
    Dictionary<string, string> env, string framework)
{
    // Phase 1: run until the side effect has landed but the call hasn't
    // returned (the after-effect window), then SIGKILL the worker.
    using (var victim = StartWorker(agentArg, sessionId, goal, storeDir, env, out _))
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline && !Directory.EnumerateFiles(signalDir, "*.started").Any())
            await Task.Delay(100);
        victim.Kill(entireProcessTree: true);
        victim.WaitForExit();
    }
    File.Delete(Path.Combine(signalDir, "block"));

    // Phase 2: recovery, per the framework's story.
    if (framework == "plain-noretry")
        return (-1, ""); // nobody comes back for the task
    return await RunWorkerAsync(agentArg, sessionId, goal, storeDir, env);
}

async Task<(int Exit, string Stdout)> RunWorkerAsync(
    string agentArg, string sessionId, string goal, string storeDir, Dictionary<string, string> env)
{
    using var process = StartWorker(agentArg, sessionId, goal, storeDir, env, out var output);
    var exited = process.WaitForExit(120_000);
    if (!exited)
        process.Kill(entireProcessTree: true);
    return (exited ? process.ExitCode : -2, output.ToString());
}

Process StartWorker(
    string agentArg, string sessionId, string goal, string storeDir,
    Dictionary<string, string> env, out StringBuilder output)
{
    var psi = new ProcessStartInfo("dotnet")
    {
        ArgumentList =
        {
            runtimeDll, "run",
            "--session-id", sessionId,
            "--goal", goal,
            "--store-dir", storeDir,
            "--agent", agentArg,
        },
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (var (k, v) in env)
        psi.Environment[k] = v;
    var captured = new StringBuilder();
    output = captured;
    var process = Process.Start(psi)!;
    process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (captured) captured.AppendLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (captured) captured.AppendLine(e.Data); };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}

RunScore Score(string framework, string storeDir, string sessionId, int exit, string stdout)
{
    var ledgerPath = Path.Combine(storeDir, "ledger.jsonl");
    var entries = !File.Exists(ledgerPath)
        ? []
        : File.ReadLines(ledgerPath)
            .Select(l => JsonDocument.Parse(l).RootElement)
            .Select(e => e.GetProperty("type").GetString()!)
            .ToArray();
    var credits = entries.Count(t => t == "credit");
    var reversals = entries.Count(t => t == "reversal");

    var completed = exit == 0 && stdout.Contains("FINAL_STATE") && stdout.Contains("\"notified\":true");
    var reportedSuccess = completed;

    // Explicit leak/terminal reporting: only queryable durable state counts.
    bool hasTerminalReport = false, hasExplicitLeakReport = false;
    if (framework == "aurelia")
    {
        var log = new FileEventStore(storeDir).ReadAsync(sessionId).GetAwaiter().GetResult();
        hasTerminalReport = log.Any(e => e is SessionCompleted or SessionFailed);
        hasExplicitLeakReport = log.OfType<SessionFailed>()
            .Any(f => f.UncompensatedKeys.Count > 0 || f.CompensatedKeys.Count > 0);
    }

    var correct = completed && credits == 1;
    var i1 = credits <= 1;
    var i2 = completed || credits == 0 || reversals >= credits || hasExplicitLeakReport;
    var i3 = completed || hasTerminalReport || (exit is 1 && stdout.Contains("ERROR"));
    var i4 = !(reportedSuccess && credits == 0);
    return new RunScore(correct, i1, i2, i3, i4, credits, reversals, completed);
}

internal sealed record RunScore(
    bool Correct, bool I1, bool I2, bool I3, bool I4, int Credits, int Reversals, bool Completed);
