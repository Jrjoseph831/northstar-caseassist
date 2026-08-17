# AI Governance Summary — Northstar CaseAssist

**Audience:** agency CIO / CISO / privacy & AI governance office.
**Purpose:** a one-page account of how an AI assistant is bounded, overseen, and
audited before it is allowed near a benefits-casework workflow.
**Status:** reference implementation on synthetic data only. Not a system of record.

---

## What it is (and is not)

Northstar CaseAssist is a **decision-support** assistant for caseworkers handling
assistance programs (utility relief, housing stability, workforce training). It
drafts case summaries, checks document completeness against approved policy, and
produces communication drafts. **It does not make decisions.** Eligibility,
payment, case closure, and resident contact are reserved to authorized staff and
are structurally blocked in the system.

## Risk tier

**High-impact support system.** The workflow affects a resident's access to
benefits, so every output is treated as consequential even though the AI cannot
act. This tier drives mandatory human oversight, policy grounding, and full
auditability.

## Human oversight model

- The AI produces a **draft**, never a determination.
- Any output that drifts toward a protected action (eligibility/payment/closure/
  contact), fails citation validation, contains PII, trips content safety, or
  shows prompt-injection indicators is **automatically routed to a separate human
  reviewer** before it can be used.
- **Separation of duties** is enforced: the person who submits an item for review
  cannot approve it. Return/rejection requires a written reviewer note.
- Every step is written to an **append-only audit trail** keyed by correlation and
  request IDs.

## Prohibited uses (enforced, not just documented)

| Prohibited | Control that blocks it |
|---|---|
| Determine eligibility (approve/deny) | Risk classifier routes to human review; language never treated as a decision |
| Authorize payment or set an amount | Same — flagged and routed, never actioned |
| Close a case | No autonomous write path; flagged and routed |
| Contact a resident | No outbound channel; flagged and routed |

## PII handling (minimum necessary)

- The caseworker's question, the case background, and document-type labels are
  **redacted before the model sees them** (email, SSN, phone, case IDs, street
  addresses, labeled names).
- The applicant's identity (name, contact, address) is **never sent to the model**;
  only non-identifying facts (program, household size) and approved policy.
- **Document contents are never sent to the model** — only the document *type* is
  used for gap analysis.
- Model **output is scanned for PII**; any detection routes to human review.
- The **safety trace stores control outcomes and counts only** — no raw prompts,
  no original identifiers.

## NIST AI RMF (AI 100-1) mapping

| Function | How this system addresses it |
|---|---|
| **GOVERN** | Documented approved purpose and prohibited uses; an in-product **AI System Registry** (owner, purpose, prohibited uses, next review date); role-based access with separation of duties; append-only audit trail. See `ai-system-card.md`, `role-permission-matrix.md`. |
| **MAP** | Use case scoped to decision *support*, not decision *making*; high-impact tier declared; residents and caseworkers identified as affected parties; explicit abuse cases enumerated. See `threat-model.md`. |
| **MEASURE** | Deterministic **control-effectiveness golden dataset** (`golden-v2`, ~18 labeled scenarios) verifying PII redaction, injection resistance, decision-boundary routing, citation grounding, and no-false-positive baselines; per-request safety trace; cost/usage metering. See `evaluation-methodology.md`. |
| **MANAGE** | Human-in-the-loop review queue for all high-risk output; rate limits and per-user daily budget caps; content-safety moderation; incident path for quarantined documents; versioned prompt/model/dataset for change management. See `operations-runbook.md`. |

## Grounding & anti-hallucination

Answers are grounded in an **approved policy corpus** via retrieval. Every policy
claim must cite a source that was actually retrieved, appear in the response, and
reproduce the exact approved excerpt — otherwise the output is flagged and routed
to review. The assistant cannot invent a benefits rule.

## Honest limitations (what a production deployment would add)

1. **Identity:** demo uses a persona switch; production uses Microsoft Entra ID
   with MFA and provisioned app roles.
2. **Data governance:** add Microsoft Purview/DLP classification, retention and
   records-management (public-records/FOIA) policy, and Azure Government residency.
3. **Evaluation depth:** current suite measures *control effectiveness*; production
   adds a labeled Q&A accuracy set, bias testing across cohorts, and drift
   monitoring on every model/prompt change.
4. **Accessibility:** built 508-conscious (keyboard focus, skip link, live regions,
   labeled controls); production requires a formal WCAG 2.1 AA audit.
5. **Scale/continuity:** demo runs a single scale-to-zero container; production
   adds autoscale, WAF/APIM quotas, and a DR posture.

*All records in this system are synthetic. No real personal information is stored
or processed.*
