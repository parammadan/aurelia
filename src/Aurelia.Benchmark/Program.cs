// The Orchestration Reliability Benchmark — pilot entry point, now thin:
// adapters × fault conditions × N through the framework-neutral harness core
// (Contracts / HarnessRunner / Scoring). docs/benchmark/harness.md explains
// how to add a framework adapter.
using System.Text.Json;
using Aurelia.Benchmark;

var n = 10;
var seed = 7;
string? outPath = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--n" when i + 1 < args.Length: _ = int.TryParse(args[++i], out n); break;
        case "--seed" when i + 1 < args.Length: _ = int.TryParse(args[++i], out seed); break;
        case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
    }
}

var runtimeDll = Path.Combine(AppContext.BaseDirectory, "Aurelia.Runtime.dll");
if (!File.Exists(runtimeDll))
{
    Console.Error.WriteLine($"runtime not found at {runtimeDll}");
    return 2;
}

IFrameworkAdapter[] adapters =
[
    new AureliaAdapter(runtimeDll),
    new PlainAdapter(runtimeDll, resubmitsOnFailure: true),
    new PlainAdapter(runtimeDll, resubmitsOnFailure: false),
];
string[] faults = ["F0", "F1", "F6"];

var allRuns = new List<object>();
Console.WriteLine($"pilot: N={n} per cell, seed={seed}, goal='credit for case 42'");
Console.WriteLine($"{"framework",-14} {"fault",-5} {"RR",-7} {"CAF",-7} violations");

foreach (var fault in faults)
{
    foreach (var adapter in adapters)
    {
        var cell = new List<RunScore>();
        for (var i = 0; i < n; i++)
            cell.Add(await HarnessRunner.RunOnceAsync(adapter, fault, seed + i));
        var rr = cell.Count(r => r.Correct);
        var caf = cell.Count(r => r is { I1: true, I2: true, I3: true, I4: true });
        var violations = string.Join(" ",
            new[] { ("I1", cell.Count(r => !r.I1)), ("I2", cell.Count(r => !r.I2)),
                    ("I3", cell.Count(r => !r.I3)), ("I4", cell.Count(r => !r.I4)) }
            .Where(v => v.Item2 > 0).Select(v => $"{v.Item1}×{v.Item2}"));
        Console.WriteLine($"{adapter.Name,-14} {fault,-5} {rr,2}/{n,-4} {caf,2}/{n,-4} {violations}");
        allRuns.AddRange(cell.Select(r => new
        {
            framework = adapter.Name, fault, r.Correct, r.I1, r.I2, r.I3, r.I4,
            r.Credits, r.Reversals, r.Claimed,
        }));
    }
}

if (outPath is not null)
{
    File.WriteAllText(outPath, JsonSerializer.Serialize(allRuns,
        new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"raw runs → {outPath}");
}
return 0;
