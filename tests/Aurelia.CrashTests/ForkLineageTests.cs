// Fork provenance lives in the log (ADR-0045): the marker survives a round
// trip through the store, sits at the divergence point, and changes nothing
// about how the session reconstructs.
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class ForkLineageTests
{
    [Fact]
    public async Task Lineage_round_trips_and_leaves_reconstruction_untouched()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-fork-lineage-");
        try
        {
            var store = new FileEventStore(dir.FullName);

            // A real source session to fork from.
            var source = CaseResolutionAgent.CreateDriver(store, dir.FullName);
            var done = await source.RunToCompletionAsync("origin", "credit for case 42");
            Assert.Contains("notified", done.FinalStateJson);
            var originLog = await store.ReadAsync("origin");

            // Copy a prefix under a new id, then mark the divergence point —
            // after the prefix, so copied sequences are untouched.
            const long at = 2;
            foreach (var e in originLog.Where(e => e.Sequence <= at))
                await store.AppendAsync(Rebind(e, "origin-fork1"));
            await store.AppendAsync(new ForkedFrom
            {
                SessionId = "origin-fork1",
                Sequence = at + 1,
                TurnOrdinal = 0,
                StepOrdinal = 0,
                RecordedAt = DateTimeOffset.UtcNow,
                SourceSessionId = "origin",
                AtSequence = at,
            });

            var forkLog = await store.ReadAsync("origin-fork1");
            var lineage = Assert.Single(forkLog.OfType<ForkedFrom>());
            Assert.Equal("origin", lineage.SourceSessionId);
            Assert.Equal(at, lineage.AtSequence);

            // It sits AFTER the copied prefix, at the divergence point.
            Assert.Equal(at + 1, lineage.Sequence);
            Assert.All(forkLog.Where(e => e.Sequence <= at),
                e => Assert.Equal(originLog.Single(o => o.Sequence == e.Sequence).GetType(), e.GetType()));

            // The source is untouched by any of this.
            Assert.Equal(originLog.Count, (await store.ReadAsync("origin")).Count);
            Assert.Empty((await store.ReadAsync("origin")).OfType<ForkedFrom>());

            // A lineage marker is inert: the fork still resumes normally and
            // reaches the same terminal state its source did.
            var resumed = await CaseResolutionAgent.CreateDriver(store, dir.FullName)
                .RunToCompletionAsync("origin-fork1", "credit for case 42");
            Assert.Contains("notified", resumed.FinalStateJson);
            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync("origin-fork1");
            Assert.Equal(resumed.FinalStateJson, replayed.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static SessionEvent Rebind(SessionEvent e, string sessionId) => e switch
    {
        SessionStarted x => x with { SessionId = sessionId },
        ToolCallRequested x => x with { SessionId = sessionId },
        ToolCallSucceeded x => x with { SessionId = sessionId },
        _ => throw new NotSupportedException($"unexpected prefix event {e.GetType().Name}"),
    };
}
