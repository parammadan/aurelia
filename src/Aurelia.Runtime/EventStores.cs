// Store selection: Cosmos when AURELIA_COSMOS_ENDPOINT is set (Entra ID —
// developer `az login` locally, managed identity in Azure); the JSONL file
// store otherwise.
using Aurelia.Core;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Cosmos;

namespace Aurelia.Runtime;

public static class EventStores
{
    private static CosmosClient? _client;

    public static IEventStore FromEnvironment(string fileStoreDirectory)
    {
        var endpoint = Environment.GetEnvironmentVariable("AURELIA_COSMOS_ENDPOINT");
        if (string.IsNullOrEmpty(endpoint))
            return new FileEventStore(fileStoreDirectory);

        _client ??= new CosmosClient(endpoint, Credential());
        return new CosmosEventStore(_client.GetContainer(
            Environment.GetEnvironmentVariable("AURELIA_COSMOS_DATABASE") ?? "aurelia",
            Environment.GetEnvironmentVariable("AURELIA_COSMOS_CONTAINER") ?? "events"));
    }

    // CLI credential first: instant on a dev box, and DefaultAzureCredential's
    // managed-identity probe hangs for minutes off-Azure. In Azure the CLI
    // credential fails fast and the chain falls through to managed identity.
    public static TokenCredential Credential() =>
        new ChainedTokenCredential(new AzureCliCredential(), new ManagedIdentityCredential());
}
