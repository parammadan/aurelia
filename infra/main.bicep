// Aurelia infrastructure: Cosmos DB (serverless, Entra-only) for the event
// store, Log Analytics + Application Insights for the OTel pipeline.
// Deploy: az deployment group create -g rg-aurelia -f infra/main.bicep
param location string = resourceGroup().location

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: 'aurelia-${uniqueString(resourceGroup().id)}'
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
      }
    ]
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
    // Entra ID only — account keys do not exist (ADR-0019).
    disableLocalAuth: true
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  parent: cosmos
  name: 'aurelia'
  properties: {
    resource: {
      id: 'aurelia'
    }
  }
}

// One partition per session (ADR-0007): the session's flat, totally-ordered
// log lives in a single logical partition, which is what makes the
// head-document transactional-batch append (ADR-0018/0019) possible.
resource events 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  parent: database
  name: 'events'
  properties: {
    resource: {
      id: 'events'
      partitionKey: {
        paths: ['/sessionId']
        kind: 'Hash'
      }
    }
  }
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'aurelia-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'aurelia-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

resource ai 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: 'aurelia-ai-${uniqueString(resourceGroup().id)}'
  location: location
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: 'aurelia-ai-${uniqueString(resourceGroup().id)}'
    publicNetworkAccess: 'Enabled'
  }
}

// model-router: the one current model this subscription has default quota
// for (newer gpt-5.x models need a quota request; 2024/early-2025 models are
// closed to new deployments). Set deployModel=false on subscriptions without
// OpenAI quota.
param deployModel bool = true

resource classifierModel 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (deployModel) {
  parent: ai
  name: 'model-router'
  sku: {
    name: 'GlobalStandard'
    capacity: 8
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'model-router'
      version: '2025-11-18'
    }
  }
}

resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: 'aurelia-sb-${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
}

resource casesQueue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBus
  name: 'cases'
  properties: {}
}

output cosmosEndpoint string = cosmos.properties.documentEndpoint
output cosmosAccountName string = cosmos.name
output appInsightsConnectionString string = insights.properties.ConnectionString
output openAiEndpoint string = ai.properties.endpoint
output openAiAccountName string = ai.name
