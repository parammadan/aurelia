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
            var driver = new ProvisionalSessionDriver(store, new BlockableEchoTool());
            var live = await driver.RunToCompletionAsync("replay-session", "check order 42");

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

    private static async Task<(FileEventStore Store, DirectoryInfo Dir)> StoreWith(params SessionEvent[] events)
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-replay-corrupt-");
        var store = new FileEventStore(dir.FullName);
        foreach (var evt in events)
            await store.AppendAsync(evt);
        return (store, dir);
    }

    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static SessionStarted Started(long seq) => new()
    {
        SessionId = SessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = 0,
        RecordedAt = T0, Goal = "g",
    };

    private static ToolCallRequested ToolRequested(long seq) => new()
    {
        SessionId = SessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = 0,
        RecordedAt = T0, ToolName = "echo", ArgumentsJson = "g",
    };

    private static ToolCallSucceeded ToolSucceeded(long seq) => new()
    {
        SessionId = SessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = 0,
        RecordedAt = T0, Attempt = 1, ResultJson = "{}",
    };

    private static SessionCompleted Completed(long seq) => new()
    {
        SessionId = SessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = 1,
        RecordedAt = T0, FinalStateJson = "{}",
    };
}
