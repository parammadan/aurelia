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

output cosmosEndpoint string = cosmos.properties.documentEndpoint
output cosmosAccountName string = cosmos.name
output appInsightsConnectionString string = insights.properties.ConnectionString
