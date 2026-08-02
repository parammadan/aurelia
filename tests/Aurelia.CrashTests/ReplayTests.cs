// Verifies ADR-0004 reconstruction replay: deterministic, substitution-only,
// and loud on every integrity violation.
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class ReplayTests
{
    [Fact]
    public async Task Replay_reproduces_the_recorded_final_state_deterministically()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-replay-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var driver = new ScriptedAgentDriver(store,
                new Dictionary<string, Aurelia.Core.ITool> { ["echo"] = new BlockableEchoTool() },
                [new ScriptStep("echo", ctx => ctx.UserMessage)]);
            var live = await driver.RunToCompletionAsync("replay-session", "check case 42");

            var engine = new ReconstructionReplayEngine(store);
            var first = await engine.ReplayAsync("replay-session");
            var second = await engine.ReplayAsync("replay-session");

            Assert.Equal(live.FinalStateJson, first.FinalStateJson);
            Assert.Equal(first, second);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Replay_of_incomplete_session_fails_clearly()
    {
        var (store, dir) = await StoreWith(
            Started(1),
            ToolRequested(2));
        try
        {
            await Assert.ThrowsAsync<SessionNotCompletedException>(
                () => new ReconstructionReplayEngine(store).ReplayAsync(SessionId));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Replay_of_full_saga_unwind_reports_failed_state_with_compensations()
    {
        // Refund (step 0) succeeds; notify (step 1) fails terminally; the
        // refund is compensated (step 2); the session parks as failed
        // (ADRs 0015-0017). Exercises round-trip of the saga event types.
        var refundKey = $"{SessionId}/0/0";
        var (store, dir) = await StoreWith(
            Started(1),
            Tool(2, step: 0, "refund"),
            Succeeded(3, step: 0),
            Tool(4, step: 1, "notify"),
            new CallFailed
            {
                SessionId = SessionId, Sequence = 5, TurnOrdinal = 0, StepOrdinal = 1,
                RecordedAt = T0, Attempt = 2, Error = "smtp down",
            },
            new CompensationRequested
            {
                SessionId = SessionId, Sequence = 6, TurnOrdinal = 0, StepOrdinal = 2,
                RecordedAt = T0, ToolName = "refund", ArgumentsJson = "{}",
                CompensatesKey = refundKey,
            },
            Succeeded(7, step: 2),
            new SessionFailed
            {
                SessionId = SessionId, Sequence = 8, TurnOrdinal = 0, StepOrdinal = 3,
                RecordedAt = T0, Reason = "notify failed terminally",
                CompensatedKeys = [refundKey], UncompensatedKeys = [],
            });
        try
        {
            var result = await new ReconstructionReplayEngine(store).ReplayAsync(SessionId);
            using var state = System.Text.Json.JsonDocument.Parse(result.FinalStateJson);
            Assert.Equal("failed", state.RootElement.GetProperty("status").GetString());
            Assert.Equal(refundKey, state.RootElement.GetProperty("compensatedKeys")[0].GetString());
            Assert.Equal(0, state.RootElement.GetProperty("uncompensatedKeys").GetArrayLength());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Replay_rejects_compensation_of_unknown_key()
    {
        var (store, dir) = await StoreWith(
            Started(1),
            new CompensationRequested
            {
                SessionId = SessionId, Sequence = 2, TurnOrdinal = 0, StepOrdinal = 0,
                RecordedAt = T0, ToolName = "refund", ArgumentsJson = "{}",
                CompensatesKey = $"{SessionId}/9/9",
            });
        try
        {
            await Assert.ThrowsAsync<EventLogIntegrityException>(
                () => new ReconstructionReplayEngine(store).ReplayAsync(SessionId));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("outcome-without-intent")]
    [InlineData("second-terminal-outcome")]
    [InlineData("sequence-regression")]
    public async Task Replay_rejects_corrupt_logs(string corruption)
    {
        SessionEvent[] events = corruption switch
        {
            "outcome-without-intent" =>
                [Started(1), ToolSucceeded(2), Completed(3)],
            "second-terminal-outcome" =>
                [Started(1), ToolRequested(2), ToolSucceeded(3), ToolSucceeded(4), Completed(5)],
            "sequence-regression" =>
                [Started(2), ToolRequested(1)],
            _ => throw new ArgumentOutOfRangeException(nameof(corruption)),
        };

        var (store, dir) = await StoreWith(events);
        try
        {
            await Assert.ThrowsAsync<EventLogIntegrityException>(
                () => new ReconstructionReplayEngine(store).ReplayAsync(SessionId));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private const string SessionId = "corrupt-session";

    // Writes raw JSONL directly: the store itself now refuses non-contiguous
    // appends (ADR-0018), but replay must still reject logs corrupted by
    // other means — so corruption is injected below the store's guard.
    private static async Task<(FileEventStore Store, DirectoryInfo Dir)> StoreWith(params SessionEvent[] events)
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-replay-corrupt-");
        var web = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var lines = events.Select(e => System.Text.Json.JsonSerializer.Serialize(new
        {
            type = e.GetType().Name,
            data = System.Text.Json.JsonSerializer.SerializeToElement(e, e.GetType(), web),
        }, web));
        await File.WriteAllLinesAsync(Path.Combine(dir.FullName, $"{SessionId}.jsonl"), lines);
        return (new FileEventStore(dir.FullName), dir);
    }

    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static SessionStarted Started(long seq) => new()
    {
        SessionId = SessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = 0,
        RecordedAt = T0, Goal = "g",
    };

    private static ToolCallRequested ToolRequested(long seq) => Tool(seq, 0, "echo");

    private static ToolCallRequested Tool(long seq, int step, string tool) => new()
    {
        SessionId = SessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = step,
        RecordedAt = T0, ToolName = tool, ArgumentsJson = "g",
    };

    private static ToolCallSucceeded ToolSucceeded(long seq) => Succeeded(seq, 0);

    private static ToolCallSucceeded Succeeded(long seq, int step) => new()
    {
        SessionId = SessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = step,
        RecordedAt = T0, Attempt = 1, ResultJson = "{}",
    };

    private static SessionCompleted Completed(long seq) => new()
    {
        SessionId = SessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = 1,
        RecordedAt = T0, FinalStateJson = "{}",
    };
}
