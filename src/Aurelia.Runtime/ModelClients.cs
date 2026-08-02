// Model backends. The runtime only ever sees IModelClient; which model
// answers is configuration (a local Ollama, vLLM on a GPU cluster, or Azure
// OpenAI's OpenAI-compatible endpoint). The durable record of every call —
// full prompt, model id, parameters, config version (ADR-0004) — lives in the
// event log regardless of backend.
using System.Net.Http.Json;
using System.Text.Json;

namespace Aurelia.Runtime;

/// <summary>A completion plus its token usage — recorded on the outcome event
/// so the log doubles as the cost record. Counts are null when the backend
/// doesn't report usage.</summary>
public sealed record ModelCompletion(string Content, int? InputTokens = null, int? OutputTokens = null);

public interface IModelClient
{
    string ModelId { get; }
    string ParametersJson { get; }
    string ConfigVersion { get; }

    /// <summary>Takes a chat-messages JSON array, returns the assistant text
    /// with usage.</summary>
    Task<ModelCompletion> CompleteAsync(string promptJson, CancellationToken ct = default);
}

/// <summary>Deterministic stand-in for CI and offline runs: extracts digits
/// from the prompt and answers with a case-classification JSON.</summary>
public sealed class FakeModelClient : IModelClient
{
    private int _calls;

    public string ModelId => "fake-deterministic";
    public string ParametersJson => """{"temperature":0}""";
    public string ConfigVersion => "fake-v1";
    public int Calls => _calls;

    public Task<ModelCompletion> CompleteAsync(string promptJson, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _calls);
        // Classify from the user message only — the raw prompt JSON contains
        // escape sequences and examples whose digits would contaminate this.
        using var prompt = JsonDocument.Parse(promptJson);
        var userText = prompt.RootElement.EnumerateArray()
            .Where(m => m.GetProperty("role").GetString() == "user")
            .Select(m => m.GetProperty("content").GetString())
            .LastOrDefault() ?? "";
        var caseId = new string(userText.Where(char.IsDigit).ToArray());
        var content = JsonSerializer.Serialize(new { caseId, intent = "service_credit" });
        // Deterministic rough estimate (~4 chars/token) so offline runs still
        // produce cost numbers.
        return Task.FromResult(new ModelCompletion(content,
            Math.Max(1, promptJson.Length / 4), Math.Max(1, content.Length / 4)));
    }
}

/// <summary>
/// Any OpenAI-compatible /v1/chat/completions endpoint: Ollama, vLLM,
/// LM Studio, or Azure OpenAI's unified v1 surface. Configured via
/// AURELIA_MODEL_ENDPOINT / AURELIA_MODEL_ID / AURELIA_MODEL_API_KEY.
/// </summary>
public sealed class OpenAICompatibleModelClient(string endpoint, string modelId, string? apiKey = null) : IModelClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };

    public string ModelId => modelId;
    public string ParametersJson => """{"temperature":0}""";
    public string ConfigVersion => $"openai-compat@{endpoint}";

    public async Task<ModelCompletion> CompleteAsync(string promptJson, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{endpoint.TrimEnd('/')}/chat/completions");
        if (!string.IsNullOrEmpty(apiKey))
        {
            // Bearer for OpenAI/vLLM/Ollama; api-key for Azure OpenAI's v1
            // surface. Sending both is harmless — servers read their own.
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Headers.Add("api-key", apiKey);
        }
        request.Content = JsonContent.Create(new
        {
            model = modelId,
            messages = JsonSerializer.Deserialize<JsonElement>(promptJson),
            temperature = 0,
        });

        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var content = body.RootElement.GetProperty("choices")[0]
            .GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidOperationException("model returned empty content");

        int? inputTokens = null, outputTokens = null;
        if (body.RootElement.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt))
                inputTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ot))
                outputTokens = ot.GetInt32();
        }
        return new ModelCompletion(content, inputTokens, outputTokens);
    }

    public static IModelClient FromEnvironmentOrFake()
    {
        var endpoint = Environment.GetEnvironmentVariable("AURELIA_MODEL_ENDPOINT");
        if (string.IsNullOrEmpty(endpoint))
            return new FakeModelClient();
        return new OpenAICompatibleModelClient(
            endpoint,
            Environment.GetEnvironmentVariable("AURELIA_MODEL_ID") ?? "llama3.2",
            Environment.GetEnvironmentVariable("AURELIA_MODEL_API_KEY"));
    }

    /// <summary>Optional fallback backend (ADR-0022): "fake" or a second
    /// OpenAI-compatible endpoint via AURELIA_MODEL_FALLBACK_ENDPOINT/_ID/_KEY.</summary>
    public static IModelClient? FallbackFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable("AURELIA_MODEL_FALLBACK") == "fake")
            return new FakeModelClient();
        var endpoint = Environment.GetEnvironmentVariable("AURELIA_MODEL_FALLBACK_ENDPOINT");
        if (string.IsNullOrEmpty(endpoint))
            return null;
        return new OpenAICompatibleModelClient(
            endpoint,
            Environment.GetEnvironmentVariable("AURELIA_MODEL_FALLBACK_ID") ?? "llama3.2",
            Environment.GetEnvironmentVariable("AURELIA_MODEL_FALLBACK_KEY"));
    }
}
