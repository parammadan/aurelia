// Routing policy (ADR-0021): a named, deterministic map from route class to
// backend. Resolution is a pure lookup — same step + same policy = same
// choice — which is what recovery and replay require. Budgets and fallback
// per ADR-0022.
using System.Text.Json;

namespace Aurelia.Runtime;

public sealed record ModelRoute(
    IModelClient Primary,
    IModelClient? Fallback = null,
    TimeSpan? LatencyBudget = null);

public sealed class ModelRoutingPolicy(string name, IReadOnlyDictionary<string, ModelRoute> routes)
{
    public string Name => name;

    public ModelRoute Resolve(string routeClass) =>
        routes.TryGetValue(routeClass, out var route) ? route
        : routes.TryGetValue("general", out var general) ? general
        : throw new InvalidOperationException(
            $"routing policy '{name}' has no route for class '{routeClass}' and no 'general' default");

    public static ModelRoutingPolicy Single(IModelClient client, string name = "single") =>
        new(name, new Dictionary<string, ModelRoute> { ["general"] = new(client) });
}
