// The local playground: start sessions in the browser, watch the event log
// live, and press the chaos button — which SIGKILLs the real worker process
// (the same Aurelia.Runtime host CI kills) and restarts a fresh one that
// resumes from the log. Real process death, not a simulation.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Aurelia.Core;
using Aurelia.Runtime;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
if (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") is null)
    app.Urls.Add("http://localhost:5199");
app.UseDefaultFiles();
app.UseStaticFiles();

var dataRoot = Path.Combine(Path.GetTempPath(), "aurelia-playground");
Directory.CreateDirectory(dataRoot);
var runtimeDll = LocateRuntimeDll();
var sessions = new ConcurrentDictionary<string, SessionHandle>();

// The worker must run against its own deps.json (the web app resolves shared
// framework assemblies the console host needs copied locally), so launch it
// from the Runtime project's own build output.
static string LocateRuntimeDll()
{
    var config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}")
        ? "Debug" : "Release";
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (!File.Exists(Path.Combine(dir.FullName, "Aurelia.slnx")))
            continue;
        var dll = Path.Combine(dir.FullName, "src", "Aurelia.Runtime", "bin", config, "net10.0", "Aurelia.Runtime.dll");
        if (File.Exists(dll))
            return dll;
    }
    return Path.Combine(AppContext.BaseDirectory, "Aurelia.Runtime.dll");
}

app.MapPost("/api/sessions", (StartRequest req) =>
{
    var id = $"s-{DateTime.UtcNow:HHmmss}-{Random.Shared.Next(1000, 9999)}";
    var handle = new SessionHandle(id,
        req.Agent is "echo" or "case-triage" ? req.Agent : "case-resolution",
        string.IsNullOrWhiteSpace(req.Goal) ? "credit for case 42" : req.Goal,
        Path.Combine(dataRoot, id), runtimeDll, req.SabotageNotify);
    sessions[id] = handle;
    handle.StartWorker();
    return Results.Json(new { sessionId = id });
});

app.MapGet("/api/sessions/{id}", async (string id) =>
    sessions.TryGetValue(id, out var handle)
        ? Results.Json(await handle.SnapshotAsync())
        : Results.NotFound());

app.MapPost("/api/sessions/{id}/chaos", (string id) =>
{
    if (!sessions.TryGetValue(id, out var handle))
        return Results.NotFound();
    var killed = handle.KillWorker();
    if (killed)
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500); // let the death be visible before recovery
            await handle.StartWorkerIfUnfinishedAsync();
        });
    return Results.Json(new { killed });
});

app.MapPost("/api/sessions/{id}/approve", async (string id, ApproveRequest req) =>
{
    if (!sessions.TryGetValue(id, out var handle))
        return Results.NotFound();
    var resolved = await Approvals.ResolveAsync(
        handle.Store, id, req.Key, req.Approved, req.Approver ?? "playground-user");
    if (resolved)
        await handle.StartWorkerIfUnfinishedAsync();
    return Results.Json(new { resolved });
});

// The trace viewer reads any session straight from its event log — playground
// sessions, CLI sessions under the same data root, or Cosmos history when
// AURELIA_COSMOS_ENDPOINT is set. Session-id validation prevents traversal.
app.MapGet("/api/trace/{id}", async (string id) =>
{
    if (!SessionIds.IsValid(id))
        return Results.BadRequest("invalid session id");
    var store = EventStores.FromEnvironment(Path.Combine(dataRoot, id));
    var events = await store.ReadAsync(id);
    return events.Count == 0
        ? Results.NotFound()
        : Results.Json(Aurelia.Playground.TraceBuilder.Build(id, events));
});

