// The case-resolution reference agent: a Dynamics 365 Contact Center-style
// scenario — look up the case, issue a service credit (compensatable side
// effect against a ledger), notify the customer. Exercises the two-phase call
// protocol, exactly-once via the ledger's key dedupe, and the saga unwind.
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public static class CaseResolutionAgent
{
    /// <summary>Credits above this threshold pause for manager approval
    /// (ADR-0011); at or below it, the gate never triggers.</summary>
    public const decimal ApprovalThresholdUsd = 30m;

    private static readonly ApprovalGate ManagerApproval = new((_, results) =>
    {
        using var found = JsonDocument.Parse(results["lookup_case"]);
        var amount = found.RootElement.GetProperty("entitledCreditUsd").GetDecimal();
        return amount > ApprovalThresholdUsd
            ? $"Approve service credit of ${amount} for case " +
              $"{found.RootElement.GetProperty("caseId").GetString()}?"
            : null;
    }, "contact-center-manager");

    public static readonly IReadOnlyList<AgentStep> Script =
    [
        new ScriptStep("lookup_case", (goal, _) => goal),
        ManagerApproval,
        new ScriptStep("issue_service_credit", (_, results) => results["lookup_case"]),
        new ScriptStep("notify_customer", (_, results) => results["issue_service_credit"]),
    ];

    private static readonly ModelStep ClassifyStep = new("classify",
        (goal, _) => JsonSerializer.Serialize(new object[]
        {
            new { role = "system", content = "You are a text-extraction function. The user message "
                + "mentions a case number. Output exactly this JSON and nothing else, no prose, no "
                + "code fences: {\"caseId\":\"<the number>\"}" },
            new { role = "user", content = goal },
        }), RouteClass: "classify");

    /// <summary>The triage variant: a durable model call classifies the
    /// free-text request and extracts the case id before the tool steps.</summary>
    public static readonly IReadOnlyList<AgentStep> TriageScript =
    [
        ClassifyStep,
        new ScriptStep("lookup_case", (_, results) => results["classify"]),
        ManagerApproval,
        new ScriptStep("issue_service_credit", (_, results) => results["lookup_case"]),
        new ScriptStep("notify_customer", (_, results) => results["issue_service_credit"]),
    ];

    /// <summary>The multi-agent variant (ADR-0023): classify, then hand the
    /// case to the case-resolution agent running as a child session.</summary>
    public static readonly IReadOnlyList<AgentStep> ConciergeScript =
    [
        ClassifyStep,
        new SubAgentStep("case-resolution", (_, results) => results["classify"]),
    ];

    public static ScriptedAgentDriver CreateDriver(
        IEventStore store, string storeDir, bool failNotifications = false, ChaosSource? chaos = null)
    {
        var tools = CreateTools(storeDir, failNotifications);
        return new(store, chaos is null ? tools : chaos.WrapTools(tools), Script);
    }

    public static ScriptedAgentDriver CreateTriageDriver(
        IEventStore store, string storeDir, IModelClient modelClient, bool failNotifications = false,
        IModelClient? fallback = null, ChaosSource? chaos = null)
    {
        if (chaos is not null)
        {
            modelClient = chaos.WrapModel(modelClient);
            fallback = fallback is null ? null : chaos.WrapModel(fallback);
        }
        // Latency budget for the classify route, if configured (ADR-0022).
        TimeSpan? budget = int.TryParse(Environment.GetEnvironmentVariable("AURELIA_MODEL_BUDGET_MS"), out var ms) && ms > 0
            ? TimeSpan.FromMilliseconds(ms)
            : null;
        var policy = new ModelRoutingPolicy("triage-v1", new Dictionary<string, ModelRoute>
        {
            ["classify"] = new(modelClient, fallback, budget),
            ["general"] = new(modelClient),
        });
        var tools = CreateTools(storeDir, failNotifications);
        return new(store, chaos is null ? tools : chaos.WrapTools(tools), TriageScript, policy);
    }

    public static ScriptedAgentDriver CreateConciergeDriver(
        IEventStore store, string storeDir, IModelClient modelClient, bool failNotifications = false,
        ChaosSource? chaos = null)
    {
        if (chaos is not null)
            modelClient = chaos.WrapModel(modelClient);
        return new(store, new Dictionary<string, ITool>(), ConciergeScript,
            ModelRoutingPolicy.Single(modelClient, "concierge-v1"),
            subAgents: (agent, childSessionId, childGoal, ct) => agent switch
            {
                // Same store: the child's log lives beside the parent's.
                "case-resolution" => CreateDriver(store, storeDir, failNotifications, chaos)
                    .RunToCompletionAsync(childSessionId, childGoal, ct),
                _ => throw new InvalidOperationException($"unknown sub-agent '{agent}'"),
            });
    }

    public static IReadOnlyDictionary<string, ITool> CreateTools(
        string storeDir, bool failNotifications = false)
    {
        var tools = new ITool[]
        {
            new CaseLookupTool(),
            new ServiceCreditTool(Path.Combine(storeDir, "ledger.jsonl")),
            new NotifyCustomerTool(failNotifications),
        };
        return tools.ToDictionary(t => t.Name);
    }
}

