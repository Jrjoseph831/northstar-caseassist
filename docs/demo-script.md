# Three-to-five minute reference demonstration

## 0:00–0:35 — Architecture and scope

- Open the private portal and point out the synthetic-data disclaimer.
- Explain the Sites BFF → Container Apps → Azure SQL/Search/Blob/Safety flow.
- State that the API is the system of record and the AI returns drafts only.

## 0:35–1:15 — Intake and case ownership

- As Maya, choose **+ Intake**.
- Select citizen demo or employee-assisted intake and keep all fictional values.
- Submit and convert; show the new application/case confirmation.
- Refresh the case workspace and show that the new assigned case came from
  Azure SQL.

## 1:15–2:05 — Normal grounded assistance

- Open NS-1048 and ask for a summary and missing documents without asking for a
  protected decision.
- Open the safety trace: identify PII counts, approved Search sources, model and
  prompt versions, token/cost usage, Content Safety, and audit IDs.
- Point out exact policy citations.

## 2:05–2:55 — Protected decision and separate review

- Ask whether the applicant should be approved or paid.
- Show high-risk classification and automatic review routing.
- Switch to Marcus; open Review queue.
- Return with a required note or approve only as a draft.
- Explain that Maya cannot decide her own item and the API—not the button—enforces
  this boundary.

## 2:55–3:35 — Injection control

- Open Documents and upload the curated fictional malicious text file.
- Show `Quarantined` plus the injection reason codes.
- Explain that uploaded evidence cannot change system instructions or roles.

## 3:35–4:15 — Governance evidence

- Switch to Priya and open Governance.
- Show calculated request/review counts, stored evaluation totals, registry
  purpose/prohibited uses, and real audit events.
- Point out Application Insights as operational telemetry, separate from the
  application audit log.

## 4:15–4:40 — Close

- Show the test/CI summary and Bicep resources.
- Identify substitutions: synthetic personas instead of Entra app roles, BFF
  credential instead of workload federation, and structured audit events rather
  than a deployed Sentinel solution.

