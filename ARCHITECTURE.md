# Northstar CaseAssist — System Architecture

Responsible‑AI casework assistant. Core thesis: **redact on the way in, ground in approved
policy, validate on the way out, and never let the AI make the decision — a human does, and
every step is audited.**

---

## 1. Deployment topology & trust boundaries

```mermaid
flowchart TD
    subgraph BROWSER["Browser — React SPA (app/page.tsx)"]
        UI["Persona picker · Case workspace<br/>Review queue · Governance"]
    end

    subgraph VERCEL["Vercel — northstar-caseassist.vercel.app"]
        CLIENT["Client bundle (page.tsx)"]
        BFF["BFF route handlers (server-only)<br/>/api/cases /api/assistant /api/documents<br/>/api/intake /api/reviews /api/governance<br/>holds NORTHSTAR_BFF_SHARED_SECRET"]
    end

    subgraph AZURE["Azure Container App — .NET 10 minimal API"]
        EDGE["Edge middleware<br/>constant-time secret check · CORS · 60 rpm/user"]
        EP["Endpoints<br/>SystemOfRecord · CaseAssist · Documents"]
    end

    SQL[("Azure SQL<br/>system of record")]
    BLOB[("Azure Blob<br/>document content")]
    SEARCH[("Azure AI Search<br/>approved policy index")]
    OPENAI["OpenAI Responses<br/>gpt-5-mini · structured JSON"]
    SAFETY["Azure AI Content Safety"]
    INSIGHTS["Application Insights"]

    UI -->|"HTTPS same-origin"| CLIENT
    CLIENT -->|"fetch /api/*"| BFF
    BFF -->|"HTTPS + X-Northstar-Bff-Key<br/>+ X-Northstar-Demo-User + correlation-id"| EDGE
    EDGE --> EP
    EP --> SQL
    EP --> BLOB
    EP --> SEARCH
    EP --> OPENAI
    EP --> SAFETY
    EP --> INSIGHTS

    classDef boundary fill:#eef7f5,stroke:#0b3039,stroke-width:1px;
    class BROWSER,VERCEL,AZURE boundary;
```

**Trust boundary 1** = browser → Vercel (no secret ever in the browser).
**Trust boundary 2** = Vercel BFF → Azure API (shared secret + persona header). The API only
accepts traffic that presents the shared secret.

---

## 2. Enterprise security envelope — where the data is protected

The application runs **inside the agency's own Azure tenant**, under the company's existing
identity, logging, and cybersecurity controls. Protection is layered, so no single point
carries the whole burden — and case data (including an SSN in a case record) is scoped to the
**assigned caseworker only**, never broadly visible and never sent to the AI.

```mermaid
flowchart TB
    USER["Caseworker · Reviewer · Administrator"]

    subgraph L1["1 - Identity (company IAM)"]
        ENTRA["Microsoft Entra ID · MFA · conditional access<br/>app roles mapped from AD security groups"]
    end
    subgraph L2["2 - Access: need-to-know, enforced server-side"]
        SCOPE["Caseworker sees ONLY assigned cases<br/>Reviewer sees ONLY assigned reviews<br/>Submitter cannot approve own item · admin-only governance"]
    end
    subgraph L3["3 - Data protection"]
        REDACT["PII redaction before AI — SSN / name / contact removed"]
        ENC["Encryption at rest (TDE) + TLS in transit<br/>Managed identity — no stored credentials"]
    end
    subgraph L4["4 - Monitoring and audit"]
        AUD["Append-only audit trail — every access, correlation + request id"]
        SIEM["to company SIEM (Sentinel / Defender) + Azure Monitor"]
    end
    subgraph L5["5 - Platform perimeter"]
        PLAT["Azure Policy · security baseline · private networking<br/>WAF / APIM · per-user rate + budget limits"]
    end

    USER --> ENTRA --> SCOPE
    SCOPE --> REDACT --> ENC
    ENC --> AUD --> SIEM
    PLAT -. governs .- L3
    PLAT -. governs .- L4
```

