// What a step builder sees (ADR-0029): the current message, the memory
// window (ADR-0030), and this turn's prior step results. What a builder
// constructs from these is exactly what the intent event records — clean,
// replay-faithful provenance.
namespace Aurelia.Runtime;

/// <summary>One completed conversation turn.</summary>
public sealed record TurnRecord(int Turn, string UserMessage, string AgentMessage);

public sealed record TurnContext(
    /// <summary>The current turn's user message — or the goal, for
    /// single-shot activations.</summary>
    string UserMessage,
    /// <summary>Hot window: recent completed turns, oldest→newest, already
    /// trimmed past any summary checkpoint.</summary>
    IReadOnlyList<TurnRecord> Window,
    /// <summary>The active warm summary covering turns before the window,
    /// or null when none exists yet.</summary>
    string? Summary,
    /// <summary>This turn's step results so far, keyed by step name.</summary>
    IReadOnlyDictionary<string, string> Results);

/// <summary>Memory-tier policy (ADR-0030): how many turns stay verbatim in
/// the hot window, and how often the warm summary is refreshed. Summarization
/// requires a model routing policy with (or defaulting into) a 'summarize'
/// route; without one it is skipped.</summary>
public sealed record MemoryPolicy(int HotTurns = 8, int SummarizeEveryTurns = 10)
{
    public static readonly MemoryPolicy Default = new();
}
