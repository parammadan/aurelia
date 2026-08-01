// THE CONTRACT (ADR-0002): kill the host with SIGKILL mid-tool-call; a fresh
// process must resume the session from the event log and complete it with the
// correct final state. Any change that breaks this test does not merge.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class CrashRecoveryTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(120);

    [Fact]
    public async Task Session_killed_mid_tool_call_resumes_and_completes_with_correct_final_state()
    {
        var workDir = Directory.CreateTempSubdirectory("aurelia-crash-");
        try
        {
            var storeDir = Path.Combine(workDir.FullName, "store");
            var signalDir = Path.Combine(workDir.FullName, "signals");
            Directory.CreateDirectory(storeDir);
            Directory.CreateDirectory(signalDir);

            const string sessionId = "crash-test-session";
            const string goal = "look up order 1234";
            var blockFile = Path.Combine(signalDir, "block");

            // Phase 1: block the tool, start the host, and SIGKILL it once the
            // tool call is provably in flight.
            File.WriteAllText(blockFile, "");
            var firstRunOutput = new StringBuilder();
            using (var victim = StartHost(sessionId, goal, storeDir, signalDir, firstRunOutput))
            {
                await WaitUntilAsync(
                    () => Directory.EnumerateFiles(signalDir, "*.started").Any(),
                    StartupTimeout,
                    $"tool never started; host output:\n{firstRunOutput}");
                victim.Kill(entireProcessTree: true);
                victim.WaitForExit();
            }

            var store = new FileEventStore(storeDir);
            var afterCrash = await store.ReadAsync(sessionId);
            Assert.Contains(afterCrash, e => e is ToolCallRequested);
            Assert.DoesNotContain(afterCrash, e => e is CallOutcome);
            Assert.DoesNotContain(afterCrash, e => e is SessionCompleted);

            // Phase 2: unblock the tool and start a fresh process, which must
            // resume from the log and finish.
            File.Delete(blockFile);
            var secondRunOutput = new StringBuilder();
            using (var survivor = StartHost(sessionId, goal, storeDir, signalDir, secondRunOutput))
            {
                var exited = survivor.WaitForExit((int)CompletionTimeout.TotalMilliseconds);
                if (!exited)
                    survivor.Kill(entireProcessTree: true);
                Assert.True(exited, $"resumed host did not exit; output:\n{secondRunOutput}");
                Assert.Equal(0, survivor.ExitCode);
            }

            var finalLog = await store.ReadAsync(sessionId);
            Assert.Single(finalLog.OfType<SessionStarted>());
            var request = Assert.Single(finalLog.OfType<ToolCallRequested>());
            var success = Assert.Single(finalLog.OfType<ToolCallSucceeded>());
            var completed = Assert.Single(finalLog.OfType<SessionCompleted>());

            // ADR-0010/0012: the outcome pairs with its intent by key, which
            // both phases derive from the same inherited coordinates.
            Assert.Equal($"{sessionId}/0/0", request.IdempotencyKey);
            Assert.Equal(request.IdempotencyKey, success.IdempotencyKey);
            Assert.Equal(1, success.Attempt);

            using var finalState = JsonDocument.Parse(completed.FinalStateJson);
            Assert.Equal(goal, finalState.RootElement.GetProperty("echoed").GetString());

            // ADR-0004: the crashed-and-recovered session replays to exactly
            // the state the live run produced.
            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync(sessionId);
            Assert.Equal(completed.FinalStateJson, replayed.FinalStateJson);
        }
        finally
        {
            workDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Case_resolution_survives_sigkill_mid_service_credit_with_exactly_one_credit()
    {
        var workDir = Directory.CreateTempSubdirectory("aurelia-case-crash-");
        try
        {
            var storeDir = Path.Combine(workDir.FullName, "store");
            var signalDir = Path.Combine(workDir.FullName, "signals");
            Directory.CreateDirectory(storeDir);
            Directory.CreateDirectory(signalDir);

            const string sessionId = "case-crash";
            const string goal = "service credit for case 42";
            var blockFile = Path.Combine(signalDir, "block");
            File.WriteAllText(blockFile, "");

            var firstRun = new StringBuilder();
            using (var victim = StartHost(sessionId, goal, storeDir, signalDir, firstRun, "case-resolution"))
            {
                await WaitUntilAsync(
                    () => Directory.EnumerateFiles(signalDir, "*.started").Any(),
                    StartupTimeout,
                    $"service credit never started; host output:\n{firstRun}");
                victim.Kill(entireProcessTree: true);
                victim.WaitForExit();
            }

            var store = new FileEventStore(storeDir);
            var afterCrash = await store.ReadAsync(sessionId);
            var creditIntent = Assert.Single(afterCrash.OfType<ToolCallRequested>(),
                r => r.ToolName == "issue_service_credit");
            Assert.DoesNotContain(afterCrash.OfType<CallOutcome>(),
                o => o.IdempotencyKey == creditIntent.IdempotencyKey);
            Assert.DoesNotContain(afterCrash, e => e is SessionCompleted or SessionFailed);

            File.Delete(blockFile);
            var secondRun = new StringBuilder();
            using (var survivor = StartHost(sessionId, goal, storeDir, signalDir, secondRun, "case-resolution"))
            {
                var exited = survivor.WaitForExit((int)CompletionTimeout.TotalMilliseconds);
                if (!exited)
                    survivor.Kill(entireProcessTree: true);
                Assert.True(exited, $"resumed host did not exit; output:\n{secondRun}");
                Assert.Equal(0, survivor.ExitCode);
            }

            // The money assertion: exactly one credit in the ledger.
            var ledger = File.ReadLines(Path.Combine(storeDir, "ledger.jsonl"))
                .Select(l => JsonDocument.Parse(l).RootElement.GetProperty("type").GetString()!)
                .ToArray();
            Assert.Equal(["credit"], ledger);

            var finalLog = await store.ReadAsync(sessionId);
            var completed = Assert.Single(finalLog.OfType<SessionCompleted>());
            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync(sessionId);
            Assert.Equal(completed.FinalStateJson, replayed.FinalStateJson);
        }
        finally
        {
            workDir.Delete(recursive: true);
        }
    }

    private static Process StartHost(
        string sessionId, string goal, string storeDir, string signalDir, StringBuilder output,
        string agent = "echo")
    {
        // The Runtime executable is copied into the test output directory by
        // the project reference.
        var runtimeDll = Path.Combine(AppContext.BaseDirectory, "Aurelia.Runtime.dll");
        Assert.True(File.Exists(runtimeDll), $"runtime not found at {runtimeDll}");

        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList =
            {
                runtimeDll,
                "run",
                "--session-id", sessionId,
                "--goal", goal,
                "--store-dir", storeDir,
                "--agent", agent,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment[BlockableEchoTool.SignalDirVariable] = signalDir;

        var process = Process.Start(psi)!;
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string failureMessage)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(100);
        }
        Assert.Fail(failureMessage);
    }
}
