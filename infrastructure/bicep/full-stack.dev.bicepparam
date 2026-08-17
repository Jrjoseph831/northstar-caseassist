using './main.bicep'

param location = 'eastus'
param environment = 'dev'
param owner = 'jhal786'
param sitesOrigin = 'https://northstar-caseassist-demo.falajoe.chatgpt.site'
param bffSharedSecret = readEnvironmentVariable('NORTHSTAR_BFF_SHARED_SECRET')
param deploySql = true
param sqlLocation = 'canadacentral'
param sqlServerNameSuffix = 'cac'
param sqlAdministratorLogin = readEnvironmentVariable('NORTHSTAR_SQL_ADMIN_LOGIN')
param sqlAdministratorPassword = readEnvironmentVariable('NORTHSTAR_SQL_ADMIN_PASSWORD')
param deploySearch = true
param deployContentSafety = true
param enableLiveAi = false
