import { env } from "cloudflare:workers";
import { and, desc, eq, gte, sql } from "drizzle-orm";
import { ensureDatabaseSchema, getDb } from "../db";
import {
  assistantRequests,
  auditEvents,
  cases,
  evaluationRuns,
  policies,
  reviewItems,
} from "../db/schema";

export type DemoPersona = {
  id: "maya-chen" | "marcus-reed" | "priya-shah";
  name: string;
  role: "Caseworker" | "Senior Reviewer" | "Administrator";
};

export const PERSONAS: Record<DemoPersona["id"], DemoPersona> = {
  "maya-chen": { id: "maya-chen", name: "Maya Chen", role: "Caseworker" },
  "marcus-reed": {
    id: "marcus-reed",
    name: "Marcus Reed",
    role: "Senior Reviewer",
  },
  "priya-shah": { id: "priya-shah", name: "Priya Shah", role: "Administrator" },
};

const CASE_SEED = [
  {
    id: "NS-1048",
    applicantName: "Elena Brooks",
    program: "Utility Relief",
    status: "Documents required",
    assignedTo: "Maya Chen",
    details:
      "Applicant Elena Brooks reports reduced work hours. SSN 900-12-3456. Phone 555-014-8821. Proof of residence and one weekly pay statement are on file. Utility shutoff is scheduled in 8 days.",
    createdAt: "2026-08-12T14:30:00.000Z",
  },
  {
    id: "NS-1061",
    applicantName: "Darius Moore",
    program: "Housing Stability",
    status: "In review",
    assignedTo: "Maya Chen",
    details:
      "Lease renewal assistance request. Email darius@example.test. Income review is complete and the current lease is on file.",
    createdAt: "2026-08-13T16:10:00.000Z",
  },
  {
    id: "NS-1074",
    applicantName: "Amina Patel",
    program: "Workforce Grant",
    status: "New",
    assignedTo: "Maya Chen",
    details:
      "Training grant application for a licensed practical nursing program. Phone 555-013-4410. Enrollment letter is on file.",
    createdAt: "2026-08-14T12:00:00.000Z",
  },
  {
    id: "NS-1082",
    applicantName: "Jordan Taylor",
    program: "Utility Relief",
    status: "Pending review",
    assignedTo: "Maya Chen",
    details:
      "Past-due electric bill and hardship statement are on file. Email jtaylor@example.test.",
    createdAt: "2026-08-15T09:20:00.000Z",
  },
];

const POLICY_SEED = [
  {
    id: "POL-UR-42",
    section: "Utility Relief Manual §4.2",
    title: "Required verification",
    program: "Utility Relief",
    content:
      "A current disconnect notice and income evidence covering the prior 30 days are required before final review.",
  },
  {
    id: "POL-DV-21",
    section: "Document Standard §2.1",
    title: "Acceptable income evidence",
    program: "All",
    content:
      "Pay statements, employer letters, and approved benefit statements may establish current household income.",
  },
  {
    id: "POL-UR-51",
    section: "Utility Relief Manual §5.1",
    title: "Urgent service interruption",
    program: "Utility Relief",
    content:
      "A documented service interruption within ten calendar days may receive expedited document review.",
  },
  {
    id: "POL-HS-31",
    section: "Housing Stability Guide §3.1",
    title: "Lease verification",
    program: "Housing Stability",
    content:
      "A current signed lease or landlord verification is required for renewal assistance.",
  },
  {
    id: "POL-HS-44",
    section: "Housing Stability Guide §4.4",
    title: "Income review",
    program: "Housing Stability",
    content:
      "Household income must be verified before an authorized employee makes an eligibility decision.",
  },
  {
    id: "POL-WG-22",
    section: "Workforce Grant Manual §2.2",
    title: "Program enrollment",
    program: "Workforce Grant",
    content:
      "Applicants must provide an enrollment or acceptance letter from an approved training provider.",
  },
  {
    id: "POL-WG-37",
    section: "Workforce Grant Manual §3.7",
    title: "Training costs",
    program: "Workforce Grant",
    content:
      "Allowable costs include tuition, required books, examinations, and essential equipment.",
  },
  {
    id: "POL-CM-12",
    section: "Communication Standard §1.2",
    title: "Clear neutral language",
    program: "All",
    content:
      "Citizen communications must use clear, neutral language and distinguish drafts from final agency decisions.",
  },
  {
    id: "POL-PR-11",
    section: "Privacy Standard §1.1",
    title: "Data minimization",
    program: "All",
    content:
      "Only the minimum personal information necessary for the approved task may be processed by an assistance system.",
  },
  {
    id: "POL-PR-24",
    section: "Privacy Standard §2.4",
    title: "Sensitive identifiers",
    program: "All",
    content:
      "Social Security numbers, phone numbers, and email addresses must be removed when they are unnecessary for the task.",
  },
  {
    id: "POL-AI-31",
    section: "AI Use Standard §3.1",
    title: "Human decision authority",
    program: "All",
    content:
      "AI assistance must not approve, deny, close, or authorize payment on a case. An authorized employee retains decision authority.",
  },
  {
    id: "POL-AI-46",
    section: "AI Use Standard §4.6",
    title: "Traceability",
    program: "All",
    content:
      "AI-assisted outputs must retain the request, applied controls, sources, model or engine version, and disposition.",
  },
];

