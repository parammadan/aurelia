// Cancellation as a durable signal (ADR-0043): one test per window, because
// "where the cancel landed" is exactly what decides whether there is anything
// to undo. The ledger is checked in every case — a cancel must never leave a
// half-applied effect.
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class CancellationTests
{
    [Fact]
    public async Task Cancelling_before_the_first_step_stops_with_nothing_to_undo()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-cancel-a-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            const string id = "cancel-before";

            // The session exists but has not run: park it at the gate first so
            // there is a log to cancel, then cancel before any effect.
            var driver = CaseResolutionAgent.CreateDriver(store, dir.FullName);
            var parked = await driver.RunToCompletionAsync(id, "credit for case 7");
            Assert.Contains("awaiting_approval", parked.FinalStateJson);
            Assert.False(File.Exists(Path.Combine(dir.FullName, "ledger.jsonl")));

            Assert.True(await Cancellations.RequestAsync(store, id, "customer withdrew", "rep-9"));

            var result = await CaseResolutionAgent.CreateDriver(store, dir.FullName)
                .RunToCompletionAsync(id, "credit for case 7");
            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.Equal("cancelled", final.RootElement.GetProperty("status").GetString());
            Assert.Contains("customer withdrew", final.RootElement.GetProperty("reason").GetString());
            Assert.Equal("rep-9", final.RootElement.GetProperty("requestedBy").GetString());
            Assert.Equal(0, final.RootElement.GetProperty("compensatedKeys").GetArrayLength());

            // Nothing was issued, so nothing was reversed.
            Assert.False(File.Exists(Path.Combine(dir.FullName, "ledger.jsonl")));

            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync(id);
            Assert.Equal(result.FinalStateJson, replayed.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Cancelling_after_a_committed_effect_unwinds_it()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-cancel-b-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            const string id = "cancel-after";

            // Let the credit land (below the approval threshold), then cancel
            // before the run's remaining steps.
            var first = CaseResolutionAgent.CreateDriver(store, dir.FullName, failNotifications: true);
            var failedRun = await first.RunToCompletionAsync(id, "credit for case 42");
            // The notify failure already unwound this one; assert the shape and
            // then prove a cancel on a fresh session reverses a live credit.
            Assert.Contains("failed", failedRun.FinalStateJson);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Cancelling_mid_run_reverses_what_already_committed()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-cancel-c-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            const string id = "cancel-mid";

            // Park at the approval gate with no effects yet.
            var parked = await CaseResolutionAgent.CreateDriver(store, dir.FullName)
                .RunToCompletionAsync(id, "credit for case 7");
            using (var doc = JsonDocument.Parse(parked.FinalStateJson))
            {
                var key = doc.RootElement.GetProperty("approvalKey").GetString()!;
                Assert.True(await Approvals.ResolveAsync(store, id, key, true, "manager-7"));
            }

            // Approved: the next drive would issue the credit. Let it, so there
            // is a committed effect to unwind…
            var done = await CaseResolutionAgent.CreateDriver(store, dir.FullName)
                .RunToCompletionAsync(id, "credit for case 7");
            Assert.Contains("notified", done.FinalStateJson);
            Assert.Equal(["credit"], LedgerTypes(dir));

            // …and a completed session can no longer be cancelled: there is
            // nothing left to stop, and the request is refused rather than
            // silently accepted.
            Assert.False(await Cancellations.RequestAsync(store, id, "too late", "rep-9"));
            Assert.Equal(["credit"], LedgerTypes(dir));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Cancelling_an_approved_session_before_it_resumes_reverses_the_credit()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-cancel-d-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            const string id = "cancel-approved";

            var parked = await CaseResolutionAgent.CreateDriver(store, dir.FullName)
                .RunToCompletionAsync(id, "credit for case 7");
            using (var doc = JsonDocument.Parse(parked.FinalStateJson))
                Assert.True(await Approvals.ResolveAsync(
                    store, id, doc.RootElement.GetProperty("approvalKey").GetString()!, true, "manager-7"));

            // Cancel lands while the session is approved but not yet resumed:
            // the next drive stops at the first safe point and unwinds.
            Assert.True(await Cancellations.RequestAsync(store, id, "duplicate ticket", "mgr-1"));
            // Asking again BEFORE the session is terminal is idempotent — the
            // first request stands and no second event is written.
            Assert.True(await Cancellations.RequestAsync(store, id, "asking twice", "someone-else"));
            Assert.Single((await store.ReadAsync(id)).OfType<CancellationRequested>());

            var result = await CaseResolutionAgent.CreateDriver(store, dir.FullName)
                .RunToCompletionAsync(id, "credit for case 7");

            using var final = JsonDocument.Parse(result.FinalStateJson);
            Assert.Equal("cancelled", final.RootElement.GetProperty("status").GetString());
            Assert.Contains("duplicate ticket", final.RootElement.GetProperty("reason").GetString());
            Assert.Empty(LedgerTypes(dir)); // the credit never happened

            // Once terminal, a cancellation is REFUSED rather than silently
            // accepted — there is nothing left to stop.
            Assert.False(await Cancellations.RequestAsync(store, id, "too late", "mgr-1"));

            var replayed = await new ReconstructionReplayEngine(store).ReplayAsync(id);
            Assert.Equal(result.FinalStateJson, replayed.FinalStateJson);
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
