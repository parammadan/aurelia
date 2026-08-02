// Queue-triggered autonomous ingestion (ADR-0026): messages become durable
// sessions whose identity derives from the message id, so at-least-once
// delivery yields exactly-once task execution by construction.
using Azure.Messaging.ServiceBus;

namespace Aurelia.Runtime;

public static class ServiceBusIngestion
{
    public static async Task SendAsync(
        string fqdn, string queue, string goal, string? messageId, CancellationToken ct = default)
    {
        await using var client = new ServiceBusClient(fqdn, EventStores.Credential());
        var sender = client.CreateSender(queue);
        var message = new ServiceBusMessage(goal);
        if (messageId is not null)
            message.MessageId = messageId;
        await sender.SendMessageAsync(message, ct);
        Console.WriteLine($"SENT {message.MessageId}");
    }

    public static async Task<int> ListenAsync(
        string fqdn, string queue, string dataRoot, int maxMessages, TimeSpan idleTimeout,
        CancellationToken ct = default)
    {
        await using var client = new ServiceBusClient(fqdn, EventStores.Credential());
        var receiver = client.CreateReceiver(queue);
        var processed = 0;
        while (processed < maxMessages)
        {
            var message = await receiver.ReceiveMessageAsync(idleTimeout, ct);
            if (message is null)
                break; // queue drained

            var sessionId = "sb-" + Sanitize(message.MessageId);
            var dir = Path.Combine(dataRoot, sessionId);
            Directory.CreateDirectory(dir);
            try
            {
                var store = EventStores.FromEnvironment(dir);
                var driver = CaseResolutionAgent.CreateDriver(store, dir,
                    Environment.GetEnvironmentVariable("AURELIA_FAIL_NOTIFY") == "1");
                var result = await driver.RunToCompletionAsync(sessionId, message.Body.ToString(), ct);
                Console.WriteLine(
                    $"[listen] msg {message.MessageId} → {sessionId}: {SessionStates.StatusOf(result.FinalStateJson)}");
                // Parked (awaiting approval) sessions complete the message too:
                // the durable log holds the state; resolution arrives via any
                // surface later (ADR-0026).
                await receiver.CompleteMessageAsync(message, ct);
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.Error.WriteLine($"[listen] msg {message.MessageId} abandoned: {ex.Message}");
                await receiver.AbandonMessageAsync(message, cancellationToken: ct);
            }
        }
        return processed;
    }

    private static string Sanitize(string messageId) =>
        new([.. messageId.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-')]);
}