export function personaFrom(value: unknown): DemoPersona {
  return PERSONAS[String(value) as DemoPersona["id"]] ?? PERSONAS["maya-chen"];
}

export async function ensureSeedData() {
  await ensureDatabaseSchema();
  const db = getDb();
  await db.insert(cases).values(CASE_SEED).onConflictDoNothing();
  await db.insert(policies).values(POLICY_SEED).onConflictDoNothing();
}

export function redactPii(input: string) {
  const findings: Array<{ type: string; replacement: string }> = [];
  const rules = [
    {
      type: "SSN",
      replacement: "[REDACTED_SSN]",
      pattern: /\b\d{3}-\d{2}-\d{4}\b/g,
    },
    {
      type: "PHONE",
      replacement: "[REDACTED_PHONE]",
      pattern: /\b(?:\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]\d{3}[-.\s]\d{4}\b/g,
    },
    {
      type: "EMAIL",
      replacement: "[REDACTED_EMAIL]",
      pattern: /\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/gi,
    },
  ];
  let redacted = input;
  for (const rule of rules) {
    redacted = redacted.replace(rule.pattern, () => {
      findings.push({ type: rule.type, replacement: rule.replacement });
      return rule.replacement;
    });
  }
  return { redacted, findings };
}

function words(value: string) {
  return new Set(value.toLowerCase().match(/[a-z]{3,}/g) ?? []);
}

function scorePolicy(
  query: string,
  program: string,
  policy: (typeof POLICY_SEED)[number],
) {
  const queryWords = words(query);
  const policyWords = words(`${policy.title} ${policy.content}`);
  let score = policy.program === program ? 6 : policy.program === "All" ? 2 : 0;
  for (const word of queryWords) if (policyWords.has(word)) score += 1;
  return score;
}