app.MapPost("/api/sessions/{id}/replay", async (string id) =>
{
    if (!sessions.TryGetValue(id, out var handle))
        return Results.NotFound();
    try
    {
        var replayed = await new ReconstructionReplayEngine(handle.Store).ReplayAsync(id);
        return Results.Json(new { ok = true, finalState = replayed.FinalStateJson });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
});

app.Run();

record StartRequest(string? Agent, string? Goal, bool SabotageNotify);
record ApproveRequest(string Key, bool Approved, string? Approver);

sealed class SessionHandle(
    string sessionId, string agent, string goal, string storeDir, string runtimeDll, bool sabotageNotify)
{
    private readonly object _gate = new();
    private readonly List<WorkerRecord> _workers = [];
    private Process? _current;

    public IEventStore Store { get; } = EventStores.FromEnvironment(storeDir);

    public void StartWorker()
    {
        lock (_gate)
        {
            if (_current is { HasExited: false })
                return;
            Directory.CreateDirectory(storeDir);
            var psi = new ProcessStartInfo("dotnet")
            {
                ArgumentList =
                {
                    runtimeDll, "run",
                    "--session-id", sessionId,
                    "--goal", goal,
                    "--store-dir", storeDir,
                    "--agent", agent,
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment["AURELIA_STEP_DELAY_MS"] = "3500";
            if (sabotageNotify)
                psi.Environment["AURELIA_FAIL_NOTIFY"] = "1";

            var process = Process.Start(psi)!;
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var record = new WorkerRecord(process.Id);
            _workers.Add(record);
            _current = process;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                lock (_gate)
                    if (record.Fate == "running")
                        record.Fate = "exited";
            };
        }
    }

    public bool KillWorker()
    {
        lock (_gate)
        {
            if (_current is null || _current.HasExited)
                return false;
            _workers[^1].Fate = "killed (SIGKILL)";
            _current.Kill(entireProcessTree: true);
            return true;
        }
    }

    public async Task StartWorkerIfUnfinishedAsync()
    {
        var events = await Store.ReadAsync(sessionId);
        if (events.Any(e => e is SessionCompleted or SessionFailed))
            return;
        StartWorker();
    }

    public async Task<object> SnapshotAsync()
    {
        var events = await Store.ReadAsync(sessionId);

        string status;
        object? approval = null;
        if (events.OfType<SessionCompleted>().Any())
            status = "completed";
        else if (events.OfType<SessionFailed>().Any())
            status = "failed";
        else
        {
            var pending = events.OfType<ApprovalRequested>().FirstOrDefault(r =>
                !events.OfType<ApprovalResolved>().Any(o => o.IdempotencyKey == r.IdempotencyKey));
            if (pending is not null)
            {
                status = "awaiting_approval";
                approval = new { key = pending.IdempotencyKey, question = pending.Question };
            }
            else
            {
                bool running;
                lock (_gate)
                    running = _current is { HasExited: false };
                status = running ? "running" : "worker_down";
            }
        }

        object[] workers;
        lock (_gate)
            workers = [.. _workers.Select(w => new { pid = w.Pid, fate = w.Fate })];

        var ledgerPath = Path.Combine(storeDir, "ledger.jsonl");
        var ledger = !File.Exists(ledgerPath)
            ? []
            : File.ReadLines(ledgerPath).Select(l => JsonDocument.Parse(l).RootElement.Clone()).ToArray();

        return new
        {
            sessionId, agent, goal, status, approval, workers, ledger,
            events = events.Select(Describe).ToArray(),
        };
    }

    private static object Describe(SessionEvent evt)
    {
        var (kind, detail) = evt switch
        {
            SessionStarted e => ("lifecycle", $"session started · goal: {e.Goal}"),
            SessionCompleted e => ("success", $"session completed · {Trim(e.FinalStateJson)}"),
            SessionFailed e => ("failure",
                $"session FAILED · {e.Reason} · compensated: [{string.Join(", ", e.CompensatedKeys)}] · leaked: [{string.Join(", ", e.UncompensatedKeys)}]"),
            ToolCallRequested e => ("intent", $"tool intent · {e.ToolName}({Trim(e.ArgumentsJson)})"),
            ModelCallRequested e => ("intent", $"model intent · {e.ModelId}"),
            ApprovalRequested e => ("approval", $"approval requested · {e.Question}"),
            CompensationRequested e => ("compensation", $"compensate {e.CompensatesKey} via {e.ToolName}"),
            ToolCallSucceeded e => ("success", $"tool result (attempt {e.Attempt}) · {Trim(e.ResultJson)}"),
            ModelCallSucceeded e => ("success", $"model output (attempt {e.Attempt}) · {Trim(e.OutputJson)}"),
            ApprovalResolved e => ("approval", e.Approved ? $"APPROVED by {e.ApproverId}" : $"DENIED by {e.ApproverId}"),
            ApprovalExpired e => ("failure", $"approval expired (attempt {e.Attempt})"),
            TimeProvided e => ("success", $"time injected · {e.Value:O}"),
            RandomProvided e => ("success", $"random injected · {e.Value}"),
            CallAttemptFailed e => ("failure", $"attempt {e.Attempt} failed · {Trim(e.Error)}"),
            CallFailed e => ("failure", $"TERMINAL failure (attempt {e.Attempt}) · {Trim(e.Error)}"),
            _ => ("lifecycle", evt.GetType().Name),
        };
        var key = evt is CallRequested r ? r.IdempotencyKey
            : evt is CallOutcome o ? o.IdempotencyKey : null;
        return new { seq = evt.Sequence, type = evt.GetType().Name, kind, detail, key };
    }

    private static string Trim(string s) => s.Length <= 110 ? s : s[..110] + "…";

    private sealed class WorkerRecord(int pid)
    {
        public int Pid => pid;
        public string Fate { get; set; } = "running";
    }
}
