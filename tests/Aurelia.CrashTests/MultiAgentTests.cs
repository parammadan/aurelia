// Multi-agent composition (ADRs 0023-0025): the child is a full session with
// its own log; parent recovery substitutes the recorded child result; a
// failed child is terminal and the parent unwinds; a paused child pauses the
// parent and both resume on approval.
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class MultiAgentTests
{
    [Fact]
    public async Task Concierge_composes_model_step_and_child_session()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-agents-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var driver = CaseResolutionAgent.CreateConciergeDriver(store, dir.FullName, new FakeModelClient());

            var result = await driver.RunToCompletionAsync("front", "hello, please credit case 42");

            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.True(final.RootElement.GetProperty("notified").GetBoolean());

            // Parent log: one model call pair + one agent call pair.
            var parentLog = await store.ReadAsync("front");
            var agentCall = Assert.Single(parentLog.OfType<AgentCallRequested>());
            Assert.Equal("front.0.1", agentCall.ChildSessionId);
            var agentDone = Assert.Single(parentLog.OfType<AgentCallSucceeded>());
            Assert.Equal(agentCall.IdempotencyKey, agentDone.IdempotencyKey);

            // Child log: a complete case-resolution session of its own.
            var childLog = await store.ReadAsync("front.0.1");
            Assert.Single(childLog.OfType<SessionStarted>());
            Assert.Single(childLog.OfType<SessionCompleted>());
            Assert.Contains(childLog.OfType<ToolCallRequested>(), r => r.ToolName == "issue_service_credit");

            Assert.Equal(["credit"], LedgerTypes(dir));

            // Both sessions replay; rerunning the parent substitutes the
            // recorded child result — neither log grows.
            var parentReplay = await new ReconstructionReplayEngine(store).ReplayAsync("front");
            Assert.Equal(result.FinalStateJson, parentReplay.FinalStateJson);
            var countsBefore = (parentLog.Count, childLog.Count);
            await driver.RunToCompletionAsync("front", "hello, please credit case 42");
            Assert.Equal(countsBefore,
                ((await store.ReadAsync("front")).Count, (await store.ReadAsync("front.0.1")).Count));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Failed_child_is_terminal_and_child_compensated_its_own_effects()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-agents-fail-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var driver = CaseResolutionAgent.CreateConciergeDriver(
                store, dir.FullName, new FakeModelClient(), failNotifications: true);

            var result = await driver.RunToCompletionAsync("front", "credit case 42");

            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.Equal("failed", final.RootElement.GetProperty("status").GetString());
            Assert.Contains("sub-agent 'case-resolution' failed",
                final.RootElement.GetProperty("reason").GetString());
            // Parent owned no compensatable effects; the CHILD reversed the credit.
            Assert.Equal(0, final.RootElement.GetProperty("uncompensatedKeys").GetArrayLength());
            Assert.Equal(["credit", "reversal"], LedgerTypes(dir));

            var childLog = await store.ReadAsync("front.0.1");
            Assert.Single(childLog.OfType<SessionFailed>());
            var parentLog = await store.ReadAsync("front");
            Assert.Single(parentLog.OfType<CallFailed>());
            Assert.DoesNotContain(parentLog, e => e is AgentCallSucceeded);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Paused_child_pauses_the_parent_and_both_resume_on_approval()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-agents-pause-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var driver = CaseResolutionAgent.CreateConciergeDriver(store, dir.FullName, new FakeModelClient());

            // Case 7's $40 credit trips the child's approval gate.
            var paused = await driver.RunToCompletionAsync("front", "credit case 7");
            using var pausedState = JsonDocument.Parse(paused.FinalStateJson);
            Assert.Equal("awaiting_subagent", pausedState.RootElement.GetProperty("status").GetString());
            Assert.Equal("front.0.1", pausedState.RootElement.GetProperty("childSessionId").GetString());
            var approvalKey = pausedState.RootElement.GetProperty("childState")
                .GetProperty("approvalKey").GetString()!;

            // Neither session has a terminal event: both are derived-paused.
            Assert.DoesNotContain(await store.ReadAsync("front"), e => e is SessionCompleted or SessionFailed);
            Assert.DoesNotContain(await store.ReadAsync("front.1"), e => e is SessionCompleted or SessionFailed);

            // Approve in the CHILD session; a fresh parent drive resumes both.
            Assert.True(await Approvals.ResolveAsync(store, "front.0.1", approvalKey, true, "manager"));
            var done = await CaseResolutionAgent
                .CreateConciergeDriver(store, dir.FullName, new FakeModelClient())
                .RunToCompletionAsync("front", "credit case 7");

            using var final = JsonDocument.Parse(done.FinalStateJson);
            Assert.True(final.RootElement.GetProperty("notified").GetBoolean());
            Assert.Equal(["credit"], LedgerTypes(dir));
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