| Layer | Control | Enforced where |
|---|---|---|
| **Identity** | Entra ID, MFA, app role from AD security groups | Company IAM platform (the demo stubs identity as a persona switch; the authorization that consumes it is real) |
| **Access (need-to-know)** | Caseworker → only assigned cases; reviewer → only assigned reviews; separation of duties | Application, **server-side, today** (`AssignedWorkerId == actor.UserId`; denials audited) |
| **Data protection** | PII redaction before the model; TDE at rest; TLS in transit; managed identity (no stored secrets) | Application + Azure, **today** |
| **Monitoring & audit** | Append-only audit trail of every access → company SIEM; Azure Monitor / App Insights | Audit **today**; SIEM ingestion in production |
| **Platform perimeter** | Azure Policy / security baseline, private networking, WAF/APIM, per-user rate & budget limits | Rate/budget **today**; network/WAF in production |

**On the SSN specifically:** it is stored in the case record (Azure SQL, encrypted at rest),
visible only to the caseworker the case is assigned to, and **stripped by the PII redactor
before any request reaches the AI** — so it satisfies casework need-to-know while never
entering the model or the model provider's logs.

---

## 3. The governed CaseAssist pipeline

Fires on **Ask CaseAssist**. Every numbered step writes an audit event.

```mermaid
flowchart TD
    START(["Ask CaseAssist"]) --> A1["1 · Identity: persona to actor"]
    A1 -->|"fail"| E401["401 + audit"]
    A1 --> A2["2 · Load case"]
    A2 -->|"none"| E404["404"]
    A2 --> A3{"3 · Authorize<br/>Caseworker AND assigned?"}
    A3 -->|"no"| E403["403 CASE_ACCESS_DENIED"]
    A3 -->|"yes"| A4{"4 · Validate 1..2000 chars"}
    A4 -->|"no"| E400["400"]
    A4 -->|"yes"| A5{"5 · Daily budget<br/>max 40 req and $0.25/user/day"}
    A5 -->|"over"| E429["429 + audit"]
    A5 -->|"ok"| B6["6 · Redact question (PII)"]
    B6 --> B7["7 · Prompt-injection scan"]
    B7 --> B8["8 · Build context<br/>doc-types REDACTED · background REDACTED<br/>no identity · no doc contents"]
    B8 --> B9["9 · Retrieve approved policy (AI Search, top 3)"]
    B9 --> C10["10 · Model: OpenAI structured JSON draft<br/>compose text + exact excerpts + SOURCE-ID"]
    C10 --> D11["11 · Citation validate"]
    D11 --> D12["12 · Output PII scan"]
    D12 --> D13["13 · Content safety"]
    D13 --> D14["14 · Risk classify"]
    D14 --> DEC{"Any reason codes?"}
    DEC -->|"no"| DRAFT["status DraftReady<br/>requiresReview = false"]
    DEC -->|"yes"| REVIEW["status PendingReview<br/>create ReviewItem to marcus.reed<br/>audit review.trigger"]
    DRAFT --> PERSIST["Persist AIRequest + SafetyTrace<br/>audit steps 6-14"]
    REVIEW --> PERSIST
    PERSIST --> RESP(["Return draft + sources + trace"])
```

---

## 4. Request sequence (who calls whom)

```mermaid
sequenceDiagram
    actor CW as Caseworker
    participant B as Browser SPA
    participant BFF as Vercel BFF
    participant API as Azure API
    participant R as PII Redactor
    participant S as AI Search
    participant M as OpenAI
    participant CS as Content Safety
    participant DB as Azure SQL

    CW->>B: Ask CaseAssist
    B->>BFF: POST /api/assistant
    BFF->>API: POST /case-assist/requests (secret + persona)
    API->>DB: resolve actor, load case, authorize
    API->>R: redact question + context
    API->>S: retrieve approved policy (top 3)
    API->>M: generate structured JSON draft
    M-->>API: summary + missingDocuments + citedSourceIds
    API->>R: scan output for PII
    API->>CS: content safety analyze
    API->>API: citation validate + risk classify
    alt sensitive
        API->>DB: create ReviewItem (marcus.reed)
    else clean
        API->>DB: status DraftReady
    end
    API->>DB: persist AIRequest + safety trace + audit
    API-->>BFF: draft + sources + reviewId
    BFF-->>B: draft + trace
    B-->>CW: rendered draft + safety trace
```