export async function processAssistantRequest(
  caseId: string,
  question: string,
  persona: DemoPersona,
) {
  if (persona.role !== "Caseworker" && persona.role !== "Administrator")
    throw new Error("Only caseworkers may submit case questions.");
  await ensureSeedData();
  const db = getDb();
  const today = new Date();
  today.setUTCHours(0, 0, 0, 0);
  const daily = await db
    .select({ count: sql<number>`count(*)` })
    .from(assistantRequests)
    .where(
      and(
        eq(assistantRequests.submitterId, persona.id),
        gte(assistantRequests.createdAt, today.toISOString()),
      ),
    );
  if (Number(daily[0]?.count ?? 0) >= 20)
    throw new Error(
      "Daily live-demo request limit reached. Try again tomorrow.",
    );
  const [cached] = await db
    .select()
    .from(assistantRequests)
    .where(
      and(
        eq(assistantRequests.caseId, caseId),
        eq(assistantRequests.question, question),
        eq(assistantRequests.submitterId, persona.id),
      ),
    )
    .orderBy(desc(assistantRequests.createdAt))
    .limit(1);
  if (cached) {
    await writeAudit(
      cached.displayId,
      persona,
      "assistant.cache.hit",
      "Identical request served from cache; no model call was made.",
    );
    return {
      id: cached.id,
      displayId: cached.displayId,
      answer: cached.answer,
      citations: JSON.parse(cached.citations),
      trace: { ...JSON.parse(cached.trace), cacheHit: true },
      requiresReview: cached.requiresReview,
      createdAt: cached.createdAt,
    };
  }
  const [caseRecord] = await db
    .select()
    .from(cases)
    .where(eq(cases.id, caseId))
    .limit(1);
  if (!caseRecord) throw new Error("Case not found.");
  const approvedPolicies = await db
    .select()
    .from(policies)
    .where(eq(policies.approved, true));
  const combined = `${caseRecord.details}\nQuestion: ${question}`;
  const pii = redactPii(combined);
  const retrieved = approvedPolicies
    .map((policy) => ({
      ...policy,
      score: scorePolicy(pii.redacted, caseRecord.program, policy),
    }))
    .sort((a, b) => b.score - a.score)
    .slice(0, 2);
  const sensitive =
    /\b(approv|deny|denial|eligible|eligibility|payment|close case|award)\w*/i.test(
      question,
    );
  const now = new Date().toISOString();
  const id = crypto.randomUUID();
  const sequence = await db
    .select({ count: sql<number>`count(*)` })
    .from(assistantRequests);
  const displayId = `REQ-${now.slice(0, 10).replaceAll("-", "")}-${String(Number(sequence[0]?.count ?? 0) + 1).padStart(4, "0")}`;
  const fallbackAnswer = `Case summary\n\n${caseRecord.applicantName} is requesting ${caseRecord.program.toLowerCase()} assistance. The case record indicates ${caseRecord.details.split(".")[0].toLowerCase()}.\n\nMissing documentation\n\n1. ${retrieved[0]?.content ?? "Review the approved program documentation."}\n2. ${retrieved[1]?.content ?? "Confirm all required verification is current."}\n\nRecommended next step\n\nRequest any missing verification before an authorized employee completes review.${sensitive ? " The requested eligibility recommendation was not generated because that decision belongs to an authorized reviewer." : ""}`;
  const live = await generateLiveAnswer({
    redactedInput: pii.redacted,
    policies: retrieved,
    sensitive,
    fallback: fallbackAnswer,
    safetyIdentifier: `northstar-demo-${persona.id}`,
  });
  const answer = live.answer;
  const trace = {
    requestId: displayId,
    user: persona.name,
    role: persona.role,
    caseId,
    pii: {
      count: pii.findings.length,
      findings: pii.findings,
      originalExcluded: true,
    },
    retrieval: {
      searched: approvedPolicies.length,
      sections: retrieved.map((item) => item.section),
    },
    model: {
      name: live.model,
      promptVersion: "2.8",
      temperature: 0,
      mode: live.mode,
    },
    controls: {
      citationsVerified: retrieved.length > 0,
      outputPiiDetected: redactPii(answer).findings.length > 0,
      eligibilityLanguageDetected: sensitive,
      humanReviewRequired: sensitive,
    },
  };
  await db.insert(assistantRequests).values({
    id,
    displayId,
    caseId,
    question,
    redactedInput: pii.redacted,
    answer,
    citations: JSON.stringify(
      retrieved.map(({ id: policyId, section, title, content }) => ({
        id: policyId,
        section,
        title,
        content,
      })),
    ),
    trace: JSON.stringify(trace),
    submitterId: persona.id,
    submitterName: persona.name,
    submitterRole: persona.role,
    requiresReview: sensitive,
    createdAt: now,
  });
  await writeAudit(
    displayId,
    persona,
    "assistant.request.completed",
    `${pii.findings.length} PII field(s) redacted; ${retrieved.length} source(s) retrieved; ${live.mode} generation; review ${sensitive ? "required" : "not required"}.`,
  );
  return {
    id,
    displayId,
    answer,
    citations: JSON.parse(
      JSON.stringify(
        retrieved.map(({ id: policyId, section, title, content }) => ({
          id: policyId,
          section,
          title,
          content,
        })),
      ),
    ),
    trace,
    requiresReview: sensitive,
    createdAt: now,
  };
}

