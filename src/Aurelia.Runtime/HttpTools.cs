// Client-side tools that call the harness-owned tool server. Two classes so
// a non-compensatable tool never LOOKS compensatable to the saga unwind
// (ADR-0016 discovers compensability by type).
using System.Net.Http.Json;
using System.Text.Json;
using Aurelia.Core;

namespace Aurelia.Runtime;

public class HttpTool(string baseUrl, string name) : ITool
{
    protected static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    protected readonly string BaseUrl = baseUrl.TrimEnd('/');

    public string Name => name;

    public async Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync(
            $"{BaseUrl}/tools/{Name}",
            new { argumentsJson, idempotencyKey }, ct);
        return await ReadResultAsync(response, ct);
    }

    protected static async Task<string> ReadResultAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                doc.RootElement.TryGetProperty("error", out var err)
                    ? err.GetString() ?? "tool server error"
                    : $"tool server returned {(int)response.StatusCode}");
        return doc.RootElement.GetProperty("resultJson").GetString()!;
    }
}

public sealed class HttpCompensatableTool(string baseUrl, string name)
    : HttpTool(baseUrl, name), ICompensatableTool
{
    public async Task<string> CompensateAsync(
        string originalArgumentsJson, string originalResultJson, string compensationKey,
        CancellationToken ct = default)
    {
        using var response = await Http.PostAsJsonAsync(
            $"{BaseUrl}/tools/{Name}/compensate",
            new { originalArgumentsJson, originalResultJson, compensationKey }, ct);
        return await ReadResultAsync(response, ct);
    }
}