---

## 5. Roles, persona routing & separation of duties

```mermaid
flowchart LR
    subgraph P["Personas (browser)"]
        MAYA["Caseworker · Maya"]
        MARCUS["Reviewer · Marcus"]
        PRIYA["Admin · Priya"]
    end
    MAYA -->|"maya.chen"| RC["Caseworker"]
    MARCUS -->|"marcus.reed"| RR["Reviewer"]
    PRIYA -->|"priya.shah"| RA["Administrator"]

    RC --> W["Workspace: OWN cases<br/>Ask CaseAssist · upload · intake · submit"]
    RR --> V["Review queue: assigned items<br/>Approve / Return-with-note"]
    RA --> G["Governance · registry · evaluations<br/>audit events · reset/generate data"]

    W -. "submitter != approver" .-> V
```

Enforced server-side: caseworkers see only their own cases; reviewers decide only items
assigned to them; a submitter can never approve their own item; only admins read audit
events or run evaluations.

---

## 6. Data model (Azure SQL — all rows synthetic)

```mermaid
erDiagram
    APPLICANT   ||--|| APPLICATION  : applies
    APPLICATION ||--|| CASE         : converts
    CASE        ||--o{ CASEDOCUMENT : has
    CASE        ||--o{ CASENOTE     : has
    CASE        ||--o{ AIREQUEST    : has
    AIREQUEST   ||--o| REVIEWITEM   : routes
    CASE        ||--o{ AUDITEVENT   : records
    POLICYSECTION }o--o{ AIREQUEST  : grounds

    APPLICANT {
        int householdSize
        string syntheticName
    }
    CASE {
        string caseNumber
        string status
        string program
    }
    CASEDOCUMENT {
        string typeInFilename
        string scanStatus
    }
    CASENOTE {
        string noteType
        string content
    }
    AIREQUEST {
        string redactedQuestion
        string safetyTraceJson
        decimal estimatedCost
    }
    REVIEWITEM {
        string assignedReviewer
        string decision
    }
    POLICYSECTION {
        string sourceId
        bool isApproved
    }
```

`CaseNote` of type `CaseBackground` is the (redacted) text fed to the model as context.
Document **content** lives in Blob and is never sent to the model — only the type (filename).

---

## 7. PII & decision gates

```mermaid
flowchart TD
    Q["Inbound: question + context"] --> R1["Redact question"]
    R1 --> R2["Redact background + doc-type labels"]
    R2 --> MIN["Minimize: no identity, no doc contents"]
    MIN --> MODEL["Model — approved policy + redacted text only"]
    MODEL --> C1{"Output PII?"}
    C1 -->|"yes"| HR["Human review"]
    C1 -->|"no"| C2{"Citations valid?<br/>match approved excerpt exactly"}
    C2 -->|"no"| HR
    C2 -->|"yes"| C3{"Content safe?"}
    C3 -->|"no"| HR
    C3 -->|"yes"| C4{"Decision language?<br/>eligibility / payment / closure / contact"}
    C4 -->|"yes"| HR
    C4 -->|"no"| DR["Draft ready"]
    HR --> AUD["Audit: counts + reason codes (no raw PII)"]
    DR --> AUD
```

---

## Environments

| Layer | Value |
| --- | --- |
| Frontend | `https://northstar-caseassist.vercel.app` (Vercel, Next.js standalone) |
| API | Azure Container App `aca-northstar-api-dev-ta542fwh` (.NET 10) |
| Image | `acrnorthstardevta542fwh.azurecr.io/northstar-api:dev-20260816-11` |
| Model | OpenAI `gpt-5-mini`, Responses API, strict JSON schema |
| Data | Azure SQL · Blob · AI Search · AI Content Safety · App Insights |
