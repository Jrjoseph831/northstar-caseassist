# Northstar CaseAssist — Stage 0 Discovery and Cost Gate

Date: 2026-08-16  
Status: Cost gate passed for local Stage 1 work; Azure provisioning remains gated  
Provisioning performed: None

## Decision

Do not provision Azure resources yet. The existing application is a functional Sites-hosted demonstration with persistent D1 data and a server-side OpenAI integration, but it is not yet the requested ASP.NET Core/Azure implementation. The lowest-risk Azure target is a separate .NET 10 LTS API on Azure Container Apps Consumption, with all optional services created only after provider registration and final free-offer checks.

The intended idle-cost ceiling is **$5 USD per month**, with a preferred steady-state cost of **$0**. Azure budget alerts are notifications, not hard spending caps, so scale-to-zero, free-offer limits, request caps, and teardown procedures remain mandatory.

## Existing system inventory

### Runtime and hosting

- Frontend: Next-compatible React application built with Vinext/Vite.
- Current hosting: OpenAI Sites on a Cloudflare Worker-style runtime.
- Current public application: `https://northstar-caseassist-demo.falajoe.chatgpt.site/`.
- Current server routes:
  - `POST /api/assistant`
  - `GET/POST /api/reviews`
  - `POST /api/reviews/decision`
  - `GET /api/governance`
- Current persistence: Cloudflare D1 binding named `DB`.
- Current model integration: OpenAI Responses API, called only from server code.
- Current model controls: `store: false`, no reasoning, maximum 450 output tokens, 20 requests per persona per day, exact-request cache, deterministic fallback.

### Current data model

The D1 schema contains:

- `cases`
- `policies`
- `assistant_requests`
- `review_items`
- `audit_events`
- `evaluation_runs`

Two migrations exist in `drizzle/`. The application includes 12 seeded fictional policy sections and fictional case data.

### Secret inventory

The hosted project exposes one secret name:

- `OPENAI_API_KEY`

The value is redacted by the hosting platform and was not printed during discovery. No `.env` files or OpenAI key-shaped values were found in the repository. Credentials must remain server-side and uncommitted.

## Functional-control assessment

| Capability | Current state | Evidence / limitation |
|---|---|---|
| Case workspace | Partial | Main case experience works, but the case list is still hardcoded in the page instead of loaded from a case API. |
| Authentication | Partial | Sites authentication is present. The application-supplied persona selector is not authoritative Entra ID role assignment. |
| Role separation | Demo-functional | Known personas map to roles; reviewer actions require the reviewer persona; submitter self-approval is blocked. Client-selected personas are not production-grade authorization. |
| PII protection | Partial | Deterministic SSN, phone, and email detection/redaction is implemented before model access. Names, addresses, and case identifiers are not covered. |
| Retrieval | Demo-functional | Keyword scoring searches 12 D1 policy sections. This is not Azure AI Search and does not use embeddings. |
| AI generation | Functional | A capped server-side OpenAI request is available, with deterministic fallback when live mode is unavailable. |
| Citations | Partial | Retrieved policy references are attached, but validation currently checks retrieval presence rather than proving every cited claim is supported by an excerpt. |
| Risk classification | Partial | Eligibility/recommendation language can route an item to review. There is no dedicated prompt-injection detector or complete output policy engine. |
| Review queue | Functional demo | Review items persist; the separate reviewer can approve or return them; return notes are required. |
| Audit trail | Partial | Structured events are persisted for important actions, but not every required pipeline step has a distinct immutable event. |
| Governance metrics | Implemented | Request, review-routing, queue, audit, and evaluation figures are calculated from persisted Azure records and evaluation runs. |
| Safety trace | Partial | Request metadata and control outcomes exist, but the full per-request trace described in the master prompt is not yet implemented. |
| Automated tests | Failing baseline | `npm test` builds successfully, then fails stale starter-template assertions that reference deleted preview files and metadata. There is not yet an end-to-end control evaluation suite. |

## Local development prerequisites

Initial discovery and follow-up verification found:

- Azure CLI 2.89.1: installed under the 32-bit Program Files path. The Codex process has a stale `PATH`, so commands currently use its absolute path.
- Azure PowerShell `Az.Accounts`: not installed.
- System .NET launcher: present, but no system-wide SDK is installed.
- Workspace-local .NET 10.0.400 SDK: installed under ignored `work/.dotnet/` for Stage 1 development.
- Windows Package Manager (`winget`): not installed.
- Azure authentication: successful.
- Active subscription: `Azure for Students`, state `Enabled`, under the Western Governors University tenant.
- Subscription policy: `quotaId` identifies the Azure for Students offer and `spendingLimit` is `On`.
- Existing Azure inventory: no resource groups and no resources.
- Required resource providers: currently not registered.
- Advertised resource-type locations: Container Apps, Azure SQL, Azure AI Search, and Cognitive Services are present in both East US and East US 2.
- Content Safety: F0 and S0 SKUs are listed in East US 2.
- Cost Management usage/budget API: rejects this student offer type. An exact remaining-credit amount was therefore not available through Azure CLI.

For a new implementation, use **.NET 10 LTS** rather than .NET 8. As of this record, .NET 10 is active through 2028-11-14, while .NET 8 reaches end of support on 2026-11-10.

Required before provisioning:

1. Confirm the remaining student-credit amount in the Azure portal, because the CLI Cost Management API does not support this offer type.
2. Register only the resource providers required for the approved vertical slice.
3. Confirm the Azure SQL `Apply offer` control displays an estimated monthly cost of $0.
4. Confirm an unused Azure AI Search Free service is available immediately before deployment.

