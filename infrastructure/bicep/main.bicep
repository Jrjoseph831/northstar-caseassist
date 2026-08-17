targetScope = 'resourceGroup'

@description('Primary Azure region for the portfolio deployment.')
param location string = resourceGroup().location

@allowed([
  'dev'
  'demo'
])
param environment string = 'dev'

param owner string

@secure()
@description('Shared server-to-server credential accepted only from the Sites BFF.')
param bffSharedSecret string

param sitesOrigin string = 'https://northstar-caseassist-demo.falajoe.chatgpt.site'
param deploySql bool = false
param sqlLocation string = location
param sqlServerNameSuffix string = ''
param deploySearch bool = false
param deployContentSafety bool = false
param enableLiveAi bool = false

@secure()
param openAiApiKey string = ''

@secure()
param sqlAdministratorLogin string = ''

@secure()
param sqlAdministratorPassword string = ''

var suffix = toLower(substring(uniqueString(subscription().subscriptionId, resourceGroup().id), 0, 8))
var baseName = 'northstar-${environment}-${suffix}'
var commonTags = {
  project: 'NorthstarCaseAssist'
  environment: environment
  owner: owner
  'synthetic-data-only': 'true'
  'cost-center': 'student-portfolio'
}

resource storage 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: replace('st${baseName}', '-', '')
  location: location
  tags: commonTags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    publicNetworkAccess: 'Enabled'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource caseDocuments 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: 'case-documents'
  properties: {
    publicAccess: 'None'
  }
}

resource approvedPolicies 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = {
  parent: blobService
  name: 'approved-policies'
  properties: {
    publicAccess: 'None'
  }
}

resource appPlan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: 'plan-${baseName}'
  location: location
  tags: commonTags
  kind: 'linux'
  sku: {
    name: 'F1'
    tier: 'Free'
    capacity: 1
  }
  properties: {
    reserved: true
  }
}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = if (deploySql) {
  name: 'sql-${baseName}${empty(sqlServerNameSuffix) ? '' : '-${sqlServerNameSuffix}'}'
  location: sqlLocation
  tags: commonTags
  properties: {
    administratorLogin: sqlAdministratorLogin
    administratorLoginPassword: sqlAdministratorPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    restrictOutboundNetworkAccess: 'Disabled'
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = if (deploySql) {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2025-01-01' = if (deploySql) {
  parent: sqlServer
  name: 'northstar'
  location: sqlLocation
  tags: commonTags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    autoPauseDelay: 60
    freeLimitExhaustionBehavior: 'AutoPause'
    licenseType: 'LicenseIncluded'
    maxSizeBytes: 34359738368
    minCapacity: json('0.5')
    requestedBackupStorageRedundancy: 'Local'
    useFreeLimit: true
    zoneRedundant: false
  }
}

var sqliteSettings = [
  {
    name: 'Database__Provider'
    value: 'Sqlite'
  }
  {
    name: 'ConnectionStrings__Northstar'
    value: 'Data Source=/home/data/northstar.db'
  }
]

var sqlSettings = [
  {
    name: 'Database__Provider'
    value: 'SqlServer'
  }
  {
    name: 'ConnectionStrings__Northstar'
    value: 'Server=tcp:${sqlServer!.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabase.name};Persist Security Info=False;User ID=${sqlAdministratorLogin};Password=${sqlAdministratorPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
  }
]

var liveAiSecretSettings = enableLiveAi ? [
  {
    name: 'AI__ApiKey'
    value: openAiApiKey
  }
] : []

resource webApp 'Microsoft.Web/sites@2024-11-01' = {
  name: 'api-${baseName}'
  location: location
  tags: commonTags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    enabled: true
    httpsOnly: true
    publicNetworkAccess: 'Enabled'
    serverFarmId: appPlan.id
    siteConfig: {
      alwaysOn: false
      appSettings: concat([
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          name: 'Northstar__DemoMode'
          value: 'true'
        }
        {
          name: 'Security__AllowedOrigins__0'
          value: sitesOrigin
        }
        {
          name: 'Security__BffSharedSecret'
          value: bffSharedSecret
        }
        {
          name: 'Storage__AccountName'
          value: storage.name
        }
        {
          name: 'Storage__Provider'
          value: 'AzureBlob'
        }
        {
          name: 'Search__Provider'
          value: deploySearch ? 'AzureAiSearch' : 'Database'
        }
        {
          name: 'Search__Endpoint'
          value: deploySearch ? 'https://${searchService.name}.search.windows.net' : ''
        }
        {
          name: 'Search__IndexName'
          value: 'approved-policy-sections-v1'
        }
        {
          name: 'ContentSafety__Provider'
          value: deployContentSafety ? 'AzureAIContentSafety' : 'DeterministicLocal'
        }
        {
          name: 'ContentSafety__Endpoint'
          value: deployContentSafety ? 'https://${contentSafety.name}.cognitiveservices.azure.com/' : ''
        }
        {
          name: 'AI__Provider'
          value: enableLiveAi ? 'OpenAI' : 'OfflineFixture'
        }
        {
          name: 'AI__Model'
          value: 'gpt-5-mini'
        }
        {
          name: 'AI__InputPricePerMillion'
          value: '0.25'
        }
        {
          name: 'AI__OutputPricePerMillion'
          value: '2.0'
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
      ], deploySql ? sqlSettings : sqliteSettings, liveAiSecretSettings)
      ftpsState: 'Disabled'
      http20Enabled: true
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      use32BitWorkerProcess: true
    }
  }
}

resource storageBlobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, webApp.id, 'Storage Blob Data Contributor')
  scope: storage
  properties: {
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

resource searchIndexReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deploySearch) {
  name: guid(searchService.id, webApp.id, 'Search Index Data Reader')
  scope: searchService
  properties: {
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '1407120a-92aa-4202-b7e9-c0e197c71c8f')
  }
}

resource searchService 'Microsoft.Search/searchServices@2025-05-01' = if (deploySearch) {
  name: 'search-${baseName}'
  location: location
  tags: commonTags
  sku: {
    name: 'free'
  }
  properties: {
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http401WithBearerChallenge'
      }
    }
    disableLocalAuth: false
    hostingMode: 'Default'
    partitionCount: 1
    publicNetworkAccess: 'enabled'
    replicaCount: 1
    semanticSearch: 'disabled'
  }
}

resource contentSafety 'Microsoft.CognitiveServices/accounts@2024-10-01' = if (deployContentSafety) {
  name: 'safety-${baseName}'
  location: location
  tags: commonTags
  kind: 'ContentSafety'
  sku: {
    name: 'F0'
  }
  properties: {
    customSubDomainName: 'safety-${baseName}'
    disableLocalAuth: false
    publicNetworkAccess: 'Enabled'
  }
}

resource contentSafetyUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployContentSafety) {
  name: guid(contentSafety.id, webApp.id, 'Cognitive Services User')
  scope: contentSafety
  properties: {
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'a97b65f3-24c7-4388-baec-2e87135dc908')
  }
}

output apiName string = webApp.name
output apiUrl string = 'https://${webApp.properties.defaultHostName}'
output storageAccountName string = storage.name
output databaseName string = deploySql ? sqlDatabase.name : ''
output searchServiceName string = deploySearch ? searchService.name : ''
output contentSafetyName string = deployContentSafety ? contentSafety.name : ''
