# Northstar Azure infrastructure

The Bicep template deploys into the existing project-scoped resource group. Its
safe default creates only:

- Linux App Service Free F1;
- one StorageV2 Standard LRS account with private containers; and
- a system-assigned identity with Blob Data Contributor scoped to that account.

Azure SQL, AI Search, and Content Safety are explicit opt-ins. Azure SQL is
configured with `useFreeLimit: true` and
`freeLimitExhaustionBehavior: AutoPause`; the template never selects paid
overage behavior.

Validate before deployment:

```powershell
az bicep build --file infrastructure/bicep/main.bicep
az deployment group what-if `
  --resource-group rg-northstar-caseassist-dev `
  --template-file infrastructure/bicep/main.bicep `
  --parameters infrastructure/bicep/main.dev.bicepparam `
  --parameters bffSharedSecret='<server-side-value>'
```

Project-scoped teardown:

```powershell
az group delete --name rg-northstar-caseassist-dev
```

Before teardown, verify that the resolved resource-group name exactly equals
`rg-northstar-caseassist-dev`. This removes the project's Azure resources but
does not affect the existing Sites deployment.
