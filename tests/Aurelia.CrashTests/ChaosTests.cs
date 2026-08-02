// Chaos injection: deterministic at the extremes (rate 1.0 always fires,
// rate 0 never does), wrappers preserve compensability by type, and total
// tool chaos drives a session through the terminal-failure path cleanly.
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class ChaosTests
{
    [Fact]
    public async Task Rate_one_always_fires_and_rate_zero_never_does()
    {
        var chaotic = new ChaosSource(new ChaosPolicy(Seed: 1, ToolFailureRate: 1.0));
        var calm = new ChaosSource(new ChaosPolicy(Seed: 1, ToolFailureRate: 0));
        var echo = new BlockableEchoTool();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new ChaosTool(echo, chaotic).ExecuteAsync("x", "k"));
        var ok = await new ChaosTool(echo, calm).ExecuteAsync("x", "k");
        Assert.Contains("echoed", ok);
    }

    [Fact]
    public void Wrapping_preserves_compensability_by_type()
    {
        var chaos = new ChaosSource(new ChaosPolicy());
        var tools = CaseResolutionAgent.CreateTools(Path.GetTempPath());
        var wrapped = chaos.WrapTools(tools);

        Assert.IsAssignableFrom<ICompensatableTool>(wrapped["issue_service_credit"]);
        Assert.False(wrapped["lookup_case"] is ICompensatableTool);
        Assert.False(wrapped["notify_customer"] is ICompensatableTool);
    }

    [Fact]
    public async Task Total_tool_chaos_fails_the_session_through_the_normal_terminal_path()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-chaos-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var chaos = new ChaosSource(new ChaosPolicy(Seed: 7, ToolFailureRate: 1.0));
            var driver = CaseResolutionAgent.CreateDriver(store, dir.FullName, chaos: chaos);

            var result = await driver.RunToCompletionAsync("chaos-total", "credit for case 42");

            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.Equal("failed", final.RootElement.GetProperty("status").GetString());

            var log = await store.ReadAsync("chaos-total");
            Assert.Contains(log.OfType<CallFailed>(), f => f.Error.Contains("chaos"));
            // Nothing side-effectful ever succeeded, so nothing to compensate.
            Assert.Equal(0, final.RootElement.GetProperty("uncompensatedKeys").GetArrayLength());

            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync("chaos-total");
            Assert.Equal(result.FinalStateJson, replayed.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Malformed_model_output_fails_triage_without_issuing_credit()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-chaos-model-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var chaos = new ChaosSource(new ChaosPolicy(Seed: 3, ModelMalformedRate: 1.0));
            var driver = CaseResolutionAgent.CreateTriageDriver(
                store, dir.FullName, new FakeModelClient(), chaos: chaos);

            var result = await driver.RunToCompletionAsync("chaos-model", "credit for case 42");

            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.Equal("failed", final.RootElement.GetProperty("status").GetString());
            Assert.False(File.Exists(Path.Combine(dir.FullName, "ledger.jsonl")));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
