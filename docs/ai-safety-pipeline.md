# AI Request Safety Pipeline

The implemented .NET request flow is:

1. Resolve a recognized synthetic development identity.
2. Verify the user is the case's assigned Caseworker.
3. Redact supported PII from the question.
4. Detect prompt-injection indicators.
5. Retrieve approved policy sections for the case's program: the redacted question
   is expanded into similar queries, each query is searched with both dense
   vectors and BM25 sparse vectors, the ranked lists are combined with
   reciprocal rank fusion, and the shortlist is reranked with late-interaction
   scoring. Only approved sections for that program enter the candidate pool, so
   ranking can reorder evidence but cannot introduce an unapproved source. See
   `architecture.md` for the stages and `evaluation-methodology.md` for how they
   are measured.
6. Call the configured provider through `IAssistantModelProvider`.
7. Validate that citations were retrieved, displayed, and accompanied by the
   stored supporting excerpt.
8. Scan the output for PII and protected actions.
9. Calculate a risk classification and reason codes.
10. Return a draft or persist a review item assigned to a separate reviewer.
11. Persist the safe trace and material audit events.

Document text is treated as evidence. It cannot alter authorization, provider
selection, prompt constraints, review routing, or tool permissions. The safety
trace intentionally omits original redacted values, credentials, raw tokens,
hidden instructions, and chain-of-thought. The generated search queries are
recorded in the trace with every identifier category removed, including the
categories DHR-4.2 permits the assistant to process, because DHR-4.3 keeps
unredacted prompts out of audit records.