## Proposed Azure resource inventory

This is a planning inventory only. No resource has been created.

| Resource | Proposed SKU/configuration | Cost posture | Gate before creation |
|---|---|---|---|
| Resource group | `rg-northstar-caseassist-dev` | Free | Confirm subscription and selected region. |
| ASP.NET Core API | Azure Container Apps Consumption; 0 minimum replicas; 1 maximum replica initially | Expected $0 at demo traffic within the monthly free grant | Confirm regional availability; use a non-Azure paid registry path or source deployment that does not create a fixed-cost ACR. |
| Container image | GitHub Container Registry, preferably a public demo image with no secrets | Avoids Azure Container Registry Basic fixed cost | Confirm repository visibility and threat model before publishing an image. |
| Database | Azure SQL Database free offer, General Purpose serverless; free-limit behavior set to auto-pause until next month | $0 within 100,000 vCore-seconds and 32 GB per month | Confirm the subscription shows the `Apply offer` control and an estimated monthly cost of $0. Do not select paid-overage behavior. |
| Policy retrieval | Azure AI Search Free | $0; one service per subscription, 50 MB storage | Confirm an unused Free service is available in the chosen region. |
| Safety | Azure AI Content Safety Free | $0 within 5,000 text records/month | Confirm the F0 SKU and required features are available in-region. |
| Document storage | StorageV2, Standard LRS, Hot, lifecycle rules | Pennies at most for a tiny fictional corpus; verify calculator first | Create only if local/repository documents are insufficient for the first vertical slice. |
| Monitoring | Application Insights + Log Analytics with strict sampling and a daily cap | Target $0; ingestion is the principal overage risk | Verify current free allowance and configure a small daily cap before enabling verbose telemetry. Never log raw PII or prompts. |
| Identity | System-assigned managed identity for the API; Entra ID app registration | No direct resource charge | Configure least privilege after the API exists. |
| Secret storage | Container App secrets initially; Key Vault deferred | Avoids unnecessary early service and transaction costs | Migrate to Key Vault only when managed identity and deployment path justify it. |
| Spending control | Azure for Students spending limit: On | Hard subscription-level protection after credit exhaustion | Keep enabled. Native Cost Management budgets are not supported by this offer type. |

### Services intentionally excluded from the MVP

- Azure Container Registry Basic, unless a later deployment constraint makes it necessary.
- API Management paid tiers.
- Microsoft Sentinel and Purview.
- Private Link, dedicated virtual networks, premium queues, multi-region deployment, and disaster-recovery replicas.
- Any Azure AI Search paid tier.
- Any Azure SQL setting that automatically continues into paid overage.

## Cost ceiling and alert plan

### Spending controls

- Desired operating ceiling: **$5 USD/month**, with a preferred steady-state cost of $0.
- Verified Azure for Students subscription spending limit: **On**.
- The Azure Cost Management budget API returns HTTP 422 for this offer type, so the previously proposed resource-group budget cannot be represented as an available control.
- Use the student-credit balance and threshold emails in the Azure portal plus a weekly manual review.
- If the subscription is ever upgraded to a supported paid offer, immediately add actual-cost alerts at $2.50, $4.00, and $5.00 plus a forecast alert at $4.00.

The following preventive controls remain required:

- Container Apps minimum replicas set to 0 and maximum replicas set to 1 initially.
- Azure SQL free-limit behavior set to pause until the next month.
- AI Search restricted to Free.
- Content Safety restricted to Free.
- OpenAI daily request limits, low output cap, caching, demo mode, and no anonymous live-AI access.
- Application Insights sampling and a small daily ingestion cap.
- A documented teardown command and portal checklist for every provisioned resource.
- Weekly cost review while the public demo is enabled.

## Region decision

Subscription-policy-approved deployment region: `eastus`.
Original candidate rejected by subscription policy: `eastus2`.

Provider metadata advertised the required resource types in East US 2, but an Azure `Allowed resource deployment regions` policy rejected resource creation there. The policy permits East US, Canada Central, North Central US, Belgium Central, and West US. East US is therefore the approved co-located target, subject to each free SKU's final deployment validation. Azure SQL's zero-dollar free-offer control and AI Search Free capacity must still be confirmed at creation time.

## Stage 1 entry criteria

Local Stage 1 work may begin now. Azure provisioning remains gated until all of the following are true:

- The remaining student-credit balance is visually confirmed in the Azure portal.
- Only required providers are deliberately registered.
- Azure SQL shows the free offer with paid overage disabled.
- AI Search Free capacity is available in the approved region.

The first vertical slice should now implement a minimal ASP.NET Core API locally—health check, synthetic cases, deterministic PII redaction, SQLite persistence, and tests—before creating Azure resources.

## Official references used for the cost gate

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Azure for Students](https://azure.microsoft.com/en-us/free/students/)
- [Azure Container Apps pricing](https://azure.microsoft.com/en-us/pricing/details/container-apps/)
- [Azure SQL Database free offer FAQ](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer-faq?view=azuresql)
- [Try Azure AI Search for free](https://learn.microsoft.com/en-us/azure/search/search-try-for-free)
- [Azure AI Content Safety pricing](https://azure.microsoft.com/en-us/pricing/details/content-safety/)
- [Azure Monitor pricing](https://azure.microsoft.com/en-us/pricing/details/monitor/)
- [Create and manage Azure budgets](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/tutorial-acm-create-budgets)
