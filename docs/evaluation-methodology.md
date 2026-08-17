# Evaluation methodology and evidence

The default automated suite does not call a live model. Deterministic fixtures
exercise the same authorization, PII, retrieval, citation, classification,
review, trace, and audit code paths at predictable cost.

## Curated coverage

The `golden-v1` dataset contains twelve stable scenarios covering complete and
incomplete applications, duplicate/conflicting records, unauthorized access,
PII, prompt injection, harmless drafting, protected decisions, and unsupported
citations. Generated cases supplement these scenarios and never replace them.

The persisted control evaluation checks at least:

- PII detection and redaction.
- Case access enforcement.
- Approved-source retrieval and citation validity.
- Protected-decision detection and review routing.
- Submitter/reviewer separation.
- Prompt-injection detection.
- Safe audit metadata.
- Synthetic reset constraints.

## Evidence rules

- The dashboard displays only stored `EvaluationRun` totals.
- A run total is always equal to passed plus failed.
- Live-model smoke requests are separate from deterministic evaluation totals.
- A failed dependency is surfaced as a failure; it is not silently relabeled as
  a successful fixture response.
- Release evidence includes the source commit, test output, Azure revision,
  request IDs, and safe trace fields.

## Commands

```powershell
dotnet test Northstar.slnx
npm run lint
npm test
az bicep build --file infrastructure/bicep/container-app.bicep --stdout
```

An administrator can persist the current control evaluation with:

```text
POST /api/v1/governance/evaluations/run
X-Northstar-Demo-User: priya.shah
```

