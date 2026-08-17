using './container-app.bicep'

param location = 'eastus'
param environment = 'dev'
param owner = 'jhal786'
param bffSharedSecret = readEnvironmentVariable('NORTHSTAR_BFF_SHARED_SECRET')
param sqlConnectionString = readEnvironmentVariable('NORTHSTAR_SQL_CONNECTION_STRING')
param openAiApiKey = readEnvironmentVariable('OPENAI_API_KEY')
param imageTag = 'dev-20260816-5'
param sitesOrigin = 'https://northstar-caseassist-demo.falajoe.chatgpt.site'