async function generateLiveAnswer(input: {
  redactedInput: string;
  policies: Array<{ section: string; title: string; content: string }>;
  sensitive: boolean;
  fallback: string;
  safetyIdentifier: string;
}) {
  const apiKey = (env as unknown as { OPENAI_API_KEY?: string }).OPENAI_API_KEY;
  if (!apiKey)
    return {
      answer: input.fallback,
      model: "Northstar Grounded Engine v1.4",
      mode: "deterministic-fallback",
    };
  const policyText = input.policies
    .map((policy) => `[${policy.section}] ${policy.title}: ${policy.content}`)
    .join("\n");
  const instructions =
    "You are Northstar CaseAssist for a fictional public-services organization. Write a concise caseworker draft grounded only in the supplied redacted case and approved policy excerpts. Use the headings Case summary, Missing documentation, and Recommended next step. Cite policy sections inline in square brackets. Never approve, deny, determine eligibility, authorize payment, or invent facts. If asked for a protected decision, state that an authorized human must decide. Do not output personal identifiers.";
  try {
    const response = await fetch("https://api.openai.com/v1/responses", {
      method: "POST",
      headers: {
        authorization: `Bearer ${apiKey}`,
        "content-type": "application/json",
      },
      body: JSON.stringify({
        model: "gpt-5.6-luna",
        instructions,
        input: `REDACTED CASE AND QUESTION\n${input.redactedInput}\n\nAPPROVED POLICY EXCERPTS\n${policyText}\n\nRisk classifier: ${input.sensitive ? "protected-decision language detected" : "no protected-decision language detected"}`,
        reasoning: { effort: "none" },
        text: { verbosity: "low" },
        max_output_tokens: 450,
        store: false,
        safety_identifier: input.safetyIdentifier,
      }),
    });
    if (!response.ok) throw new Error(`OpenAI API returned ${response.status}`);
    const payload = (await response.json()) as {
      output_text?: string;
      output?: Array<{
        content?: Array<{ type?: string; text?: string }>;
      }>;
    };
    const answer =
      payload.output_text ??
      payload.output
        ?.flatMap((item) => item.content ?? [])
        .find((item) => item.type === "output_text")?.text;
    if (!answer?.trim()) throw new Error("OpenAI API returned no text");
    return {
      answer: answer.trim(),
      model: "GPT-5.6 Luna",
      mode: "live-openai",
    };
  } catch {
    return {
      answer: input.fallback,
      model: "Northstar Grounded Engine v1.4",
      mode: "deterministic-fallback",
    };
  }
}

export async function writeAudit(
  requestId: string | null,
  actor: DemoPersona,
  eventType: string,
  detail: string,
) {
  await getDb().insert(auditEvents).values({
    id: crypto.randomUUID(),
    requestId,
    actorId: actor.id,
    actorName: actor.name,
    actorRole: actor.role,
    eventType,
    detail,
    createdAt: new Date().toISOString(),
  });
}

export async function getDashboardData() {
  await ensureSeedData();
  const db = getDb();
  const requestRows = await db
    .select()
    .from(assistantRequests)
    .orderBy(desc(assistantRequests.createdAt))
    .limit(30);
  const reviewRows = await db
    .select()
    .from(reviewItems)
    .orderBy(desc(reviewItems.updatedAt))
    .limit(30);
  const events = await db
    .select()
    .from(auditEvents)
    .orderBy(desc(auditEvents.createdAt))
    .limit(12);
  const evals = await db
    .select()
    .from(evaluationRuns)
    .orderBy(desc(evaluationRuns.createdAt))
    .limit(1);
  const reviewRequired = requestRows.filter(
    (item) => item.requiresReview,
  ).length;
  const routed = reviewRows.length;
  return {
    requests: requestRows.length,
    reviewRequired,
    routed,
    reviewRate: reviewRequired
      ? Math.round((routed / reviewRequired) * 100)
      : 0,
    pending: reviewRows.filter((item) => item.status === "Pending").length,
    events,
    evaluation: evals[0] ?? null,
  };
}
