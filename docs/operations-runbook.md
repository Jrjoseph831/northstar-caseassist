# Deployment, monitoring, incident, and teardown runbook

## Deploy

1. Run frontend lint/tests, .NET build/tests, vulnerable-package reporting, and
   Bicep validation.
2. Build an immutable container tag and smoke-test `/health` locally.
3. Push the image to the project registry.
4. Load secrets only into process environment variables.
5. Deploy `infrastructure/bicep/container-app.bicep` to
   `rg-northstar-caseassist-dev`.
6. Verify health, image tag, provider mode, SQL access, Search, Content Safety,
   Blob upload, and a request-specific safety trace.
7. Build and deploy the exact committed Sites source version.

The GitHub workflow performs these steps only for an explicit manual dispatch
from `main`, after all CI jobs pass, and uses the `northstar-dev` environment so
repository owners can require approval.

## Monitor

- Application audit: governance dashboard and Azure SQL `AuditEvents` records.
- Infrastructure/runtime: `appi-northstar-dev-ta542fwh` and
  `log-northstar-dev-ta542fwh`.
- Log retention: 30 days.
- Sampling: 25%.
- Workspace ingestion cap: 0.05 GB/day.
- AI guard: 40 requests or $0.25 estimated cost per synthetic user per UTC day.

Application audit data and operational telemetry serve different purposes and
must not be represented as interchangeable.

## Incident response

1. Identify the request/correlation ID from the safe error or trace.
2. Inspect application audit events and sampled operational telemetry.
3. Disable live AI by deploying `AI__Provider=OfflineFixture` if the provider or
   safety boundary is unreliable.
4. Keep review items pending; do not auto-approve during degraded operation.
5. Preserve synthetic hashes/metadata without copying model or secret values.
6. Correct the control, run deterministic evaluations, then perform one bounded
   live smoke request before re-enabling the path.
7. Record the incident scenario and evidence in project documentation.

## Project-scoped stop/teardown

Scale-to-zero is automatic for the Container App. To remove ongoing project
resources, first verify the exact resource group:

```powershell
az group show --name rg-northstar-caseassist-dev --query id --output tsv
```

Only after confirming that exact project-scoped ID, delete it with:

```powershell
az group delete --name rg-northstar-caseassist-dev --yes --no-wait
```

This removes the project resource group only. It does not target a subscription,
home directory, workspace root, or another broad scope.

