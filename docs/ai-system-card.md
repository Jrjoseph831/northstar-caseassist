# Northstar CaseAssist — AI System Card

## System identity

- System: Northstar CaseAssist
- Owner: Northstar Responsible AI Office (fictional)
- Environment: reference demonstration using synthetic data only
- Deployed model mode: bounded server-side OpenAI Responses API using `gpt-5-mini`
- Local test mode: deterministic `OfflineFixture`
- Current prompt version: `northstar-prompt-v1`
- Current dataset version: `golden-v2`

## Approved purpose

- Case summaries
- Document-completeness checks
- Search over approved fictional policies
- Communication drafts for caseworker review

## Prohibited uses

- Eligibility approval or denial
- Payment authorization or amount determination
- Case closure
- Autonomous applicant contact

## Human oversight

High-impact, ambiguous, unsafe, injection-affected, PII-bearing, or
unsupported outputs create a persistent review item assigned to a separate
reviewer. The current synthetic caseworker is Maya Chen and the current
synthetic reviewer is Marcus Reed. The API blocks unauthorized decisions and
checks that submitter and reviewer differ.

## Implemented controls

| Control | Current evidence |
|---|---|
| Assigned-case authorization | Enforced before every CaseAssist request; denials are audited. |
| Minimum necessary processing | The model receives only the redacted question, non-identifying case facts (program, status, household size), redacted case background, redacted document-type labels, and retrieved approved policy. Applicant identity and document contents are never sent. |
| PII redaction | SSN, email, phone, case identifiers, street addresses, and labeled synthetic people are replaced before provider access. |
| Approved retrieval | Only approved sections for the case's program enter the candidate pool. Multi-query expansion, dense and sparse hybrid search, rank fusion and late-interaction reranking reorder that pool; they cannot add a source to it. |
| Retrieval quality measurement | recall@3, precision@3, MRR, nDCG@3 and hit rate are measured against a labeled question set on every evaluation run, alongside faithfulness, grounding and latency. |
| Citation validation | Every cited source must have been retrieved, appear in the response, and include the stored supporting excerpt. |
| Prompt-injection detection | Deterministic patterns detect instruction overrides, role changes, system-prompt references, and secret-exfiltration requests. |
| Output controls | PII, protected decisions, payment actions, closure, contact, conflict, injection, and invalid citations affect risk routing. |
| Separate review | Persistent review item assigned to a different synthetic reviewer. |
| Safety trace | Per-request safe metadata records executed controls without original redacted values or hidden prompts. |
| Evaluation evidence | Administrator-triggered checks calculate and persist actual totals and failures. |

## Current limitations and production mapping

- The local candidate store and the deployed Azure AI Search index share the
  same candidate-source interface; the retrieval stages that rank the pool run
  identically either way. Dense embeddings default to a local deterministic
  model and can be switched to a hosted embedding service by configuration.
- The deployed provider uses a 450-token output ceiling, low verbosity, minimal
  reasoning effort, disabled response storage, and persisted token/cost usage.
  A live provider failure is surfaced rather than mislabeled as fixture output.
- Development identity headers will be replaced by validated Microsoft Entra ID
  tokens and app roles.
- Local SQLite and deployed serverless Azure SQL share the same data model.
- Deterministic injection rules remain active alongside live Azure AI Content
  Safety analysis.
