// The single source of the derived-state JSON shapes, so the driver (which
// returns them live) and the replay engine (which reconstructs them) can never
// drift apart.
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public static class SessionStates
{
    public static string FailedJson(SessionFailed failed) =>
        JsonSerializer.Serialize(new
        {
            status = "failed",
            reason = failed.Reason,
            compensatedKeys = failed.CompensatedKeys,
            uncompensatedKeys = failed.UncompensatedKeys,
        });

    public static string AwaitingApprovalJson(ApprovalRequested approval) =>
        JsonSerializer.Serialize(new
        {
            status = "awaiting_approval",
            approvalKey = approval.IdempotencyKey,
            question = approval.Question,
            approverPolicy = approval.ApproverPolicy,
        });
}
