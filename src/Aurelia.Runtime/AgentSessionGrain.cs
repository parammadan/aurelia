// PERIMETER — AI-generated scaffolding (ADR-0003). Thin Orleans adapter: the
// grain is the addressable surface for a session, but all state lives in the
// event store, never in the grain. Session-channel decoupling starts here.
using Aurelia.Core;

namespace Aurelia.Runtime;

public interface IAgentSessionGrain : IGrainWithStringKey
{
    Task<string> RunToCompletionAsync(string goal);
}

public sealed class AgentSessionGrain(ISessionRuntime runtime) : Grain, IAgentSessionGrain
{
    public async Task<string> RunToCompletionAsync(string goal)
    {
        var result = await runtime.RunToCompletionAsync(this.GetPrimaryKeyString(), goal);
        return result.FinalStateJson;
    }
}
