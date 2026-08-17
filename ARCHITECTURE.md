# Architecture

**The whole idea in one line:** redact on the way in, ground every answer in approved policy,
check it on the way out, and let a human — never the AI — make the call. Every step is logged.

Three diagrams: how it's built, how it's secured, and how the AI is kept in line.

---

## How it's built

![Deployment topology](docs/diagrams/diagram-1.png)

The browser never holds a secret. It talks to a **Next.js BFF on Vercel**, which is the only
place the API secret lives. The BFF calls the **.NET API on Azure**, which does the real work and
reaches the data and AI services. Two trust boundaries: browser → Vercel, and Vercel → Azure
(the API answers only to callers that present the secret).

## How it's secured

![Security envelope](docs/diagrams/diagram-2.png)

Five layers, so no single control carries the whole load. A case — including its SSN — is scoped
to the **one caseworker it's assigned to**, encrypted at rest, and stripped before it ever
reaches the AI. Document *contents* stay in Blob storage and never go to the model; only the
document *type* is used to spot what's missing.

| Layer | Control |
|---|---|
| **Identity** | Entra ID, MFA, app roles from AD groups |
| **Access** | Need-to-know, enforced server-side (`AssignedWorkerId == actor.UserId`) |
| **Data** | PII redacted before the model · encrypted at rest · TLS in transit · managed identity |
| **Monitoring** | Append-only audit trail → company SIEM + Azure Monitor |
| **Perimeter** | Azure Policy, private networking, WAF/APIM, rate + budget limits |

The demo stands in for identity with a persona switch, but the authorization that consumes it is
real and runs server-side.

## How the AI is kept in line

![Governed pipeline](docs/diagrams/diagram-3.png)

Every request runs the same gauntlet. It's authorized, PII-redacted, and grounded in retrieved
policy *before* the model runs. The draft that comes back is checked for invented citations,
leaked PII, unsafe content, and decision language like "approved" or "denied." Clean drafts go to
the caseworker; anything flagged routes to a **separate human reviewer** — never the person who
asked. The AI can draft, but it can't decide.

---

## Where it runs

| | |
| --- | --- |
| Frontend | `northstar-caseassist.vercel.app` — Next.js on Vercel |
| API | Azure Container App — .NET 10 |
| Model | OpenAI `gpt-5-mini` — Responses API, strict JSON schema |
| Data | Azure SQL · Blob · AI Search · Content Safety · App Insights |
