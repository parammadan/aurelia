// The framework-neutral benchmark contract (methodology §4): a framework
// under test provides an adapter — how to launch a worker for the task, what
// its documented recovery story is, and how to query its terminal state. The
// harness owns everything else: the task, the ledger, fault injection, kills,
// and scoring.
using System.Diagnostics;

namespace Aurelia.Benchmark;

/// <summary>One task instance: isolated directories, the goal, and the
/// harness-provided environment (fault spec, signal protocol).</summary>
public sealed record TaskAssignment(
    string TaskId,
    string Goal,
    string StoreDir,
    string SignalDir,
    IReadOnlyDictionary<string, string> Env);

/// <summary>The framework's own account of a finished task, used for the
/// terminal-legibility (I3) and explicit-accounting (I2) invariants. Null
/// from the query means "this framework cannot answer what happened".</summary>
public sealed record TerminalReport(
    string Status, // completed | failed
    IReadOnlyList<string> CompensatedEffects,
    IReadOnlyList<string> LeakedEffects);

public interface IFrameworkAdapter
{
    string Name { get; }

    /// <summary>How to launch a worker for the task, per the framework's own
    /// documented production configuration.</summary>
    ProcessStartInfo StartInfo(TaskAssignment task);

    /// <summary>The framework's documented recovery action after its worker
    /// dies. Null means the framework has no recovery story.</summary>
    ProcessStartInfo? RecoveryStartInfo(TaskAssignment task);

    /// <summary>Query the framework's durable state surface for what happened
    /// to the task. Null means no queryable answer exists (fails I3 unless
    /// the run completed).</summary>
    Task<TerminalReport?> QueryTerminalStateAsync(TaskAssignment task, CancellationToken ct);

    /// <summary>Whether the framework claims the task succeeded, from its
    /// worker's exit. Checked against the ledger for I4 (no fabrication).</summary>
    bool ClaimsSuccess(int exitCode, string stdout);
}

public sealed record RunScore(
    bool Correct, bool I1, bool I2, bool I3, bool I4,
    int Credits, int Reversals, bool Claimed);
