// The Orchestration Reliability Benchmark — pilot entry point, now thin:
// adapters × fault conditions × N through the framework-neutral harness core
// (Contracts / HarnessRunner / Scoring). docs/benchmark/harness.md explains
// how to add a framework adapter.
using System.Text.Json;
using Aurelia.Benchmark;

var n = 10;
var seed = 7;
string? outPath = null;
var toolsMode = "inproc";
string? frameworksFilter = null;
string? faultsFilter = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--n" when i + 1 < args.Length: _ = int.TryParse(args[++i], out n); break;
        case "--seed" when i + 1 < args.Length: _ = int.TryParse(args[++i], out seed); break;
        case "--out" when i + 1 < args.Length: outPath = args[++i]; break;
        case "--tools" when i + 1 < args.Length: toolsMode = args[++i]; break;
        case "--frameworks" when i + 1 < args.Length: frameworksFilter = args[++i]; break;
        case "--faults" when i + 1 < args.Length: faultsFilter = args[++i]; break;
    }
}

// --tools http: the physical dependency boundary — tools, ledger, and fault
// injection live in the harness-owned tool server (see docs/benchmark/harness.md).
string? toolServerDll = null;
if (toolsMode == "http")
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (!File.Exists(Path.Combine(dir.FullName, "Aurelia.slnx")))
            continue;
        toolServerDll = Path.Combine(dir.FullName,
            "src", "Aurelia.ToolServer", "bin", "Release", "net10.0", "Aurelia.ToolServer.dll");
        break;
    }
    if (toolServerDll is null || !File.Exists(toolServerDll))
    {
        Console.Error.WriteLine("tool server not built; run: dotnet build src/Aurelia.ToolServer -c Release");
        return 2;
    }
}

var runtimeDll = Path.Combine(AppContext.BaseDirectory, "Aurelia.Runtime.dll");
if (!File.Exists(runtimeDll))
{
    Console.Error.WriteLine($"runtime not found at {runtimeDll}");
    return 2;
}

var adapters = new List<IFrameworkAdapter>
{
    new AureliaAdapter(runtimeDll),
    new PlainAdapter(runtimeDll, resubmitsOnFailure: true),
    new PlainAdapter(runtimeDll, resubmitsOnFailure: false),
};

// LangGraph joins only behind the physical tool boundary, and only where its
// venv exists (benchmark/adapters/langgraph/README-less setup: python3 -m venv
// .venv && .venv/bin/pip install -r requirements.txt).
if (toolServerDll is not null)
{
    var lgDir = Path.Combine(
        Directory.GetParent(toolServerDll)!.Parent!.Parent!.Parent!.Parent!.Parent!.FullName,
        "benchmark", "adapters", "langgraph");
    var lgPython = Path.Combine(lgDir, ".venv", "bin", "python");
    var lgScript = Path.Combine(lgDir, "case_resolution.py");
    if (File.Exists(lgPython) && File.Exists(lgScript))
    {
        adapters.Add(new LangGraphAdapter(lgPython, lgScript, uuidKeys: false));
        adapters.Add(new LangGraphAdapter(lgPython, lgScript, uuidKeys: true));
    }

    // Temporal joins when its venv exists AND a dev server is reachable
    // (TEMPORAL_ADDRESS, e.g. from `temporal server start-dev --headless`).
    var tpDir = Path.Combine(Path.GetDirectoryName(lgDir)!, "temporal");
    var tpPython = Path.Combine(tpDir, ".venv", "bin", "python");
    var tpScript = Path.Combine(tpDir, "case_resolution.py");
    if (File.Exists(tpPython) && File.Exists(tpScript)
        && Environment.GetEnvironmentVariable("TEMPORAL_ADDRESS") is not null)
    {
        adapters.Add(new TemporalAdapter(tpPython, tpScript, uuidKeys: false));
        adapters.Add(new TemporalAdapter(tpPython, tpScript, uuidKeys: true));
    }
}

if (frameworksFilter is not null)
{
    var wanted = frameworksFilter.Split(',', StringSplitOptions.TrimEntries);
    adapters = [.. adapters.Where(a => wanted.Any(w => a.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))];
}
string[] faults = faultsFilter is null
    ? ["F0", "F1", "F6"]
    : faultsFilter.Split(',', StringSplitOptions.TrimEntries);

var allRuns = new List<object>();
Console.WriteLine($"pilot: N={n} per cell, seed={seed}, tools={toolsMode}, goal='credit for case 42'");
Console.WriteLine($"{"framework",-14} {"fault",-5} {"RR",-7} {"CAF",-7} violations");

foreach (var fault in faults)
{
    foreach (var adapter in adapters)
    {
        var cell = new List<RunScore>();
        for (var i = 0; i < n; i++)
            cell.Add(await HarnessRunner.RunOnceAsync(adapter, fault, seed + i, toolServerDll));
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
