// Multi-turn conversations + memory tiers (ADRs 0028-0031): turn-scoped
// execution, context windows with clean provenance, crash-resume of an
// interrupted turn, durable summarization, and replay of open sessions.
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class TurnTests
{
    [Fact]
    public async Task Turns_are_scoped_and_the_window_carries_history()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-turns-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var probe = new PromptProbeClient();
            var driver = CaseResolutionAgent.CreateChatDriver(store, probe);

            var r1 = await driver.HandleTurnAsync("conv", "my name is Param");
            var r2 = await driver.HandleTurnAsync("conv", "case 42 is broken");
            var r3 = await driver.HandleTurnAsync("conv", "what was my name again?");

            var log = await store.ReadAsync("conv");
            Assert.Equal([1, 2, 3], log.OfType<UserMessageReceived>().Select(e => e.TurnOrdinal));
            Assert.Equal([1, 2, 3], log.OfType<AgentMessageSent>().Select(e => e.TurnOrdinal));
            // Intents are keyed per turn (ADR-0007 finally exercised).
            Assert.Equal(["conv/1/0", "conv/2/0", "conv/3/0"],
                log.OfType<ModelCallRequested>().Select(m => m.IdempotencyKey));

            // The turn-3 prompt (recorded on the intent — provenance) carries
            // turn 1's content via the window.
            var turn3Prompt = log.OfType<ModelCallRequested>().Single(m => m.TurnOrdinal == 3).PromptJson;
            Assert.Contains("my name is Param", turn3Prompt);
            Assert.Contains(r1.FinalStateJson, turn3Prompt); // agent's own turn-1 reply too
            Assert.NotEqual(r2.FinalStateJson, r3.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Interrupted_turn_resumes_with_the_recorded_message_and_substituted_model_call()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-turns-resume-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var probe = new PromptProbeClient();
            await CaseResolutionAgent.CreateChatDriver(store, probe).HandleTurnAsync("conv", "turn one");

            // Simulate a crash mid-turn-2: user message + model intent/outcome
            // recorded, but the process died before AgentMessageSent.
            var log = await store.ReadAsync("conv");
            var seq = log[^1].Sequence;
            await store.AppendAsync(new UserMessageReceived
            {
                SessionId = "conv", Sequence = ++seq, TurnOrdinal = 2, StepOrdinal = 0,
                RecordedAt = DateTimeOffset.UtcNow, Content = "the original turn-two message",
            });

            var callsBefore = probe.Calls;
            var resumed = await CaseResolutionAgent.CreateChatDriver(store, probe)
                .HandleTurnAsync("conv", "a DIFFERENT retry message");

            // ADR-0028: the recorded message wins; the turn completes as turn 2.
            var after = await store.ReadAsync("conv");
            Assert.Equal(2, after.OfType<AgentMessageSent>().Max(e => e.TurnOrdinal));
            var turn2Prompt = after.OfType<ModelCallRequested>().Single(m => m.TurnOrdinal == 2).PromptJson;
            Assert.Contains("the original turn-two message", turn2Prompt);
            Assert.DoesNotContain("DIFFERENT retry message", turn2Prompt);
            Assert.Equal(callsBefore + 1, probe.Calls); // model ran once for the resumed turn
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Summary_checkpoint_is_durable_and_replaces_old_turns_in_the_window()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-turns-summary-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var probe = new PromptProbeClient();
            var driver = CaseResolutionAgent.CreateChatDriver(store, probe,
                new MemoryPolicy(HotTurns: 2, SummarizeEveryTurns: 2));

            for (var i = 1; i <= 5; i++)
                await driver.HandleTurnAsync("conv", $"message number {i}");

            var log = await store.ReadAsync("conv");
            var checkpoint = Assert.Single(log.OfType<SummaryCheckpointed>());
            Assert.True(checkpoint.ThroughTurn >= 1);
            // The summarization itself was a durable, recorded model call.
            Assert.Contains(log.OfType<ModelCallRequested>(), m => m.RouteClass == "summarize");

            // Later prompts include the summary and drop summarized turns.
            var lastPrompt = log.OfType<ModelCallRequested>()
                .Where(m => m.RouteClass == "chat").OrderBy(m => m.Sequence).Last().PromptJson;
            Assert.Contains("summarized", lastPrompt);
            Assert.DoesNotContain("message number 1", lastPrompt);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Replay_of_an_open_conversation_returns_the_last_completed_turn()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-turns-replay-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var driver = CaseResolutionAgent.CreateChatDriver(store, new PromptProbeClient());
            await driver.HandleTurnAsync("conv", "one");
            var r2 = await driver.HandleTurnAsync("conv", "two");

            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync("conv");
            using var doc = JsonDocument.Parse(replayed.FinalStateJson);
            Assert.Equal("open", doc.RootElement.GetProperty("status").GetString());
            Assert.Equal(2, doc.RootElement.GetProperty("asOfTurn").GetInt32());
            Assert.Equal(r2.FinalStateJson, doc.RootElement.GetProperty("reply").GetString());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>Deterministic model that proves what it was shown: replies
    /// with a digest of its prompt, and counts invocations.</summary>
    private sealed class PromptProbeClient : IModelClient
    {
        private int _calls;

        public string ModelId => "prompt-probe";
        public string ParametersJson => "{}";
        public string ConfigVersion => "probe-v1";
        public int Calls => _calls;

        public Task<ModelCompletion> CompleteAsync(string promptJson, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(new ModelCompletion(
                $"reply#{_calls} (saw {promptJson.Length} chars)", 10, 5));
        }
    }
}
