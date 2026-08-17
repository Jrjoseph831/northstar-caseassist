targetScope = 'resourceGroup'

param location string = 'eastus'
param environment string = 'dev'
param owner string

@secure()
param bffSharedSecret string

@secure()
param sqlConnectionString string
@secure()
param openAiApiKey string
param imageTag string
param sitesOrigin string = 'https://northstar-caseassist-demo.falajoe.chatgpt.site'

var suffix = toLower(substring(uniqueString(subscription().subscriptionId, resourceGroup().id), 0, 8))
var baseName = 'northstar-${environment}-${suffix}'
var registryName = replace('acr${baseName}', '-', '')
var storageName = replace('st${baseName}', '-', '')
var searchName = 'search-${baseName}'
var contentSafetyName = 'safety-${baseName}'
var workspaceName = 'log-${baseName}'
var applicationInsightsName = 'appi-${baseName}'
var commonTags = {
  project: 'NorthstarCaseAssist'
  environment: environment
  owner: owner
  'synthetic-data-only': 'true'
  'cost-center': 'student-portfolio'
}

resource registry 'Microsoft.ContainerRegistry/registries@2025-11-01' existing = {
  name: registryName
}

resource storage 'Microsoft.Storage/storageAccounts@2025-01-01' existing = {
  name: storageName
}

resource search 'Microsoft.Search/searchServices@2025-05-01' existing = {
  name: searchName
}

resource contentSafety 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: contentSafetyName
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: commonTags
  properties: {
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: json('0.05')
    }
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  tags: commonTags
  properties: {
    Application_Type: 'web'
    RetentionInDays: 30
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
  }
}

resource workloadIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-northstar-api-${environment}-${suffix}'
  location: location
  tags: commonTags
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, workloadIdentity.id, 'AcrPull')
  scope: registry
  properties: {
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, workloadIdentity.id, 'Storage Blob Data Contributor')
  scope: storage
  properties: {
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

resource searchReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(search.id, workloadIdentity.id, 'Search Index Data Reader')
  scope: search
  properties: {
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '1407120a-92aa-4202-b7e9-c0e197c71c8f')
  }
}

resource contentSafetyUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(contentSafety.id, workloadIdentity.id, 'Cognitive Services User')
  scope: contentSafety
  properties: {
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')
  }
}

resource managedEnvironment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: 'cae-${baseName}'
  location: location
  tags: commonTags
  properties: {
    zoneRedundant: false
  }
}

resource containerApp 'Microsoft.App/containerApps@2025-01-01' = {
  name: 'aca-northstar-api-${environment}-${suffix}'
  location: location
  tags: commonTags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workloadIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        allowInsecure: false
        targetPort: 8080
        transport: 'auto'
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: workloadIdentity.id
        }
      ]
      secrets: [
        {
          name: 'bff-secret'
          value: bffSharedSecret
        }
        {
          name: 'sql-connection'
          value: sqlConnectionString
        }
        {
          name: 'openai-api-key'
          value: openAiApiKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'northstar-api'
          image: '${registry.properties.loginServer}/northstar-api:${imageTag}'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ASPNETCORE_URLS', value: 'http://+:8080' }
            { name: 'AZURE_CLIENT_ID', value: workloadIdentity.properties.clientId }
            { name: 'Northstar__DemoMode', value: 'true' }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: applicationInsights.properties.ConnectionString }
            { name: 'Security__AllowedOrigins__0', value: sitesOrigin }
            { name: 'Security__BffSharedSecret', secretRef: 'bff-secret' }
            { name: 'Database__Provider', value: 'SqlServer' }
            { name: 'ConnectionStrings__Northstar', secretRef: 'sql-connection' }
            { name: 'Storage__Provider', value: 'AzureBlob' }
            { name: 'Storage__AccountName', value: storage.name }
            { name: 'Search__Provider', value: 'AzureAiSearch' }
            { name: 'Search__Endpoint', value: 'https://${search.name}.search.windows.net' }
            { name: 'Search__IndexName', value: 'approved-policy-sections-v1' }
            { name: 'ContentSafety__Provider', value: 'AzureAIContentSafety' }
            { name: 'ContentSafety__Endpoint', value: 'https://${contentSafety.name}.cognitiveservices.azure.com/' }
            { name: 'AI__Provider', value: 'OpenAI' }
            { name: 'AI__ApiKey', secretRef: 'openai-api-key' }
            { name: 'AI__Model', value: 'gpt-5-mini' }
            { name: 'AI__InputPricePerMillion', value: '0.25' }
            { name: 'AI__OutputPricePerMillion', value: '2.0' }
            { name: 'AI__DailyRequestLimit', value: '40' }
            { name: 'AI__DailyCostLimitUsd', value: '0.25' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 30
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
  dependsOn: [
    acrPull
    blobContributor
    searchReader
    contentSafetyUser
  ]
}

output apiName string = containerApp.name
output apiUrl string = 'https://${containerApp.properties.configuration.ingress.fqdn}'
output identityName string = workloadIdentity.name
output applicationInsightsName string = applicationInsights.name
output logAnalyticsWorkspaceName string = logAnalytics.name
