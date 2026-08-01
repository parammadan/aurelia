// ADR-0018: appends are atomic and contiguity-checked — a stale writer's
// append is rejected, torn tails are truncated before the next append, and
// session ids that could escape the store directory are refused.
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class StoreConcurrencyTests
{
    [Fact]
    public async Task Duplicate_and_gapped_sequences_are_rejected()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-seq-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            await store.AppendAsync(Started(1));

            await Assert.ThrowsAsync<SequenceConflictException>(() => store.AppendAsync(Started(1)));
            await Assert.ThrowsAsync<SequenceConflictException>(() => store.AppendAsync(Message(3)));

            await store.AppendAsync(Message(2)); // contiguous: fine
            Assert.Equal(2, (await store.ReadAsync(SessionId)).Count);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task A_stale_writer_cannot_append_over_another_writers_event()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-stale-");
        try
        {
            // Two independent store instances over the same directory stand in
            // for two processes driving the same session.
            var writerA = new FileEventStore(dir.FullName);
            var writerB = new FileEventStore(dir.FullName);

            await writerA.AppendAsync(Started(1));
            await writerB.AppendAsync(Message(2)); // B saw seq 1, appends 2

            // A still believes the log ends at 1 and tries to append "its" 2.
            await Assert.ThrowsAsync<SequenceConflictException>(() => writerA.AppendAsync(Message(2)));

            var log = await writerA.ReadAsync(SessionId);
            Assert.Equal([1L, 2L], log.Select(e => e.Sequence));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Torn_tail_is_truncated_by_the_next_append()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-torn-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            await store.AppendAsync(Started(1));

            // Simulate a hard kill mid-append: an unterminated partial line.
            File.AppendAllText(Path.Combine(dir.FullName, $"{SessionId}.jsonl"),
                """{"type":"AgentMes""");

            // The torn tail was never durable: the log still ends at 1, the
            // next append truncates the fragment and lands cleanly.
            await store.AppendAsync(Message(2));

            var log = await store.ReadAsync(SessionId);
            Assert.Equal([1L, 2L], log.Select(e => e.Sequence));
            Assert.IsType<AgentMessageSent>(log[1]);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("white space")]
    public async Task Unsafe_session_ids_are_refused(string sessionId)
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-ids-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            await Assert.ThrowsAsync<ArgumentException>(() => store.ReadAsync(sessionId));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private const string SessionId = "seq-session";

    private static readonly DateTimeOffset T0 = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

    private static SessionStarted Started(long seq) => new()
    {
        SessionId = SessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = 0,
        RecordedAt = T0, Goal = "g",
    };

    private static AgentMessageSent Message(long seq) => new()
    {
        SessionId = SessionId, Sequence = seq, TurnOrdinal = 0, StepOrdinal = 0,
        RecordedAt = T0, Content = "m",
    };
}
