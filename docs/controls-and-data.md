# Controls, data classification, and retention

## Control evidence map

| Objective | Executing component | Evidence |
|---|---|---|
| Assigned-case access | `DemoIdentityService` and case endpoint authorization | Allowed/denied API response and `case.access` audit event |
| Minimum necessary model input | PII redactor and controlled model request | Redaction summary and safety trace |
| Approved grounding | `HybridPolicyRetriever` over an approved, program-filtered candidate pool | Retrieved source IDs/excerpts, per-stage ranking, and `policy.retrieve` event |
| Citation integrity | `CitationValidator` | Validation result/reason codes in trace and audit |
| Protected decision boundary | `RiskClassifier` | High-risk reason codes and persistent review item |
| Separation of duties | Review decision endpoint | Submitter/reviewer IDs and rejected self-approval |
| Content safety | Azure AI Content Safety scanner | Provider, live flag, categories, and reason codes |
| Prompt-injection defense | Upload/request deterministic detector | Quarantine status and injection reason codes |
| Usage control | ASP.NET rate limiter and persisted daily usage guard | HTTP 429 plus safe denial audit event |
| Operational monitoring | Azure Monitor OpenTelemetry | Sampled request/dependency/exception telemetry |
| Governance evidence | SQL registry, evaluation, review, and audit records | Calculated dashboard values |

## Data classification and retention

| Data | Classification | Reference storage | Retention approach |
|---|---|---|---|
| Fictional applicant/case records | Synthetic confidential | Azure SQL | Kept for demo until project teardown or synthetic reset |
| Uploaded fictional documents | Synthetic confidential / untrusted | Private Blob container | Kept until case/demo teardown; quarantined status retained |
| Approved fictional policies | Public demo content | Repository, Blob, AI Search | Versioned with source; superseded versions retained in source history |
| Redacted question and AI draft | Synthetic internal | Azure SQL | Kept for traceability; original unredacted prompt is not stored |
| Application audit event | Synthetic audit metadata | Azure SQL | Kept with demo records; reset deletes synthetic records only |
| Operational telemetry | Internal operational | Application Insights / Log Analytics | 30 days; 25% sampling; 0.05 GB/day ingestion cap |
| Secrets | Restricted | Sites/Azure host-managed secret settings | Never committed, returned to browsers, or included in audit metadata |

## Prompt and policy versioning

- Each AI request persists `northstar-prompt-v1` and the resolved model name.
- Policy sections have stable IDs, document titles, versions, and section labels.
- The Search index is explicitly named `approved-policy-sections-v1`.
- Evaluation runs persist evaluation, dataset, prompt, and model versions.
- A change to prompt or policy semantics requires a new version and a new
  evaluation run before the governance record is updated.

