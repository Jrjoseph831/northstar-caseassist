# AI Request Safety Pipeline

The implemented .NET request flow is:

1. Resolve a recognized synthetic development identity.
2. Verify the user is the case's assigned Caseworker.
3. Redact supported PII from the question.
4. Detect prompt-injection indicators.
5. Search approved policy sections for the case's program.
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
hidden instructions, and chain-of-thought.
