// The case-resolution reference agent: happy path, exactly-once issuance,
// the saga unwind (credit reversed when notify fails), retried compensation,
// and compensation exhaustion parking the session (ADRs 0015-0017).
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class CaseResolutionTests
{
    [Fact]
    public async Task Happy_path_issues_one_credit_and_replays_identically()
    {
        using var fixture = new Fixture(failNotifications: false);
        var result = await fixture.Driver.RunToCompletionAsync("case-happy", "service credit for case 42");

        using var final = JsonDocument.Parse(result.FinalStateJson);
        Assert.True(final.RootElement.GetProperty("notified").GetBoolean());
        Assert.Equal("42", final.RootElement.GetProperty("caseId").GetString());

        Assert.Equal(["credit"], fixture.LedgerEntryTypes());

        var replayed = await new ReconstructionReplayEngine(fixture.Store).ReplayAsync("case-happy");
        Assert.Equal(result.FinalStateJson, replayed.FinalStateJson);

        // Re-running a completed session is a no-op: still exactly one credit.
        var again = await fixture.Driver.RunToCompletionAsync("case-happy", "service credit for case 42");
        Assert.Equal(result.FinalStateJson, again.FinalStateJson);
        Assert.Equal(["credit"], fixture.LedgerEntryTypes());
    }

    [Fact]
    public async Task Failed_notify_reverses_the_credit_and_parks_the_session()
    {
        using var fixture = new Fixture(failNotifications: true);
        var result = await fixture.Driver.RunToCompletionAsync("case-saga", "service credit for case 42");

        using var final = JsonDocument.Parse(result.FinalStateJson);
        Assert.Equal("failed", final.RootElement.GetProperty("status").GetString());
        Assert.Equal("case-saga/0/2",
            final.RootElement.GetProperty("compensatedKeys")[0].GetString());
        Assert.Equal(0, final.RootElement.GetProperty("uncompensatedKeys").GetArrayLength());

        Assert.Equal(["credit", "reversal"], fixture.LedgerEntryTypes());

        var replayed = await new ReconstructionReplayEngine(fixture.Store).ReplayAsync("case-saga");
        Assert.Equal(result.FinalStateJson, replayed.FinalStateJson);
    }

    [Fact]
    public async Task Flaky_compensation_is_retried_under_the_same_key()
    {
        using var fixture = new Fixture(failNotifications: true, compensationFailures: 1);
        var result = await fixture.Driver.RunToCompletionAsync("case-flaky", "service credit for case 42");

        using var final = JsonDocument.Parse(result.FinalStateJson);
        Assert.Equal("failed", final.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, final.RootElement.GetProperty("compensatedKeys").GetArrayLength());

        var log = await fixture.Store.ReadAsync("case-flaky");
        var compensation = Assert.Single(log.OfType<CompensationRequested>());
        Assert.Single(log.OfType<CallAttemptFailed>(),
            f => f.IdempotencyKey == compensation.IdempotencyKey);
        Assert.Equal(["credit", "reversal"], fixture.LedgerEntryTypes());
    }

    [Fact]
    public async Task Exhausted_compensation_is_reported_as_uncompensated()
    {
        using var fixture = new Fixture(failNotifications: true, compensationFailures: 99);
        var result = await fixture.Driver.RunToCompletionAsync("case-stuck", "service credit for case 42");

        using var final = JsonDocument.Parse(result.FinalStateJson);
        Assert.Equal("failed", final.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, final.RootElement.GetProperty("compensatedKeys").GetArrayLength());
        Assert.Equal("case-stuck/0/2",
            final.RootElement.GetProperty("uncompensatedKeys")[0].GetString());

        // The credit leaked (reversal never landed) — and the log says so.
        Assert.Equal(["credit"], fixture.LedgerEntryTypes());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("aurelia-case-");

        public FileEventStore Store { get; }
        public ScriptedAgentDriver Driver { get; }

        public Fixture(bool failNotifications, int compensationFailures = 0)
        {
            Store = new FileEventStore(_dir.FullName);
            var ledger = Path.Combine(_dir.FullName, "ledger.jsonl");
            var credit = compensationFailures > 0
                ? new FlakyCompensationTool(new ServiceCreditTool(ledger), compensationFailures)
                : (ITool)new ServiceCreditTool(ledger);
            var tools = new Dictionary<string, ITool>
            {
                ["lookup_case"] = new CaseLookupTool(),
                ["issue_service_credit"] = credit,
                ["notify_customer"] = new NotifyCustomerTool(failNotifications),
            };
            Driver = new ScriptedAgentDriver(Store, tools, CaseResolutionAgent.Script);
        }

        public string[] LedgerEntryTypes()
        {
            var ledger = Path.Combine(_dir.FullName, "ledger.jsonl");
            if (!File.Exists(ledger))
                return [];
            return [.. File.ReadLines(ledger)
                .Select(l => JsonDocument.Parse(l).RootElement.GetProperty("type").GetString()!)];
        }

        public void Dispose() => _dir.Delete(recursive: true);
    }

    private sealed class FlakyCompensationTool(ServiceCreditTool inner, int failures) : ICompensatableTool
    {
        private int _failed;

        public string Name => inner.Name;

        public Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default)
            => inner.ExecuteAsync(argumentsJson, idempotencyKey, ct);

        public Task<string> CompensateAsync(
            string originalArgumentsJson, string originalResultJson, string compensationKey,
            CancellationToken ct = default)
        {
            if (_failed < failures)
            {
                _failed++;
                throw new InvalidOperationException("ledger service unavailable");
            }
            return inner.CompensateAsync(originalArgumentsJson, originalResultJson, compensationKey, ct);
        }
    }
}
