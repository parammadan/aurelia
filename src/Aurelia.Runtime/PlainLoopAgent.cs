// The benchmark floor baseline: what most production agents actually are —
// an in-memory loop over the same tools, with no durable state, no
// idempotency-key reuse across attempts, and no recovery. It exists to be
// measured against, honestly and idiomatically naive.
using Aurelia.Core;

namespace Aurelia.Runtime;

public static class PlainLoopAgent
{
    public static async Task<string> RunAsync(
        IReadOnlyDictionary<string, ITool> tools, string goal, CancellationToken ct = default)
    {
        // Fresh random keys per attempt — exactly what ad-hoc agents do, and
        // exactly why their retries double side effects.
        static string Key() => $"plain-{Guid.NewGuid():N}";

        var found = await tools["lookup_case"].ExecuteAsync(goal, Key(), ct);
        var credit = await tools["issue_service_credit"].ExecuteAsync(found, Key(), ct);
        return await tools["notify_customer"].ExecuteAsync(credit, Key(), ct);
    }
}
