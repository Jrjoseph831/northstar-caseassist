# STRIDE threat model

Scope: the browser SPA, server-side BFF routes (Vercel), Northstar API (Azure
Container App), Azure data services, retrieval/model pipeline, and operational
telemetry. All data is fictional and marked synthetic.

| Threat | Example | Implemented mitigation | Residual / production action |
|---|---|---|---|
| Spoofing | Browser claims to be a reviewer | Server-side BFF boundary with a shared-secret header (constant-time compare); API resolves only three recognized synthetic identities; reviewer assignment checked server-side | Replace demo header identity with validated Entra tokens and app roles |
| Tampering | Change a case while another user edits | API validation and optimistic version checks for mutable case state | Use database rowversion and richer conflict UI |
| Repudiation | Reviewer denies approving a draft | Immutable decision fields plus actor, timestamp, correlation ID, request ID, and audit event | Export signed/retained events to a security platform |
| Information disclosure | SSN reaches the model or logs | Deterministic redaction before retrieval/model access; traces store types/counts only; no raw prompt in audit metadata | Add DLP/Purview classifications in a regulated deployment |
| Denial of service | Public visitor drains model balance | BFF boundary, 60/minute API rate limit, 40 requests/day/user, $0.25/day/user estimate guard, max one replica | Add APIM/WAF quotas and centralized abuse monitoring |
| Elevation of privilege | Caseworker approves their own output | API requires assigned Reviewer role and enforces submitter/reviewer separation | Entra privileged role workflow and access reviews |
| Prompt injection | Uploaded text asks to ignore policy | Documents are untrusted, scanned, quarantined on deterministic indicators, and cannot change permissions | Add Prompt Shields when suitable and retain regression cases |
| Supply chain | Compromised dependency or image | Locked dependencies, CI builds/tests, vulnerable-package report, immutable image tags | Add signed images, SBOM, Dependabot, and admission policy |

## AI-specific failure modes (attack → control)

The three failures most likely to cause harm in AI-assisted casework, and the
specific control that stops each:

| Failure mode | What it looks like | Control that stops it |
|---|---|---|
| **Prompt injection** | An uploaded document or case field contains "ignore your instructions / reveal the system prompt / act as admin" | Documents are untrusted data, never instructions: filename/type/size/UTF-8 validation, deterministic injection detection, quarantine on hit, and exclusion from the approved policy index. Document *contents* are never sent to the model. Detected indicators raise the request's risk and route it to human review. |
| **PII leak to the model or logs** | An SSN/name/address in the question, case background, or a document label reaches the model or an audit record | Deterministic redaction of the question, case background, and document labels **before** the model call; applicant identity is never included in model context; document contents are never sent; model output is re-scanned for PII (detection → review); the safety trace and audit store outcomes and counts only — no raw prompts or identifiers. |
| **Hallucinated eligibility / invented policy** | The model states someone "is eligible" or cites a benefits rule that does not exist | Answers are grounded in an approved, retrieved policy corpus; citation validation requires every cited source to have been retrieved, appear in the response, and reproduce the exact stored excerpt — otherwise the output is flagged. Eligibility/approval/denial language is detected and routed to a human; the AI has no path to record a determination. |

## Abuse cases explicitly blocked

- Eligibility approval or denial.
- Payment authorization or amount determination.
- Autonomous case closure.
- Autonomous applicant contact.
- Unsupported citations or source substitution.
- Access to a case not assigned to the requesting caseworker.
- Return/rejection without required reviewer feedback.

## Incident scenario: malicious uploaded document

1. A fictional `.txt` document contains instruction-override language.
2. The upload endpoint validates filename, type, size, and UTF-8 content.
3. The detector records reason codes and stores the document as `Quarantined`.
4. The document is not promoted into the approved policy index.
5. A safe audit event records hash, size, scan status, and reason codes—not the
   sensitive document content.
6. An administrator inspects the event and preserves the synthetic evidence.
7. In production, the event would be forwarded to the security monitoring and
   incident-response workflow.

