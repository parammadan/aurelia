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

    /// <summary>The triage variant: a durable model call classifies the
    /// free-text request and extracts the case id before the tool steps.</summary>
    public static readonly IReadOnlyList<AgentStep> TriageScript =
    [
        new ModelStep("classify", (goal, _) => JsonSerializer.Serialize(new object[]
        {
            new { role = "system", content = "Extract the customer-service case number from the "
                + "message. Reply with ONLY a JSON object of the shape {\"caseId\":\"<number>\"}." },
            new { role = "user", content = goal },
        })),
        new ScriptStep("lookup_case", (_, results) => results["classify"]),
        ManagerApproval,
        new ScriptStep("issue_service_credit", (_, results) => results["lookup_case"]),
        new ScriptStep("notify_customer", (_, results) => results["issue_service_credit"]),
    ];

    public static ScriptedAgentDriver CreateDriver(
        IEventStore store, string storeDir, bool failNotifications = false) =>
        new(store, CreateTools(storeDir, failNotifications), Script);

    public static ScriptedAgentDriver CreateTriageDriver(
        IEventStore store, string storeDir, IModelClient modelClient, bool failNotifications = false) =>
        new(store, CreateTools(storeDir, failNotifications), TriageScript, modelClient);

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
        var caseId = new string(argumentsJson.Where(char.IsDigit).ToArray());
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
        // Crash-harness hook: lets the kill test land SIGKILL mid-issuance.
        await BlockableSignals.SignalAndWaitAsync(idempotencyKey, ct);

        using var args = JsonDocument.Parse(argumentsJson);
        var caseId = args.RootElement.GetProperty("caseId").GetString()!;
        var amountUsd = args.RootElement.GetProperty("entitledCreditUsd").GetDecimal();

        AppendIfAbsent(new { key = idempotencyKey, type = "credit", caseId, amountUsd });
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
