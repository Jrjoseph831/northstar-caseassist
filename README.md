# Northstar CaseAssist

**A reference implementation of a *governed* AI assistant for government casework.**

Northstar CaseAssist demonstrates how a public-sector agency can put a large
language model to work in a benefits-casework workflow **without letting it make
decisions, leak personal information, or invent policy** — and prove it, with an
audit trail and a control-effectiveness evaluation.

It is a reference implementation built on synthetic data. It is not a system of
record and processes no real personal information.

- **Live demo:** https://northstar-caseassist.vercel.app
- **Architecture:** [`ARCHITECTURE.md`](ARCHITECTURE.md) · [`topology.png`](topology.png)
- **Governance one-pager:** [`docs/ai-governance-summary.md`](docs/ai-governance-summary.md)

---

## The problem

Agencies that administer assistance programs (utility relief, housing stability,
workforce training) are under pressure to adopt AI, but casework is a high-stakes,
regulated setting. An LLM that summarizes a case is useful; an LLM that implies
someone is *eligible*, leaks an SSN, or cites a benefits rule it made up is a legal
and human-harm incident. The hard part is not calling a model — it is building the
**controls that make the model safe to deploy**.

## What it does

A caseworker asks CaseAssist to summarize a case or identify missing documents. The
assistant returns a **grounded draft** — a summary, a document-gap analysis against
approved policy, and cited sources — plus a **safety trace** showing every control
that ran. Sensitive requests route to a human reviewer. Administrators see governance
metrics, the AI system registry, and the audit trail.

## The controls (what makes it governed)

| Control | What it does |
|---|---|
| **Decision boundary** | The AI cannot approve/deny eligibility, authorize payment, close a case, or contact a resident. Such requests are flagged and routed to a human. |
| **Human-in-the-loop** | High-risk output creates a review item for a *separate* reviewer; submitter ≠ approver; returns require a written note. |
| **PII redaction** | The question, case background, and document labels are redacted before the model. Applicant identity and document *contents* are never sent to the model. Output is re-scanned for PII. |
| **Policy grounding** | Answers are retrieved from an approved policy corpus; every citation must match a retrieved, exact excerpt or the output is flagged. No invented rules. |
| **Prompt-injection resistance** | Uploaded documents are untrusted, scanned, and quarantined on injection indicators; they cannot change permissions. |
| **Auditability** | Append-only audit events keyed by correlation and request IDs; the safety trace stores outcomes and counts, never raw prompts or identifiers. |
| **Abuse/cost guards** | BFF shared-secret boundary, per-user rate limits and daily budget caps, content-safety moderation. |

## Architecture

Browser SPA → **Vercel BFF** (holds the API secret, server-side only) →
**Azure Container App** (.NET 10 minimal API) → Azure SQL, Blob, AI Search,
OpenAI Responses, AI Content Safety. Full diagrams in [`ARCHITECTURE.md`](ARCHITECTURE.md).

- **Frontend:** Next.js 16 (React), server-side BFF route handlers
- **API:** .NET 10 minimal API, EF Core, managed identity to Azure services
- **AI:** OpenAI Responses (`gpt-5-mini`) with strict JSON-schema output; grounded
  retrieval; deterministic offline fixture for reproducible, zero-cost tests
- **Data:** Azure SQL (system of record), Blob (documents), AI Search (policy index)
- **Governance:** control-evaluation runner, AI system registry, append-only audit store

## How it maps to NIST AI RMF

Govern / Map / Measure / Manage are addressed in
[`docs/ai-governance-summary.md`](docs/ai-governance-summary.md): documented purpose
and prohibited uses with an in-product registry (Govern); a scoped, high-impact-tier
use case with enumerated abuse cases (Map); a deterministic control-effectiveness
golden dataset and per-request trace (Measure); and a human-review queue with
rate/budget guards and versioned change management (Manage).

## What a production deployment would add (honest gaps)

This is a reference implementation. To run for real, an agency would add:

1. **Identity** — Microsoft Entra ID with MFA and provisioned app roles (the demo
   uses a persona switch).
2. **Data governance** — Purview/DLP classification, retention & records management
   (public records / FOIA), and Azure Government data residency.
3. **Evaluation depth** — a labeled Q&A accuracy set, bias testing across cohorts,
   and drift monitoring on model/prompt changes (the current suite measures control
   effectiveness).
4. **Accessibility** — a formal WCAG 2.1 AA / Section 508 audit (the UI is built
   508-conscious: keyboard focus, skip link, live regions, labeled controls).
5. **Scale & continuity** — autoscale, WAF/APIM quotas, and a documented DR posture
   (the demo is a single scale-to-zero container).

## Run it locally

Local development uses SQLite and a deterministic offline model, so the full
governance pipeline runs with no cloud dependencies and no model cost.

```bash
# API (.NET 10)
dotnet test                              # unit + integration tests
dotnet run --project src/Northstar.Api

# Frontend (Next.js)
npm install
npm run dev
```

Role checks use the `X-Northstar-Demo-User` header (`maya.chen`, `marcus.reed`,
`priya.shah`) — an explicit local substitute for Entra ID. Permissions are enforced
by the API, not by frontend visibility. Synthetic reset requires the exact
confirmation `DELETE SYNTHETIC DATA` and only deletes records flagged `isSynthetic`.

## Documentation

- [Architecture & data flow](ARCHITECTURE.md) · [topology diagram](topology.png)
- [**AI governance summary (NIST AI RMF)**](docs/ai-governance-summary.md)
- [AI system card](docs/ai-system-card.md)
- [STRIDE threat model](docs/threat-model.md)
- [Controls, classification & retention](docs/controls-and-data.md)
- [Role & permission matrix](docs/role-permission-matrix.md)
- [Evaluation methodology](docs/evaluation-methodology.md)
- [AI safety pipeline](docs/ai-safety-pipeline.md)
- [Operations & teardown runbook](docs/operations-runbook.md)
- [Demonstration script](docs/demo-script.md)

---

*Synthetic data only. No real personal information is stored or processed.*
