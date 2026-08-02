// Endpoint tests for the playground (defense-pack follow-through): the API
// drives REAL worker processes, so these are end-to-end — create a session,
// watch it complete, pull its trace, and check the guardrails.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Aurelia.PlaygroundTests;

public class EndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public EndpointTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Session_runs_to_completion_and_trace_is_served()
    {
        var created = await _client.PostAsJsonAsync("/api/sessions",
            new { agent = "echo", goal = "endpoint test", sabotageNotify = false });
        created.EnsureSuccessStatusCode();
        var sessionId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessionId").GetString()!;

        // Real worker process + demo pacing: allow up to 60 s.
        string status = "";
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var snapshot = await _client.GetFromJsonAsync<JsonElement>($"/api/sessions/{sessionId}");
            status = snapshot.GetProperty("status").GetString()!;
            if (status is "completed" or "failed")
                break;
            await Task.Delay(500);
        }
        Assert.Equal("completed", status);

        var trace = await _client.GetFromJsonAsync<JsonElement>($"/api/trace/{sessionId}");
        Assert.Equal("completed", trace.GetProperty("status").GetString());
        Assert.True(trace.GetProperty("events").GetArrayLength() >= 4);

        var replay = await _client.PostAsync($"/api/sessions/{sessionId}/replay", null);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(replayBody.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Guardrails_hold_for_unknown_and_invalid_input()
    {
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync("/api/sessions/no-such-session")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync("/api/trace/no-such-session")).StatusCode);
        // Path-unsafe session ids are refused, not resolved.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.GetAsync("/api/trace/..%2Fescape")).StatusCode);
        var badApprove = await _client.PostAsJsonAsync("/api/sessions/nope/approve",
            new { key = "nope/0/1", approved = true });
        Assert.Equal(HttpStatusCode.NotFound, badApprove.StatusCode);
    }
}
