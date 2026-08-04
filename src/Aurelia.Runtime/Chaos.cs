// Chaos injection as a runtime API (Phase 3): faults are wrappers around
// tools and model clients, driven by a seeded RNG so a chaos run is
// reproducible from its spec. This is the seed of the Orchestration
// Reliability Benchmark: run the eval suite under a fault spec and the pass
// rate IS the recovery rate.
using System.Globalization;
using Aurelia.Core;

namespace Aurelia.Runtime;

/// <summary>Parsed from a spec like "seed=7,tool_fail=0.25,model_malformed=0.1,model_fail=0.1".</summary>
public sealed record ChaosPolicy(
    int Seed = 0,
    double ToolFailureRate = 0,
    double ModelMalformedRate = 0,
    double ModelFailureRate = 0)
{
    public static ChaosPolicy Parse(string spec)
    {
        var policy = new ChaosPolicy();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            var value = double.Parse(kv[1], CultureInfo.InvariantCulture);
            policy = kv[0] switch
            {
                "seed" => policy with { Seed = (int)value },
                "tool_fail" => policy with { ToolFailureRate = value },
                "model_malformed" => policy with { ModelMalformedRate = value },
                "model_fail" => policy with { ModelFailureRate = value },
                _ => throw new ArgumentException($"unknown chaos fault '{kv[0]}'"),
            };
        }
        return policy;
    }
}

public sealed class ChaosSource(ChaosPolicy policy)
{
    private readonly Random _rng = new(policy.Seed);
    private readonly object _gate = new();

    public ChaosPolicy Policy => policy;

    public bool Roll(double rate)
    {
        if (rate <= 0)
            return false;
        lock (_gate)
            return _rng.NextDouble() < rate;
    }

    public IReadOnlyDictionary<string, ITool> WrapTools(IReadOnlyDictionary<string, ITool> tools) =>
        tools.ToDictionary(kv => kv.Key, kv => kv.Value is ICompensatableTool c
            ? new ChaosCompensatableTool(c, this)
            : (ITool)new ChaosTool(kv.Value, this));

    public IModelClient WrapModel(IModelClient inner) => new ChaosModelClient(inner, this);
}

public class ChaosTool(ITool inner, ChaosSource chaos) : ITool
{
    public string Name => inner.Name;

    public Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default)
    {
        if (chaos.Roll(chaos.Policy.ToolFailureRate))
            throw new InvalidOperationException($"chaos: injected failure in '{Name}'");
        return inner.ExecuteAsync(argumentsJson, idempotencyKey, ct);
    }
}

/// <summary>Separate wrapper so a non-compensatable tool never LOOKS
/// compensatable to the unwind (ADR-0016 discovers compensability by type).
/// Compensation itself is not chaos-injected in v0 — that scenario class
/// belongs to the benchmark proper.</summary>
public sealed class ChaosCompensatableTool(ICompensatableTool inner, ChaosSource chaos)
    : ChaosTool(inner, chaos), ICompensatableTool
{
    private readonly ICompensatableTool _inner = inner;

    public Task<string> CompensateAsync(
        string originalArgumentsJson, string originalResultJson, string compensationKey,
        CancellationToken ct = default) =>
        _inner.CompensateAsync(originalArgumentsJson, originalResultJson, compensationKey, ct);
}

public sealed class ChaosModelClient(IModelClient inner, ChaosSource chaos) : IModelClient
{
    public string ModelId => inner.ModelId;
    public string ParametersJson => inner.ParametersJson;
    public string ConfigVersion => inner.ConfigVersion;

    public Task<ModelCompletion> CompleteAsync(string promptJson, CancellationToken ct = default)
    {
        if (chaos.Roll(chaos.Policy.ModelFailureRate))
            throw new InvalidOperationException("chaos: injected model failure");
        if (chaos.Roll(chaos.Policy.ModelMalformedRate))
            // Deliberately digit-free garbage: defeats both JSON parsing and
            // the digit-extraction fallback downstream.
            return Task.FromResult(new ModelCompletion(
                "CHAOS §§ malformed model output — not json, no case number"));
        return inner.CompleteAsync(promptJson, ct);
    }
}

/// <summary>
/// Deterministic endpoint faults (ADR-0046) — the failures a real provider
/// actually produces, on demand rather than by probability. Distinct from
/// <see cref="ChaosModelClient"/> on purpose: this is a fault switch for
/// demonstrating failover, not a statistical experiment.
/// </summary>
public sealed class FaultyModelClient(
    IModelClient inner,
    EndpointFault fault = EndpointFault.RateLimited,
    int failFirst = 1,
    TimeSpan? spike = null) : IModelClient
{
    private int _calls;

    public string ModelId => inner.ModelId;
    public string ParametersJson => inner.ParametersJson;
    public string ConfigVersion => inner.ConfigVersion;

    /// <summary>How many calls this client has been asked to serve, faults
    /// included — the demo shows it to prove the primary was really tried.</summary>
    public int Calls => _calls;

    public async Task<ModelCompletion> CompleteAsync(string promptJson, CancellationToken ct = default)
    {
        var call = Interlocked.Increment(ref _calls);
        if (call <= failFirst)
            switch (fault)
            {
                case EndpointFault.RateLimited:
                    throw new HttpRequestException(
                        $"429 Too Many Requests: rate limit exceeded for model '{inner.ModelId}'");
                case EndpointFault.LatencySpike:
                    // Delegates eventually; the point is to breach the caller's
                    // latency budget exactly as a slow provider would.
                    await Task.Delay(spike ?? TimeSpan.FromSeconds(5), ct);
                    break;
                case EndpointFault.HardFailure:
                    throw new HttpRequestException(
                        $"connection reset by the endpoint serving '{inner.ModelId}'");
            }

        return await inner.CompleteAsync(promptJson, ct);
    }
}

public enum EndpointFault
{
    RateLimited,
    LatencySpike,
    HardFailure,
}
