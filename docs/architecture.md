# Architecture and data flow

Northstar CaseAssist is a synthetic-data-only reference system. The Sites portal
is the presentation tier; the ASP.NET Core API on Azure Container Apps is the
system-of-record and orchestration boundary. Browsers never receive service
credentials or connect directly to Azure data or model services.

## System context

```mermaid
flowchart LR
    citizen["Fictional applicant"] --> portal["Private Northstar Sites portal"]
    worker["Maya Chen\nCaseworker"] --> portal
    reviewer["Marcus Reed\nReviewer"] --> portal
    admin["Priya Shah\nAdministrator"] --> portal
    portal --> api["Northstar API\nAzure Container Apps"]
    api --> azure["Azure data, search, safety, and telemetry"]
    api --> openai["OpenAI Responses API"]
```

## Deployed containers and services

```mermaid
flowchart TD
    browser["Authenticated browser"] -->|"same-origin HTTPS"| bff["Sites server routes\nBFF credential held server-side"]
    bff -->|"HTTPS + correlation ID + synthetic role"| api["ASP.NET Core API\nContainer Apps, min 0 / max 1"]
    api --> sql["Azure SQL Database\napplications, cases, AI requests, reviews, audit"]
    api --> blob["Blob Storage\nprivate case-documents and approved-policies"]
    api --> search["Azure AI Search Free\napproved-policy-sections-v1"]
    api --> safety["Azure AI Content Safety F0"]
    api --> model["OpenAI Responses API\ngpt-5-mini"]
    api --> appi["Application Insights\n25% sampled operational telemetry"]
    appi --> logs["Log Analytics\n30 days, 0.05 GB/day cap"]
```

## AI request sequence

```mermaid
sequenceDiagram
    actor Maya as Maya / Caseworker
    participant UI as Sites portal
    participant API as Northstar API
    participant SQL as Azure SQL
    participant Search as AI Search
    participant Model as OpenAI
    participant Safety as Content Safety
    Maya->>UI: Ask a case question
    UI->>API: Redacted-workflow request through BFF
    API->>SQL: Verify identity, role, and case assignment
    API->>API: Redact PII and detect injection patterns
    API->>Search: Retrieve approved policy sections
    Search-->>API: Exact source IDs and excerpts
    API->>Model: Controlled prompt with redacted question and evidence
    Model-->>API: Draft, citations, and token usage
    API->>Safety: Analyze output
    API->>API: Validate citations and classify risk
    API->>SQL: Persist request, trace, review item, and audit events
    API-->>UI: Draft plus safe trace metadata
```

## Trust boundaries

- Browser to Sites: authenticated private-site boundary.
- Sites to API: secret-backed BFF boundary; no service secret enters client code.
- API to Azure services: user-assigned managed identity for Blob, Search, and
  Content Safety.
- API to SQL/OpenAI: host-managed secret references in the reference deployment.
- Application audit records are distinct from sampled operational telemetry.

## Reference substitutions

| Reference implementation | Production mapping |
|---|---|
| Private Sites portal and synthetic persona selector | Entra ID app roles and Conditional Access |
| BFF shared credential | Entra workload identity or managed identity federation |
| Container Apps public ingress restricted by BFF middleware | APIM plus private ingress/network controls |
| Structured audit table and dashboard | Export to Log Analytics/Sentinel |
| Deterministic injection detector | Retain it and add Prompt Shields/defense-in-depth |
| OpenAI provider interface | Azure OpenAI provider with the same application contract |

