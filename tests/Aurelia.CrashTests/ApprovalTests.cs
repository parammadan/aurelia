// The human-in-the-loop primitive (ADRs 0011-0013): a session pauses on an
// approval gate with no terminal event, survives the process boundary while
// paused, resumes on approval from a different driver instance, and unwinds
// on denial without ever executing the gated side effect.
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class ApprovalTests
{
    [Fact]
    public async Task Pause_survives_the_process_boundary_and_resumes_after_approval()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-approve-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            const string sessionId = "case-approval";

            // Case 7's $40 credit exceeds the $30 threshold: the run pauses.
            var first = CaseResolutionAgent.CreateDriver(store, dir.FullName);
            var paused = await first.RunToCompletionAsync(sessionId, "credit for case 7");

            using var pausedState = JsonDocument.Parse(paused.FinalStateJson);
            Assert.Equal("awaiting_approval", pausedState.RootElement.GetProperty("status").GetString());
            var approvalKey = pausedState.RootElement.GetProperty("approvalKey").GetString()!;
            Assert.Equal($"{sessionId}/0/1", approvalKey);
            Assert.Contains("$40", pausedState.RootElement.GetProperty("question").GetString());

            // Nothing side-effectful happened, and the pause is derived state:
            // one intent, no outcome, no terminal event.
            Assert.False(File.Exists(Path.Combine(dir.FullName, "ledger.jsonl")));
            var log = await store.ReadAsync(sessionId);
            Assert.Single(log.OfType<ApprovalRequested>());
            Assert.DoesNotContain(log, e => e is SessionCompleted or SessionFailed);

            // A manager approves — could be days later, from any process.
            Assert.True(await Approvals.ResolveAsync(store, sessionId, approvalKey, true, "manager-7"));

            // A brand-new driver instance (fresh process) resumes and finishes.
            var second = CaseResolutionAgent.CreateDriver(store, dir.FullName);
            var done = await second.RunToCompletionAsync(sessionId, "credit for case 7");

            using var final = JsonDocument.Parse(done.FinalStateJson);
            Assert.True(final.RootElement.GetProperty("notified").GetBoolean());
            Assert.Equal(["credit"], LedgerTypes(dir));

            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync(sessionId);
            Assert.Equal(done.FinalStateJson, replayed.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Denied_approval_fails_the_session_without_side_effects()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-deny-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            const string sessionId = "case-denied";

            var driver = CaseResolutionAgent.CreateDriver(store, dir.FullName);
            var paused = await driver.RunToCompletionAsync(sessionId, "credit for case 7");
            using var pausedState = JsonDocument.Parse(paused.FinalStateJson);
            var approvalKey = pausedState.RootElement.GetProperty("approvalKey").GetString()!;

            await Approvals.ResolveAsync(store, sessionId, approvalKey, false, "manager-7");
            var done = await CaseResolutionAgent.CreateDriver(store, dir.FullName)
                .RunToCompletionAsync(sessionId, "credit for case 7");

            using var final = JsonDocument.Parse(done.FinalStateJson);
            Assert.Equal("failed", final.RootElement.GetProperty("status").GetString());
            Assert.Contains("denied by manager-7", final.RootElement.GetProperty("reason").GetString());
            Assert.Equal(0, final.RootElement.GetProperty("uncompensatedKeys").GetArrayLength());

            // The credit was never issued: the gate sits before the side effect.
            Assert.False(File.Exists(Path.Combine(dir.FullName, "ledger.jsonl")));

            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync(sessionId);
            Assert.Equal(done.FinalStateJson, replayed.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Below_threshold_credits_never_request_approval()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-nogate-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var driver = CaseResolutionAgent.CreateDriver(store, dir.FullName);
            var done = await driver.RunToCompletionAsync("case-auto", "credit for case 42");

            using var final = JsonDocument.Parse(done.FinalStateJson);
            Assert.True(final.RootElement.GetProperty("notified").GetBoolean());
            var log = await store.ReadAsync("case-auto");
            Assert.DoesNotContain(log, e => e is ApprovalRequested);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Expired_approval_unwinds_via_recorded_time()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-expiry-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            IReadOnlyList<AgentStep> script =
            [
                new ScriptStep("lookup_case", ctx => ctx.UserMessage),
                new ApprovalGate(_ => "approve this?", "manager", Expiry: TimeSpan.FromMinutes(1)),
                new ScriptStep("issue_service_credit", ctx => ctx.Results["lookup_case"]),
                new ScriptStep("notify_customer", ctx => ctx.Results["issue_service_credit"]),
            ];
            // Virtual time: the driver observes this clock for expiry decisions,
            // so the test advances time instead of sleeping past a real deadline.
            var virtualNow = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
            ScriptedAgentDriver Driver() => new(store,
                CaseResolutionAgent.CreateInProcessTools(dir.FullName), script,
                clock: () => virtualNow);

            var paused = await Driver().RunToCompletionAsync("exp", "credit for case 42");
            Assert.Contains("awaiting_approval", paused.FinalStateJson);

            virtualNow += TimeSpan.FromMinutes(5); // the deadline passes, virtually
            var done = await Driver().RunToCompletionAsync("exp", "credit for case 42");

            using var final = JsonDocument.Parse(done.FinalStateJson);
            Assert.Equal("failed", final.RootElement.GetProperty("status").GetString());
            Assert.Contains("expired", final.RootElement.GetProperty("reason").GetString());

            // ADR-0006/0013: the decision rode a RECORDED injected time.
            var log = await store.ReadAsync("exp");
            Assert.Single(log.OfType<TimeRequested>());
            Assert.Single(log.OfType<TimeProvided>());
            Assert.Single(log.OfType<ApprovalExpired>());
            Assert.False(File.Exists(Path.Combine(dir.FullName, "ledger.jsonl"))); // gate sat before the effect

            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync("exp");
            Assert.Equal(done.FinalStateJson, replayed.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static string[] LedgerTypes(DirectoryInfo dir)
    {
        var ledger = Path.Combine(dir.FullName, "ledger.jsonl");
        return !File.Exists(ledger)
            ? []
            : [.. File.ReadLines(ledger)
                .Select(l => JsonDocument.Parse(l).RootElement.GetProperty("type").GetString()!)];
    }
}
