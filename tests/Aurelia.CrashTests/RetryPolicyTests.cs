// ADR-0027: retries follow the declared policy — attempt count honored,
// exponential backoff actually paces the retries, and attempt failures stay
// recorded events.
using System.Diagnostics;
using Aurelia.Core;
using Aurelia.Runtime;
using Xunit;

namespace Aurelia.CrashTests;

public class RetryPolicyTests
{
    [Fact]
    public async Task Retries_follow_the_declared_attempts_and_backoff()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-retry-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var tool = new FailNTimesTool(failures: 2);
            var policy = new RetryPolicy(MaxAttempts: 3, TimeSpan.FromMilliseconds(50), BackoffFactor: 2.0);
            var driver = new ScriptedAgentDriver(store,
                new Dictionary<string, ITool> { ["flaky"] = tool },
                [new ScriptStep("flaky", ctx => ctx.UserMessage)],
                retryPolicy: policy);

            var clock = Stopwatch.StartNew();
            var result = await driver.RunToCompletionAsync("retry-session", "go");
            clock.Stop();

            Assert.Contains("ok", result.FinalStateJson);
            var log = await store.ReadAsync("retry-session");
            Assert.Equal(2, log.OfType<CallAttemptFailed>().Count());
            Assert.Equal(3, Assert.Single(log.OfType<ToolCallSucceeded>()).Attempt);
            // Backoff before attempts 2 and 3: 50 ms + 100 ms minimum.
            Assert.True(clock.ElapsedMilliseconds >= 140,
                $"expected ≥150ms of backoff, elapsed {clock.ElapsedMilliseconds}ms");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Exhaustion_respects_the_declared_max()
    {
        var dir = Directory.CreateTempSubdirectory("aurelia-retry-max-");
        try
        {
            var store = new FileEventStore(dir.FullName);
            var driver = new ScriptedAgentDriver(store,
                new Dictionary<string, ITool> { ["flaky"] = new FailNTimesTool(failures: 99) },
                [new ScriptStep("flaky", ctx => ctx.UserMessage)],
                retryPolicy: new RetryPolicy(4, TimeSpan.FromMilliseconds(1), 1.0));

            var result = await driver.RunToCompletionAsync("retry-max", "go");

            Assert.Contains("failed", result.FinalStateJson);
            var log = await store.ReadAsync("retry-max");
            Assert.Equal(4, log.OfType<CallAttemptFailed>().Count());
            Assert.Single(log.OfType<CallFailed>());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private sealed class FailNTimesTool(int failures) : ITool
    {
        private int _failed;

        public string Name => "flaky";

        public Task<string> ExecuteAsync(string argumentsJson, string idempotencyKey, CancellationToken ct = default)
        {
            if (_failed < failures)
            {
                _failed++;
                throw new InvalidOperationException("flaky");
            }
            return Task.FromResult("""{"ok":true}""");
        }
    }
}
