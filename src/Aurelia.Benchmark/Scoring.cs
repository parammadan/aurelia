// Neutral scoring (methodology §2): reads ONLY harness-owned ground truth
// (the ledger) plus the adapter's own claims and terminal report. No
// framework internals.
using System.Text.Json;

namespace Aurelia.Benchmark;

public static class Scoring
{
    public static RunScore Score(
        TaskAssignment task, IFrameworkAdapter adapter,
        int exitCode, string stdout, TerminalReport? report)
    {
        var ledgerPath = Path.Combine(task.StoreDir, "ledger.jsonl");
        var entries = !File.Exists(ledgerPath)
            ? []
            : File.ReadLines(ledgerPath)
                .Select(l => JsonDocument.Parse(l).RootElement.GetProperty("type").GetString()!)
                .ToArray();
        var credits = entries.Count(t => t == "credit");
        var reversals = entries.Count(t => t == "reversal");

        var claimed = adapter.ClaimsSuccess(exitCode, stdout);

        // I1 — exactly-once: at most one credit, ever.
        var i1 = credits <= 1;
        // I2 — no silent leakage: success, or no effect, or effect reversed,
        // or an explicit failure report accounting for the effects.
        var i2 = claimed || credits == 0 || reversals >= credits || report?.Status == "failed";
        // I3 — terminal legibility: success, a queryable terminal report, or
        // at minimum an explicit error exit.
        var i3 = claimed || report is not null || (exitCode == 1 && stdout.Contains("ERROR"));
        // I4 — no fabrication: claiming success without the effect is worse
        // than failing honestly.
        var i4 = !(claimed && credits == 0);

        var correct = claimed && credits == 1;
        return new RunScore(correct, i1, i2, i3, i4, credits, reversals, claimed);
    }
}
