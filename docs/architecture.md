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
    API->>API: Expand the question into similar queries
    API->>Search: Retrieve approved candidate sections
    Search-->>API: Approved candidates for the program
    API->>API: Dense + sparse search, rank fusion, late-interaction rerank
    API->>Model: Controlled prompt with redacted question and evidence
    Model-->>API: Draft, citations, and token usage
    API->>Safety: Analyze output
    API->>API: Validate citations and classify risk
    API->>SQL: Persist request, trace, review item, and audit events
    API-->>UI: Draft plus safe trace metadata
```

## Retrieval pipeline

Every answer is grounded in approved policy sections, and how those sections are
chosen is part of the control surface. One keyword search of the caseworker's
exact wording is not enough: caseworkers ask in caseworker language and the
corpus is written in policy language.

```mermaid
flowchart TD
    question["Redacted question"] --> expand["Generate similar queries\n(rule-based, or model when configured)"]
    question --> pool["Approved candidate pool\nprogram-filtered, approved only"]
    expand --> pool
    pool --> dense["Dense vector search\ncosine over embeddings"]
    pool --> sparse["Sparse BM25 search\nexact policy terms"]
    dense --> fuse["Reciprocal rank fusion\nk = 60"]
    sparse --> fuse
    fuse --> rerank["Late-interaction rerank\nIDF-weighted MaxSim"]
    rerank --> sources["Top sections + per-stage scores"]
    sources --> model["Prompt with approved excerpts"]
    sources --> trace["Safety trace"]
```

| Stage | What it contributes | Default |
|---|---|---|
| Query expansion | Rewrites the question into policy wording so a section is not missed for vocabulary reasons | Rule-based, 4 variants plus the original |
| Dense search | Matches on meaning when wording differs | Local deterministic embedding, 256d; hosted embeddings when configured |
| Sparse search | Matches the exact terms a policy turns on, which vectors blur | BM25, k1 = 1.4, b = 0.75 |
| Fusion | Combines rankings that are on different scales, promoting what several searches agree on | Reciprocal rank fusion, k = 60 |
| Reranking | Scores question tokens against section tokens instead of one blended vector | IDF-weighted MaxSim, 25% of the final score |

Two deliberate choices are worth naming. First, the offline path uses a local
deterministic embedding rather than a hosted one: it costs nothing, adds no
network call, and returns the same vector every time, which is what makes an
evaluation run reproducible. Setting `Retrieval:EmbeddingProvider` to `OpenAI`
swaps in hosted embeddings, and a failed embedding call falls back to the local
model for that request with the served model recorded in the trace.

Second, fusion leads and reranking refines. Sweeping the rerank weight against
the labeled question set showed recall and MRR falling once the reranker
outvoted the agreement between searches — on a corpus this small, agreement is
the stronger signal. The weight is configuration, not a constant, because that
balance shifts as a corpus grows.

When `Search:Provider` is `AzureAiSearch`, the service supplies the candidate
pool and its own relevance ranking joins the fusion as one more ranked list; the
dense, sparse, fusion and reranking stages run in the API either way.

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