/// <summary>Read-only stand-in for a Dataverse case lookup.</summary>
public sealed class CaseLookupTool : ITool
{
    private static readonly Dictionary<string, (string Customer, decimal CreditUsd)> Cases = new()
    {
        ["42"] = ("Contoso Ltd", 25m),
        ["7"] = ("Fabrikam Inc", 40m),
    };

    public string Name => "lookup_case";

    public Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default)
    {
        // JSON-first ({"caseId":"42"} from the classifier), digit-stripping
        // fallback for free-text goals.
        string caseId;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            caseId = doc.RootElement.GetProperty("caseId").GetString() ?? "";
        }
        catch (JsonException)
        {
            caseId = new string(argumentsJson.Where(char.IsDigit).ToArray());
        }
        if (!Cases.TryGetValue(caseId, out var found))
            throw new InvalidOperationException($"case '{caseId}' not found");
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            caseId,
            customer = found.Customer,
            entitledCreditUsd = found.CreditUsd,
        }));
    }
}

/// <summary>
/// The side-effectful, compensatable step. The "ledger" is a JSONL file;
/// entries are deduplicated by idempotency key, which is what makes issuance
/// exactly-once across crashes and retries. Compensation appends a reversal
/// under the compensation's own key (ADR-0015/0016).
/// </summary>
public sealed class ServiceCreditTool(string ledgerPath) : ICompensatableTool
{
    private static readonly object Gate = new();

    public string Name => "issue_service_credit";

    public async Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default)
    {
        // Crash-harness hook: lets a kill land mid-issuance. With
        // AURELIA_SIGNAL_AFTER_EFFECT=1 the window is AFTER the ledger write —
        // the effect happened, the caller never learned it. That's the case
        // that separates exactly-once runtimes from everything else.
        var afterEffect = Environment.GetEnvironmentVariable("AURELIA_SIGNAL_AFTER_EFFECT") == "1";
        if (!afterEffect)
            await BlockableSignals.SignalAndWaitAsync(idempotencyKey, ct);

        using var args = JsonDocument.Parse(argumentsJson);
        var caseId = args.RootElement.GetProperty("caseId").GetString()!;
        var amountUsd = args.RootElement.GetProperty("entitledCreditUsd").GetDecimal();

        AppendIfAbsent(new { key = idempotencyKey, type = "credit", caseId, amountUsd });
        if (afterEffect)
            await BlockableSignals.SignalAndWaitAsync(idempotencyKey, ct);
        return JsonSerializer.Serialize(new { creditId = idempotencyKey, caseId, amountUsd });
    }

    public Task<string> CompensateAsync(
        string originalArgumentsJson, string originalResultJson, string compensationKey,
        CancellationToken ct = default)
    {
        using var original = JsonDocument.Parse(originalResultJson);
        var creditId = original.RootElement.GetProperty("creditId").GetString()!;
        var caseId = original.RootElement.GetProperty("caseId").GetString()!;
        var amountUsd = original.RootElement.GetProperty("amountUsd").GetDecimal();

        AppendIfAbsent(new { key = compensationKey, type = "reversal", caseId, amountUsd, reverses = creditId });
        return Task.FromResult(JsonSerializer.Serialize(new { reversalId = compensationKey, reverses = creditId }));
    }

    private void AppendIfAbsent(object entry)
    {
        var line = JsonSerializer.Serialize(entry);
        var key = JsonDocument.Parse(line).RootElement.GetProperty("key").GetString()!;
        lock (Gate)
        {
            if (File.Exists(ledgerPath) && File.ReadLines(ledgerPath).Any(existing =>
                    JsonDocument.Parse(existing).RootElement.GetProperty("key").GetString() == key))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ledgerPath))!);
            File.AppendAllText(ledgerPath, line + Environment.NewLine);
        }
    }
}

/// <summary>Notification stand-in; failure is injectable to demo the saga
/// unwind ("credit issued, notify failed → credit reversed").</summary>
public sealed class NotifyCustomerTool(bool failing) : ITool
{
    public string Name => "notify_customer";

    public Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default)
    {
        if (failing)
            throw new InvalidOperationException("notification channel unavailable");
        using var credit = JsonDocument.Parse(argumentsJson);
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            notified = true,
            creditId = credit.RootElement.GetProperty("creditId").GetString(),
            caseId = credit.RootElement.GetProperty("caseId").GetString(),
        }));
    }
}
