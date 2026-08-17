using './main.bicep'

param location = 'eastus'
param environment = 'dev'
param owner = 'jhal786'
param sitesOrigin = 'https://northstar-caseassist-demo.falajoe.chatgpt.site'
param bffSharedSecret = readEnvironmentVariable('NORTHSTAR_BFF_SHARED_SECRET')
param deploySql = false
param deploySearch = false
param deployContentSafety = false
