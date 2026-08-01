// Durable model calls (ADR-0004): the intent records the full prompt/model/
// config, a recorded outcome is substituted on rerun (the model is NOT called
// again), failures retry under the same key, and exhaustion fails the session.
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class ModelStepTests
{
    [Fact]
    public async Task Model_call_is_recorded_two_phase_and_substituted_on_rerun()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-model-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var fake = new FakeModelClient();
            var driver = CaseResolutionAgent.CreateTriageDriver(store, dir.FullName, fake);

            const string goal = "my invoice is wrong, please credit case 42";
            var result = await driver.RunToCompletionAsync("triage", goal);

            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.Equal("42", final.RootElement.GetProperty("caseId").GetString());

            var log = await store.ReadAsync("triage");
            var intent = Assert.Single(log.OfType<ModelCallRequested>());
            Assert.Equal("fake-deterministic", intent.ModelId);
            Assert.Contains(goal, intent.PromptJson);        // full inputs recorded
            Assert.Equal("fake-v1", intent.ConfigVersion);
            var outcome = Assert.Single(log.OfType<ModelCallSucceeded>());
            Assert.Equal(intent.IdempotencyKey, outcome.IdempotencyKey);
            Assert.Equal(1, fake.Calls);

            // The money property: rerunning the session substitutes the
            // recorded output — the model is not called again.
            var again = await driver.RunToCompletionAsync("triage", goal);
            Assert.Equal(result.FinalStateJson, again.FinalStateJson);
            Assert.Equal(1, fake.Calls);

            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync("triage");
            Assert.Equal(result.FinalStateJson, replayed.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Flaky_model_is_retried_under_the_same_key()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-model-flaky-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var flaky = new FlakyModelClient(new FakeModelClient(), failures: 1);
            var driver = new ScriptedAgentDriver(store,
                CaseResolutionAgent.CreateTools(dir.FullName),
                CaseResolutionAgent.TriageScript, flaky);

            var result = await driver.RunToCompletionAsync("triage-flaky", "credit case 42");

            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.True(final.RootElement.GetProperty("notified").GetBoolean());

            var log = await store.ReadAsync("triage-flaky");
            var intent = Assert.Single(log.OfType<ModelCallRequested>());
            Assert.Single(log.OfType<CallAttemptFailed>(),
                f => f.IdempotencyKey == intent.IdempotencyKey);
            Assert.Equal(2, Assert.Single(log.OfType<ModelCallSucceeded>()).Attempt);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Exhausted_model_fails_the_session_with_nothing_to_compensate()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-model-dead-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var dead = new FlakyModelClient(new FakeModelClient(), failures: 99);
            var driver = new ScriptedAgentDriver(store,
                CaseResolutionAgent.CreateTools(dir.FullName),
                CaseResolutionAgent.TriageScript, dead);

            var result = await driver.RunToCompletionAsync("triage-dead", "credit case 42");

            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.Equal("failed", final.RootElement.GetProperty("status").GetString());
            Assert.Equal(0, final.RootElement.GetProperty("compensatedKeys").GetArrayLength());
            Assert.False(File.Exists(Path.Combine(dir.FullName, "ledger.jsonl")));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private sealed class FlakyModelClient(IModelClient inner, int failures) : IModelClient
    {
        private int _failed;

        public string ModelId => inner.ModelId;
        public string ParametersJson => inner.ParametersJson;
        public string ConfigVersion => inner.ConfigVersion;

        public Task<string> CompleteAsync(string promptJson, CancellationToken ct = default)
        {
            if (_failed < failures)
            {
                _failed++;
                throw new InvalidOperationException("model endpoint unavailable");
            }
            return inner.CompleteAsync(promptJson, ct);
        }
    }
}
