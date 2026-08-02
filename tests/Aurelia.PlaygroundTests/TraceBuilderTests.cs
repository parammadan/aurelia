// Tests for the trace-viewer projection (previously untested — defense-pack
// weakness #8) and the single status derivation every surface now shares.
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Playground;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.PlaygroundTests;

public class TraceBuilderTests
{
    [Fact]
    public async Task Trace_pairs_calls_computes_durations_and_links_children()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-tracebuild-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var driver = CaseResolutionAgent.CreateConciergeDriver(store, dir.FullName, new FakeModelClient());
            await driver.RunToCompletionAsync("tb", "credit case 42");

            var trace = ToJson(TraceBuilder.Build("tb", await store.ReadAsync("tb")));

            Assert.Equal("completed", trace.GetProperty("status").GetString());
            var calls = trace.GetProperty("calls").EnumerateArray().ToArray();
            Assert.Equal(2, calls.Length); // model classify + sub-agent dispatch

            var model = calls[0];
            Assert.Equal("model", model.GetProperty("kind").GetString());
            Assert.Equal("succeeded", model.GetProperty("status").GetString());
            Assert.True(model.GetProperty("endMs").GetDouble() >= model.GetProperty("startMs").GetDouble());

            var agent = calls[1];
            Assert.Equal("agent", agent.GetProperty("kind").GetString());
            Assert.Equal("tb.1", agent.GetProperty("childSessionId").GetString());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Trace_shows_pending_approval_as_awaiting_with_open_bar()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-tracepause-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var driver = CaseResolutionAgent.CreateDriver(store, dir.FullName);
            await driver.RunToCompletionAsync("tb", "credit for case 7"); // trips the approval gate

            var trace = ToJson(TraceBuilder.Build("tb", await store.ReadAsync("tb")));

            Assert.Equal("awaiting_approval", trace.GetProperty("status").GetString());
            var approval = trace.GetProperty("calls").EnumerateArray()
                .Single(c => c.GetProperty("kind").GetString() == "approval");
            Assert.Equal("pending", approval.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, approval.GetProperty("endMs").ValueKind);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Trace_reports_retries_and_saga_compensation()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-tracesaga-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var driver = CaseResolutionAgent.CreateDriver(store, dir.FullName, failNotifications: true);
            await driver.RunToCompletionAsync("tb", "credit for case 42");

            var trace = ToJson(TraceBuilder.Build("tb", await store.ReadAsync("tb")));

            Assert.Equal("failed", trace.GetProperty("status").GetString());
            var calls = trace.GetProperty("calls").EnumerateArray().ToArray();

            var notify = calls.Single(c => c.GetProperty("name").GetString() == "notify_customer");
            Assert.Equal("failed", notify.GetProperty("status").GetString());
            Assert.Equal(2, notify.GetProperty("attempts").GetInt32());

            Assert.Single(calls, c => c.GetProperty("kind").GetString() == "compensation"
                && c.GetProperty("status").GetString() == "succeeded");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Status_derivation_is_shared_and_correct()
    {
        var t0 = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
        SessionEvent started = new SessionStarted
        { SessionId = "s", Sequence = 1, TurnOrdinal = 0, StepOrdinal = 0, RecordedAt = t0, Goal = "g" };
        SessionEvent approval = new ApprovalRequested
        { SessionId = "s", Sequence = 2, TurnOrdinal = 0, StepOrdinal = 1, RecordedAt = t0, Question = "q", ApproverPolicy = "p" };
        SessionEvent resolved = new ApprovalResolved
        { SessionId = "s", Sequence = 3, TurnOrdinal = 0, StepOrdinal = 1, RecordedAt = t0, Attempt = 1, Approved = true, ApproverId = "a" };
        SessionEvent completed = new SessionCompleted
        { SessionId = "s", Sequence = 4, TurnOrdinal = 0, StepOrdinal = 2, RecordedAt = t0, FinalStateJson = "{}" };

        Assert.Equal("in_flight", SessionStates.Derive([started]).Status);
        Assert.Equal("awaiting_approval", SessionStates.Derive([started, approval]).Status);
        Assert.NotNull(SessionStates.Derive([started, approval]).PendingApproval);
        Assert.Equal("in_flight", SessionStates.Derive([started, approval, resolved]).Status);
        Assert.Equal("completed", SessionStates.Derive([started, approval, resolved, completed]).Status);
    }

    private static JsonElement ToJson(object trace) =>
        JsonSerializer.SerializeToElement(trace);
}
