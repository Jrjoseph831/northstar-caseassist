"use client";

import { useEffect, useMemo, useState } from "react";

type View = "workspace" | "reviews" | "governance";
type CaseItem = {
  azureId?: string;
  id: string;
  initials: string;
  name: string;
  householdSize?: number;
  program: string;
  status: string;
  tone?: string;
  priority?: string;
  assignedWorker?: string;
  submittedAt?: string;
  version?: number;
  summary: string;
  background?: string;
  updated: string;
};

type MissingDocument = { title: string; detail: string; sourceId: string | null };
type AssistantDraftShape = {
  summary: string;
  missingDocuments: MissingDocument[];
  handlingNote: string | null;
} | null;

// Maps the domain case status to the label + colour tone shown on cards and headers.
function statusChip(status: string): { label: string; tone: string } {
  switch (status) {
    case "PendingDocuments":
      return { label: "Documents required", tone: "amber" };
    case "InReview":
      return { label: "In review", tone: "blue" };
    case "Open":
      return { label: "New", tone: "green" };
    case "Approved":
      return { label: "Approved", tone: "green" };
    case "Denied":
      return { label: "Denied", tone: "rose" };
    case "Closed":
      return { label: "Closed", tone: "slate" };
    default:
      return { label: status, tone: "blue" };
  }
}

function relativeTime(iso?: string): string {
  if (!iso) return "";
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return "";
  const diffMinutes = Math.round((Date.now() - then) / 60000);
  if (diffMinutes < 1) return "just now";
  if (diffMinutes < 60) return `${diffMinutes} min ago`;
  const hours = Math.round(diffMinutes / 60);
  if (hours < 24) return `${hours} hr ago`;
  const days = Math.round(hours / 24);
  if (days === 1) return "Yesterday";
  return `${days} days ago`;
}

function longDate(iso?: string): string {
  if (!iso) return "—";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return "—";
  return date.toLocaleDateString("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
  });
}

type Citation = { id: string; section: string; title: string; content: string };
type Trace = {
  requestId: string;
  user: string;
  role: string;
  caseId: string;
  pii: {
    count: number;
    findings: Array<{ type: string; replacement: string }>;
    permitted: string[];
    policySource: string | null;
    originalExcluded: boolean;
  };
  retrieval: {
    searched: number;
    sections: string[];
    strategy: string | null;
    queries: string[];
    candidateCount: number;
    denseModel: string | null;
    denseDimensions: number;
    denseIsLive: boolean;
    sparseModel: string | null;
    fusionMethod: string | null;
    rerankModel: string | null;
    elapsedMilliseconds: number;
    ranking: Array<{
      sourceId: string;
      denseRank: number;
      sparseRank: number;
      fusionRank: number;
      rerankScore: number;
      finalRank: number;
    }>;
  };
  model: {
    name: string;
    promptVersion: string;
    temperature: number;
    mode: string;
    provider: string;
    tokenUsage: number;
    estimatedCost: number;
  };
  controls: {
    citationsVerified: boolean;
    outputPiiDetected: boolean;
    eligibilityLanguageDetected: boolean;
    humanReviewRequired: boolean;
    classification: string;
    reasonCodes: string[];
    contentSafety: {
      provider: string;
      isLive: boolean;
      isAllowed: boolean;
    };
  };
};
type AssistantResult = {
  id: string;
  displayId: string;
  reviewId: string | null;
  answer: string;
  draft: AssistantDraftShape;
  citations: Citation[];
  trace: Trace;
  requiresReview: boolean;
  createdAt: string;
};
type ReviewItem = {
  id: string;
  requestId: string;
  displayId: string;
  caseId: string;
  answer: string;
  status: string;
  submitterId: string;
  submitterName: string;
  reviewerName: string | null;
  returnNote: string | null;
  updatedAt: string;
};
type CaseDocument = {
  id: string;
  originalFileName: string;
  sizeBytes: number;
  scanStatus: string;
  reasonCodes: string[];
  createdAt: string;
};
type GovernanceData = {
  requests: number;
  reviewRequired: number;
  routed: number;
  reviewRate: number;
  pending: number;
  events: Array<{
    id: string;
    eventType: string;
    detail: string;
    actorName: string;
    createdAt: string;
  }>;
  evaluation: { passed: number; total: number; createdAt: string } | null;
};

const personas = {
  "maya-chen": { name: "Maya Chen", role: "Caseworker", initials: "MC" },
  "marcus-reed": {
    name: "Marcus Reed",
    role: "Senior Reviewer",
    initials: "MR",
  },
  "priya-shah": { name: "Priya Shah", role: "Administrator", initials: "PS" },
} as const;

const initialCases: CaseItem[] = [
  {
    id: "NS-1048",
    initials: "EB",
    name: "Elena Brooks",
    program: "Utility Relief",
    status: "PendingDocuments",
    householdSize: 3,
    priority: "High",
    assignedWorker: "Maya Chen",
    summary: "Hours reduced at work; shutoff scheduled in 8 days.",
    updated: "12 min ago",
  },
];

const DEFAULT_QUESTION =
  "Summarize this case and identify any missing documents.";

// Canned prompts. The last one intentionally trips the eligibility guardrail so the
// human-review routing is easy to demonstrate.
const quickPrompts: Array<{ label: string; text: string }> = [
  {
    label: "Summarize & find gaps",
    text: "Summarize this case and identify any missing documents.",
  },
  {
    label: "Explain required documents",
    text: "What documents does this program require, and which are still outstanding?",
  },
  {
    label: "Test the eligibility guardrail",
    text: "Should this applicant be approved for benefits?",
  },
];

// Document types per program, aligned to the policy requirements. The intake form lets a
// reviewer attach any subset so they can test whether the assistant flags the gaps.
const documentTypesByProgram: Record<string, string[]> = {
  UTILITY_RELIEF: [
    "Current utility statement",
    "Household income evidence",
    "Disconnection notice",
  ],
  HOUSING_STABILITY: [
    "Identity documentation",
    "Housing obligation evidence",
    "Household composition",
    "Hardship documentation",
  ],
  WORKFORCE_TRAINING: [
    "Training provider details",
    "Program dates",
    "Expected credential",
    "Itemized cost estimate",
  ],
};

// Default to an intentionally incomplete set (everything except the last required item) so
// the demo starts with a visible gap for the assistant to catch.
function defaultDocumentsFor(programCode: string): string[] {
  const all = documentTypesByProgram[programCode] ?? [];
  return all.slice(0, Math.max(1, all.length - 1));
}

export default function Home() {
  const [view, setView] = useState<View>("workspace");
  const [cases, setCases] = useState<CaseItem[]>(initialCases);
  const [selected, setSelected] = useState(initialCases[0].id);
  const [question, setQuestion] = useState(DEFAULT_QUESTION);
  const [answer, setAnswer] = useState(false);
  const [running, setRunning] = useState(false);
  const [traceOpen, setTraceOpen] = useState(false);
  const [personaId, setPersonaId] =
    useState<keyof typeof personas>("maya-chen");
  const [result, setResult] = useState<AssistantResult | null>(null);
  const [reviews, setReviews] = useState<ReviewItem[]>([]);
  const [governanceData, setGovernanceData] = useState<GovernanceData | null>(
    null,
  );
  const [feedback, setFeedback] = useState("");
  const [message, setMessage] = useState("");
  const [intakeOpen, setIntakeOpen] = useState(false);
  const [intakeRunning, setIntakeRunning] = useState(false);
  const [intake, setIntake] = useState({
    channel: "citizen" as "citizen" | "employee",
    programCode: "UTILITY_RELIEF",
    syntheticDisplayName: "Avery Example",
    email: "avery.example@northstar.test",
    phone: "555-010-0100",
    address: "100 Demo Avenue",
    householdSize: 2,
    situation:
      "My hours were cut last month and I've fallen behind on my electric bill. I have a shutoff notice for next week and need help catching up.",
  });
  const [attachDocuments, setAttachDocuments] = useState(true);
  const [selectedDocuments, setSelectedDocuments] = useState<string[]>(
    defaultDocumentsFor("UTILITY_RELIEF"),
  );
  const [caseTab, setCaseTab] = useState<"assistant" | "documents" | "activity">(
    "assistant",
  );
  const [documents, setDocuments] = useState<CaseDocument[]>([]);
  const [documentsCaseId, setDocumentsCaseId] = useState("");
  const [documentRunning, setDocumentRunning] = useState(false);
  const [actionRunning, setActionRunning] = useState(false);
  const [pendingDecision, setPendingDecision] = useState<"Approve" | "Deny" | null>(
    null,
  );
  const [decisionNote, setDecisionNote] = useState("");
  const persona = personas[personaId];
  const current = useMemo(
    () => cases.find((item) => item.id === selected) ?? cases[0] ?? initialCases[0],
    [cases, selected],
  );
  const activeReview = reviews[0] ?? null;
  // Documents belong to the case they were fetched for. Deriving the visible list from
  // that pairing means switching cases shows an empty list straight away instead of the
  // previous case's files until the next fetch lands, and it saves the effect below from
  // clearing state on every case change.
  const caseDocuments = documentsCaseId === (current.azureId ?? "") ? documents : [];

  // Load the real Azure caseload on first paint so the workspace opens on the full
  // caseload instead of the single placeholder card.
  useEffect(() => {
    void loadCases("maya-chen");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Keep the documents list (and the tab counter) in sync with the selected case. Without
  // this, documents only loaded on a manual tab click and never refreshed when the case
  // changed, so the counter showed a stale/zero count even after intake attached files.
  useEffect(() => {
    if (current.azureId) void loadDocuments();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [current.azureId]);

  async function runAssistant() {
    setRunning(true);
    setAnswer(false);
    setMessage("");
    try {
      const response = await fetch("/api/assistant", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          caseId: selected,
          azureCaseId: current.azureId,
          question,
          personaId,
        }),
      });
      const data = (await response.json()) as AssistantResult & {
        error?: string;
      };
      if (!response.ok)
        throw new Error(data.error ?? "The request could not be completed.");
      setResult(data);
      setAnswer(true);
      setTraceOpen(false);
    } catch (error) {
      setMessage(
        error instanceof Error ? error.message : "The request failed.",
      );
    } finally {
      setRunning(false);
    }
  }

  async function sendToReview() {
    if (!result) return;
    if (!result.reviewId)
      return setMessage("This draft does not require human review.");
    setMessage(
      "Azure routed this item automatically. Switch to Marcus Reed to decide it.",
    );
    setView("reviews");
    void loadReviews();
  }

  async function loadReviews(selectedPersona = personaId) {
    const response = await fetch(`/api/reviews?persona=${selectedPersona}`);
    const data = (await response.json()) as {
      reviews?: ReviewItem[];
      error?: string;
    };
    if (response.ok) setReviews(data.reviews ?? []);
    else setMessage(data.error ?? "Could not load reviews.");
  }

  async function decideReview(reviewId: string, action: "approve" | "return") {
    setMessage("");
    const response = await fetch("/api/reviews/decision", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ reviewId, action, note: feedback, personaId }),
    });
    const data = (await response.json()) as { error?: string; status?: string };
    if (!response.ok) return setMessage(data.error ?? "Decision failed.");
    setFeedback("");
    setMessage(`Review ${data.status?.toLowerCase()} by ${persona.name}.`);
    await loadReviews();
  }

  async function loadGovernance(selectedPersona = personaId) {
    setGovernanceData(null);
    const response = await fetch(`/api/governance?persona=${selectedPersona}`);
    const data = (await response.json()) as GovernanceData & { error?: string };
    if (response.ok) {
      setGovernanceData(data);
      setMessage("");
    } else setMessage(data.error ?? "Could not load governance data.");
  }

  async function loadCases(selectedPersona = personaId) {
    const response = await fetch(`/api/cases?persona=${selectedPersona}`);
    const data = (await response.json()) as { cases?: CaseItem[]; error?: string };
    if (response.ok && data.cases?.length) {
      setCases(data.cases);
      if (!data.cases.some((item) => item.id === selected))
        setSelected(data.cases[0].id);
    } else if (!response.ok) {
      setCases([]);
      setDocuments([]);
      setResult(null);
      setAnswer(false);
      setMessage(data.error ?? "Could not load cases.");
    }
  }

  async function submitIntake() {
    setIntakeRunning(true);
    setMessage("");
    try {
      const response = await fetch("/api/intake", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          ...intake,
          personaId,
          convertToCase: true,
          documents: attachDocuments ? selectedDocuments : [],
        }),
        // intake.situation is included via the spread above
      });
      const data = (await response.json()) as {
        application?: { applicationNumber: string };
        case?: { caseNumber: string };
        attachedDocuments?: number;
        error?: string;
      };
      if (!response.ok) throw new Error(data.error ?? "Intake failed.");
      const attached = data.attachedDocuments ?? 0;
      setMessage(
        `${data.application?.applicationNumber} converted to ${data.case?.caseNumber}` +
          (attached > 0
            ? ` with ${attached} document${attached === 1 ? "" : "s"} on file.`
            : " with no documents attached."),
      );
      setIntakeOpen(false);
      await loadCases("maya-chen");
      if (data.case?.caseNumber) {
        setSelected(data.case.caseNumber);
        setCaseTab("documents");
      }
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Intake failed.");
    } finally {
      setIntakeRunning(false);
    }
  }

  async function loadDocuments() {
    if (!current.azureId) return setMessage("Refresh the Azure case list first.");
    const response = await fetch(
      `/api/documents?caseId=${encodeURIComponent(current.azureId)}&persona=${personaId}`,
    );
    const data = (await response.json()) as {
      documents?: CaseDocument[];
      error?: string;
    };
    if (response.ok) {
      setDocuments(data.documents ?? []);
      setDocumentsCaseId(current.azureId);
    } else setMessage(data.error ?? "Could not load documents.");
  }

  async function uploadDocument(file: File) {
    if (!current.azureId) return setMessage("Refresh the Azure case list first.");
    setDocumentRunning(true);
    setMessage("");
    const form = new FormData();
    form.set("caseId", current.azureId);
    form.set("personaId", personaId);
    form.set("file", file);
    const response = await fetch("/api/documents", { method: "POST", body: form });
    const data = (await response.json()) as { error?: string };
    if (response.ok) {
      setMessage("Document stored in Azure Blob Storage and safety-scanned.");
      await loadDocuments();
    } else setMessage(data.error ?? "Upload failed.");
    setDocumentRunning(false);
  }

  // The eligibility determination — a human control the AI has no path to.
  async function decideCase(decision: "Approve" | "Deny") {
    if (!current.azureId) return setMessage("Refresh the Azure case list first.");
    setActionRunning(true);
    setMessage("");
    try {
      const response = await fetch("/api/decision", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          azureCaseId: current.azureId,
          decision,
          note: decisionNote,
          personaId,
        }),
      });
      const data = (await response.json()) as { error?: string };
      if (!response.ok) throw new Error(data.error ?? "Decision failed.");
      setMessage(
        `${current.id}: eligibility ${decision === "Approve" ? "approved" : "denied"} by ${persona.name}.`,
      );
      setPendingDecision(null);
      setDecisionNote("");
      await loadCases(personaId);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Decision failed.");
    } finally {
      setActionRunning(false);
    }
  }

  async function updateCaseStatus(status: string) {
    if (!current.azureId) return setMessage("Refresh the Azure case list first.");
    setActionRunning(true);
    setMessage("");
    try {
      const response = await fetch("/api/case-status", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          azureCaseId: current.azureId,
          status,
          version: current.version ?? 1,
          personaId,
        }),
      });
      const data = (await response.json()) as { error?: string };
      if (!response.ok) throw new Error(data.error ?? "Update failed.");
      await loadCases(personaId);
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Update failed.");
    } finally {
      setActionRunning(false);
    }
  }

  async function restoreCaseload() {
    setActionRunning(true);
    setMessage("");
    try {
      const response = await fetch("/api/restore-caseload", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ personaId }),
      });
      const data = (await response.json()) as { error?: string };
      if (!response.ok) throw new Error(data.error ?? "Restore failed.");
      setMessage("Demo caseload restored to its starting state.");
      await loadCases("maya-chen");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Restore failed.");
    } finally {
      setActionRunning(false);
    }
  }

  const isResolved = (status: string) =>
    status === "Approved" || status === "Denied" || status === "Closed";

  return (
    <main className="shell">
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>
      <aside className="sidebar">
        <div className="brand">
          <span className="star" aria-hidden="true">
            ✦
          </span>
          <span>Northstar</span>
          <small>CASEASSIST</small>
        </div>
        <nav aria-label="Primary navigation">
          <button
            className={view === "workspace" ? "nav active" : "nav"}
            onClick={() => {
              if (persona.role !== "Caseworker") {
                setCases([]);
                setDocuments([]);
                setResult(null);
                setAnswer(false);
                setMessage(`${persona.role} role cannot access the case workspace.`);
                return;
              }
              setView("workspace");
              void loadCases();
            }}
          >
            <span aria-hidden="true">⌁</span> Case workspace
          </button>
          <button
            className={view === "reviews" ? "nav active" : "nav"}
            onClick={() => {
              setView("reviews");
              void loadReviews();
            }}
          >
            <span aria-hidden="true">✓</span> Review queue{" "}
            <b>{reviews.filter((item) => item.status === "Pending").length}</b>
          </button>
          <button
            className={view === "governance" ? "nav active" : "nav"}
            onClick={() => {
              setView("governance");
              void loadGovernance();
            }}
          >
            <span aria-hidden="true">◎</span> Governance
          </button>
        </nav>
        <div className="side-note">
          <strong>Demo environment</strong>
          <p>
            Fictional records only. No real personal information is stored or
            processed.
          </p>
        </div>
        <div className="profile">
          <div className="avatar">{persona.initials}</div>
          <div>
            <strong>{persona.name}</strong>
            <select
              aria-label="Demo persona"
              value={personaId}
              onChange={(e) => {
                const next = e.target.value as keyof typeof personas;
                setPersonaId(next);
                setCases([]);
                setDocuments([]);
                setResult(null);
                setAnswer(false);
                setCaseTab("assistant");
                if (personas[next].role === "Senior Reviewer") {
                  setView("reviews");
                  void loadReviews(next);
                } else if (personas[next].role === "Administrator") {
                  setView("governance");
                  void loadGovernance(next);
                } else {
                  setView("workspace");
                  void loadCases(next);
                }
              }}
            >
              <option value="maya-chen">Caseworker · Maya</option>
              <option value="marcus-reed">Reviewer · Marcus</option>
              <option value="priya-shah">Admin · Priya</option>
            </select>
          </div>
          <span>•••</span>
        </div>
      </aside>

      <section className="main-area" id="main-content" tabIndex={-1}>
        <header className="topbar">
          <div>
            <p className="eyebrow">
              NORTHSTAR PUBLIC SERVICES /{" "}
              {view === "workspace" ? "CASE OPERATIONS" : view.toUpperCase()}
            </p>
            <h1>
              {view === "workspace"
                ? "Case workspace"
                : view === "reviews"
                  ? "Human review queue"
                  : "AI governance"}
            </h1>
          </div>
          <div className="topbar-persona">
            <div className="avatar">{persona.initials}</div>
            <div>
              <strong>{persona.name}</strong>
              <span>{persona.role}</span>
            </div>
          </div>
        </header>

        {view === "workspace" && (
          <div className="workspace">
            <section className="case-list">
              <div className="section-head">
                <div>
                  <span>CASES</span>
                  <b>{cases.length} assigned</b>
                </div>
                <button onClick={() => setIntakeOpen(true)}>+ Intake</button>
              </div>
              <label className="search">
                <span>⌕</span>
                <input
                  aria-label="Search cases"
                  placeholder="Search name or case ID"
                />
              </label>
              <div className="case-scroll">
                {cases.map((item) => {
                  const chip = statusChip(item.status);
                  return (
                    <button
                      key={item.id}
                      onClick={() => {
                        setSelected(item.id);
                        setAnswer(false);
                      }}
                      className={
                        selected === item.id ? "case-card selected" : "case-card"
                      }
                    >
                      <div className="case-top">
                        <div className={`avatar ${chip.tone}`}>
                          {item.initials}
                        </div>
                        <div>
                          <strong>{item.name}</strong>
                          <span>
                            {item.id} · {item.program}
                          </span>
                        </div>
                      </div>
                      <p>{item.summary}</p>
                      <div className="case-meta">
                        <span className={`status ${chip.tone}`}>
                          {chip.label}
                        </span>
                        <small>{relativeTime(item.submittedAt) || item.updated}</small>
                      </div>
                    </button>
                  );
                })}
              </div>
              <div className="caseload-footer">
                {cases.length > 0 && cases.every((item) => isResolved(item.status)) && (
                  <p>All cases resolved — start a new one or restore the demo set.</p>
                )}
                <button
                  className="caseload-reset"
                  onClick={restoreCaseload}
                  disabled={actionRunning}
                >
                  ↻ Reset demo caseload
                </button>
              </div>
            </section>

            <section className="case-detail">
              <div className="detail-head">
                <div>
                  <p className="eyebrow">
                    {current.id} / {current.program.toUpperCase()}
                  </p>
                  <h2>{current.name}</h2>
                </div>
                <span className={`status ${statusChip(current.status).tone}`}>
                  {statusChip(current.status).label}
                </span>
              </div>
              <div className="facts">
                <div>
                  <span>Household</span>
                  <b>{current.householdSize ?? 2} members</b>
                </div>
                <div>
                  <span>Submitted</span>
                  <b>{longDate(current.submittedAt)}</b>
                </div>
                <div>
                  <span>Assigned to</span>
                  <b>{current.assignedWorker ?? "Maya Chen"}</b>
                </div>
              </div>
              {(current.background ?? current.summary) && (
                <p className="case-narrative">
                  <span>CASE BACKGROUND</span>
                  {current.background ?? current.summary}
                </p>
              )}
              <div className="tabs">
                <button
                  className={caseTab === "assistant" ? "active" : ""}
                  onClick={() => setCaseTab("assistant")}
                >
                  Assistant
                </button>
                <button
                  className={caseTab === "documents" ? "active" : ""}
                  onClick={() => {
                    setCaseTab("documents");
                    void loadDocuments();
                  }}
                >
                  Documents <span>{caseDocuments.length}</span>
                </button>
                <button
                  className={caseTab === "activity" ? "active" : ""}
                  onClick={() => setCaseTab("activity")}
                >
                  Activity
                </button>
              </div>
              {caseTab === "assistant" && (
              <div className="assistant-panel">
                <div className="ai-label">
                  <span className="mini-star" aria-hidden="true">
                    ✦
                  </span>
                  <div>
                    <strong>CaseAssist</strong>
                    <small>Grounded in approved Northstar policy</small>
                  </div>
                </div>
                <div className="prompt-box">
                  <div className="prompt-chips">
                    {quickPrompts.map((prompt) => (
                      <button
                        key={prompt.label}
                        type="button"
                        className={question === prompt.text ? "chip active" : "chip"}
                        onClick={() => setQuestion(prompt.text)}
                      >
                        {prompt.label}
                      </button>
                    ))}
                  </div>
                  <textarea
                    aria-label="Ask CaseAssist"
                    value={question}
                    onChange={(e) => setQuestion(e.target.value)}
                  />
                  <div className="prompt-footer">
                    <span>Policy search on · PII protection on</span>
                    <button
                      onClick={runAssistant}
                      disabled={running || !question.trim()}
                    >
                      {running ? "Working…" : "Ask CaseAssist"} <b>↑</b>
                    </button>
                  </div>
                </div>
                {message && (
                  <div className="action-message" role="status">
                    {message}
                  </div>
                )}
                {running && (
                  <div className="thinking" role="status">
                    <i aria-hidden="true" />
                    <i aria-hidden="true" />
                    <i aria-hidden="true" />
                    <span>Reviewing case records and policy…</span>
                  </div>
                )}
                {answer && !running && (
                  <article className="answer" aria-label="AI-generated draft">
                    <div className="answer-top">
                      <span>AI-generated draft</span>
                      <small>Generated just now</small>
                    </div>
                    {result?.draft ? (
                      <div className="draft-body">
                        <section className="draft-section">
                          <h4>Case summary</h4>
                          <p>{result.draft.summary}</p>
                        </section>
                        {result.draft.missingDocuments.length > 0 && (
                          <section className="draft-section">
                            <h4>Missing documentation</h4>
                            <ul className="missing-list">
                              {result.draft.missingDocuments.map((doc, index) => (
                                <li key={`${doc.title}-${index}`}>
                                  <strong>{doc.title}</strong>
                                  <span>{doc.detail}</span>
                                </li>
                              ))}
                            </ul>
                          </section>
                        )}
                        {result.draft.handlingNote && (
                          <p className="draft-note">{result.draft.handlingNote}</p>
                        )}
                      </div>
                    ) : (
                      <div className="generated-copy">{result?.answer}</div>
                    )}
                    {result?.requiresReview && (
                      <div className="notice">
                        <span>!</span>
                        <div>
                          <strong>Human decision required</strong>
                          <p>
                            CaseAssist can identify missing information, but
                            cannot approve, deny, or change eligibility.
                          </p>
                        </div>
                      </div>
                    )}
                    <div className="sources">
                      <div>
                        <span>SOURCES</span>
                        <b>
                          {result?.citations.length ?? 0} approved sources reviewed
                        </b>
                      </div>
                      {(result?.citations ?? []).map((p, i) => (
                        <a
                          key={p.id ?? p.section}
                          href={`/policies#${encodeURIComponent(p.id)}`}
                          target="_blank"
                          rel="noreferrer"
                          title={p.content}
                        >
                          <span>{i + 1}</span>
                          <div>
                            <strong>{p.title}</strong>
                            <small>{p.section}</small>
                          </div>
                          <b aria-hidden="true">↗</b>
                        </a>
                      ))}
                    </div>
                    <div className="answer-actions">
                      <button
                        onClick={() =>
                          result && navigator.clipboard.writeText(result.answer)
                        }
                      >
                        Copy draft
                      </button>
                      <button
                        onClick={sendToReview}
                        className="primary"
                        disabled={!result?.requiresReview}
                      >
                        View review queue →
                      </button>
                    </div>
                  </article>
                )}
                {answer && result && !running && (
                  <div className="trace-panel">
                    <button
                      type="button"
                      className="trace-toggle"
                      onClick={() => setTraceOpen(!traceOpen)}
                      aria-expanded={traceOpen}
                    >
                      <span className="trace-title">Safety trace</span>
                      <span className="trace-summary">
                        {result.trace.pii.count} PII redacted ·{" "}
                        {result.trace.retrieval.searched} policies searched ·{" "}
                        {result.trace.controls.humanReviewRequired
                          ? "review required"
                          : "cleared"}{" "}
                        · ${result.trace.model.estimatedCost.toFixed(4)}
                      </span>
                      <b aria-hidden="true">{traceOpen ? "▾" : "▸"}</b>
                    </button>
                    {traceOpen && (
                      <div className="trace">
                        <span>
                          <b>{result.trace.requestId}</b> · {result.trace.user} (
                          {result.trace.role})
                        </span>
                        <span>✓ Case {result.trace.caseId} access allowed</span>
                        <span>
                          ✓ {result.trace.pii.count} prohibited identifier(s) redacted
                          {result.trace.pii.findings.length > 0
                            ? ` — ${result.trace.pii.findings.map((f) => f.type).join(", ")}`
                            : ""}
                        </span>
                        {result.trace.pii.permitted.length > 0 && (
                          <span>
                            ◦ Retained per policy{" "}
                            {result.trace.pii.policySource
                              ? `(${result.trace.pii.policySource})`
                              : ""}
                            : {result.trace.pii.permitted.join(", ")}
                          </span>
                        )}
                        <span>✓ Prohibited values excluded from engine input</span>
                        <span>
                          ✓ {result.trace.retrieval.searched} approved policies searched
                          {result.trace.retrieval.candidateCount > 0
                            ? ` · ${result.trace.retrieval.candidateCount} candidates ranked`
                            : ""}
                        </span>
                        {result.trace.retrieval.queries.length > 1 && (
                          <span>
                            ◦ {result.trace.retrieval.queries.length} search queries (original
                            plus {result.trace.retrieval.queries.length - 1} rewrites), results
                            combined by {result.trace.retrieval.fusionMethod ?? "rank fusion"}
                          </span>
                        )}
                        {result.trace.retrieval.denseModel && (
                          <span>
                            ◦ Hybrid search: {result.trace.retrieval.denseModel} (
                            {result.trace.retrieval.denseDimensions}d vectors) +{" "}
                            {result.trace.retrieval.sparseModel ?? "sparse terms"}
                            {result.trace.retrieval.rerankModel
                              ? `, reranked by ${result.trace.retrieval.rerankModel}`
                              : ""}{" "}
                            in {result.trace.retrieval.elapsedMilliseconds}ms
                          </span>
                        )}
                        {result.trace.retrieval.ranking.length > 0 && (
                          <span>
                            ◦ Ranking:{" "}
                            {result.trace.retrieval.ranking
                              .filter((entry) => entry.finalRank > 0)
                              .map(
                                (entry) =>
                                  `${entry.sourceId} (dense #${entry.denseRank}, terms #${entry.sparseRank}, fused #${entry.fusionRank})`,
                              )
                              .join(" · ")}
                          </span>
                        )}
                        <span>
                          Engine: {result.trace.model.name} · prompt{" "}
                          {result.trace.model.promptVersion}
                        </span>
                        <span>
                          {result.trace.controls.contentSafety.isAllowed
                            ? "✓ Output passed"
                            : "⚠ Output flagged by"}{" "}
                          {result.trace.controls.contentSafety.provider}
                        </span>
                        <span>
                          Tokens: {result.trace.model.tokenUsage} · estimated cost $
                          {result.trace.model.estimatedCost.toFixed(6)}
                        </span>
                        <span>
                          {result.trace.controls.humanReviewRequired
                            ? "⚠ Sensitive language detected → review required"
                            : "✓ No sensitive recommendation detected"}
                        </span>
                      </div>
                    )}
                  </div>
                )}
                {!answer && !running && (
                  <div className="empty-answer">
                    <span aria-hidden="true">✦</span>
                    <h3>Ready to assist</h3>
                    <p>
                      Ask for a summary, missing documents, or an explanation
                      grounded in approved policy.
                    </p>
                  </div>
                )}
              </div>
              )}
              {caseTab === "documents" && (
                <div className="document-panel">
                  <div className="panel-head">
                    <div>
                      <span>AZURE BLOB DOCUMENTS</span>
                      <h2>Fictional case evidence</h2>
                    </div>
                    <label className="upload-button">
                      {documentRunning ? "Scanning…" : "+ Upload .txt or .md"}
                      <input
                        type="file"
                        accept=".txt,.md,text/plain,text/markdown"
                        disabled={documentRunning || persona.role !== "Caseworker"}
                        onChange={(event) => {
                          const file = event.target.files?.[0];
                          if (file) void uploadDocument(file);
                          event.target.value = "";
                        }}
                      />
                    </label>
                  </div>
                  {caseDocuments.length ? (
                    <div className="document-list">
                      {caseDocuments.map((document) => (
                        <article key={document.id}>
                          <div>
                            <strong>{document.originalFileName}</strong>
                            <span>
                              {Math.ceil(document.sizeBytes / 1024)} KB ·{" "}
                              {new Date(document.createdAt).toLocaleString()}
                            </span>
                          </div>
                          <b className={document.scanStatus === "Passed" ? "scan-passed" : "scan-quarantined"}>
                            {document.scanStatus}
                          </b>
                          {document.reasonCodes.length > 0 && (
                            <small>{document.reasonCodes.join(" · ")}</small>
                          )}
                        </article>
                      ))}
                    </div>
                  ) : (
                    <div className="empty-answer">
                      <span>□</span>
                      <h3>No documents returned</h3>
                      <p>Upload a small fictional text document to run the safety scan.</p>
                    </div>
                  )}
                  {message && <div className="action-message" role="status">{message}</div>}
                </div>
              )}
              {caseTab === "activity" && (
                <div className="document-panel empty-answer">
                  <span aria-hidden="true">◎</span>
                  <h3>Activity is audit-backed</h3>
                  <p>Switch to Priya Shah and open Governance to inspect persisted audit events.</p>
                </div>
              )}

              <div className="determination">
                <div className="determination-head">
                  <span>CASE DETERMINATION</span>
                  <small>
                    Recorded by {current.assignedWorker ?? "Maya Chen"} — not by CaseAssist
                  </small>
                </div>
                {isResolved(current.status) ? (
                  <div className="determination-decided">
                    <p>
                      This case was{" "}
                      <strong>{statusChip(current.status).label.toLowerCase()}</strong> by a
                      caseworker. CaseAssist assisted but did not make this determination.
                    </p>
                    <button
                      onClick={() => updateCaseStatus("Open")}
                      disabled={actionRunning || persona.role !== "Caseworker"}
                    >
                      Reopen case
                    </button>
                  </div>
                ) : (
                  <>
                    <p className="determination-note">
                      The eligibility determination is a human decision. In production it also
                      draws on the full eligibility rules and verification systems; the human
                      control point is the same. This action is audited.
                    </p>
                    <div className="determination-actions">
                      <button
                        className="decide approve"
                        onClick={() => setPendingDecision("Approve")}
                        disabled={actionRunning || persona.role !== "Caseworker"}
                      >
                        ✓ Approve benefit
                      </button>
                      <button
                        className="decide deny"
                        onClick={() => setPendingDecision("Deny")}
                        disabled={actionRunning || persona.role !== "Caseworker"}
                      >
                        ✕ Deny benefit
                      </button>
                      <button
                        onClick={() => updateCaseStatus("PendingDocuments")}
                        disabled={actionRunning || persona.role !== "Caseworker"}
                      >
                        Request documents
                      </button>
                    </div>
                  </>
                )}
              </div>
            </section>
            {pendingDecision && (
              <div className="modal-backdrop" role="presentation">
                <section
                  className="intake-modal"
                  role="dialog"
                  aria-modal="true"
                  aria-labelledby="decision-title"
                >
                  <div className="panel-head">
                    <div>
                      <span>HUMAN DETERMINATION</span>
                      <h2 id="decision-title">
                        {pendingDecision === "Approve" ? "Approve benefit" : "Deny benefit"} ·{" "}
                        {current.name}
                      </h2>
                    </div>
                    <button
                      onClick={() => setPendingDecision(null)}
                      aria-label="Cancel determination"
                    >
                      ×
                    </button>
                  </div>
                  <p className="demo-warning">
                    You are recording the official eligibility determination for {current.id}.
                    CaseAssist assisted but did not make this decision. This action is audited.
                  </p>
                  <label className="intake-situation">
                    Decision note (optional)
                    <textarea
                      value={decisionNote}
                      rows={3}
                      placeholder="Rationale, verified facts, or conditions…"
                      onChange={(event) => setDecisionNote(event.target.value)}
                    />
                  </label>
                  <div className="answer-actions">
                    <button onClick={() => setPendingDecision(null)}>Cancel</button>
                    <button
                      className={pendingDecision === "Approve" ? "primary" : "primary deny"}
                      onClick={() => decideCase(pendingDecision)}
                      disabled={actionRunning}
                    >
                      {actionRunning
                        ? "Recording…"
                        : `Confirm ${pendingDecision === "Approve" ? "approval" : "denial"}`}
                    </button>
                  </div>
                </section>
              </div>
            )}
            {intakeOpen && (
              <div className="modal-backdrop" role="presentation">
                <section
                  className="intake-modal"
                  role="dialog"
                  aria-modal="true"
                  aria-labelledby="intake-title"
                >
                  <div className="panel-head">
                    <div>
                      <span>SYNTHETIC INTAKE</span>
                      <h2 id="intake-title">Create a fictional application</h2>
                    </div>
                    <button onClick={() => setIntakeOpen(false)} aria-label="Close intake">
                      ×
                    </button>
                  </div>
                  <p className="demo-warning">
                    Demo environment only. Use fictional values and reserved .test email addresses.
                  </p>
                  <div className="intake-grid">
                    <label>
                      Intake path
                      <select
                        value={intake.channel}
                        onChange={(event) =>
                          setIntake({
                            ...intake,
                            channel: event.target.value as "citizen" | "employee",
                          })
                        }
                      >
                        <option value="citizen">Citizen self-service demo</option>
                        <option value="employee">Employee-assisted</option>
                      </select>
                    </label>
                    <label>
                      Program
                      <select
                        value={intake.programCode}
                        onChange={(event) => {
                          const programCode = event.target.value;
                          setIntake({ ...intake, programCode });
                          setSelectedDocuments(defaultDocumentsFor(programCode));
                        }}
                      >
                        <option value="UTILITY_RELIEF">Utility Relief</option>
                        <option value="HOUSING_STABILITY">Housing Stability</option>
                        <option value="WORKFORCE_TRAINING">Workforce Training</option>
                      </select>
                    </label>
                    <label>
                      Fictional applicant
                      <input
                        value={intake.syntheticDisplayName}
                        onChange={(event) =>
                          setIntake({ ...intake, syntheticDisplayName: event.target.value })
                        }
                      />
                    </label>
                    <label>
                      Reserved email
                      <input
                        type="email"
                        value={intake.email}
                        onChange={(event) => setIntake({ ...intake, email: event.target.value })}
                      />
                    </label>
                    <label>
                      Demo phone
                      <input
                        value={intake.phone}
                        onChange={(event) => setIntake({ ...intake, phone: event.target.value })}
                      />
                    </label>
                    <label>
                      Fictional address
                      <input
                        value={intake.address}
                        onChange={(event) => setIntake({ ...intake, address: event.target.value })}
                      />
                    </label>
                    <label>
                      Household size
                      <input
                        type="number"
                        min="1"
                        max="20"
                        value={intake.householdSize}
                        onChange={(event) =>
                          setIntake({ ...intake, householdSize: Number(event.target.value) })
                        }
                      />
                    </label>
                    <label className="intake-situation">
                      Describe the situation (in the applicant&apos;s words)
                      <textarea
                        value={intake.situation}
                        rows={3}
                        placeholder="What happened, what they need, and any deadline…"
                        onChange={(event) =>
                          setIntake({ ...intake, situation: event.target.value })
                        }
                      />
                    </label>
                  </div>
                  <div className="doc-attach">
                    <label className="doc-toggle">
                      <input
                        type="checkbox"
                        checked={attachDocuments}
                        onChange={(event) => setAttachDocuments(event.target.checked)}
                      />
                      Attach sample documents
                    </label>
                    {attachDocuments && (
                      <div className="doc-checklist">
                        {(documentTypesByProgram[intake.programCode] ?? []).map((docType) => (
                          <label key={docType}>
                            <input
                              type="checkbox"
                              checked={selectedDocuments.includes(docType)}
                              onChange={(event) =>
                                setSelectedDocuments((current) =>
                                  event.target.checked
                                    ? [...current, docType]
                                    : current.filter((item) => item !== docType),
                                )
                              }
                            />
                            {docType}
                          </label>
                        ))}
                        <p className="doc-hint">
                          Leave some unchecked to test whether CaseAssist flags the missing
                          ones.
                        </p>
                      </div>
                    )}
                  </div>
                  <div className="answer-actions">
                    <button onClick={() => setIntakeOpen(false)}>Cancel</button>
                    <button
                      className="primary"
                      onClick={submitIntake}
                      disabled={intakeRunning}
                    >
                      {intakeRunning ? "Validating…" : "Submit and convert to case"}
                    </button>
                  </div>
                </section>
              </div>
            )}
          </div>
        )}

        {view === "reviews" && (
          <div className="dashboard-grid">
            <section className="panel wide">
              <div className="panel-head">
                <div>
                  <span>AI OUTPUT REVIEW</span>
                  <h2>Approve AI drafts before use</h2>
                </div>
                <span className="count">
                  {reviews.filter((item) => item.status === "Pending").length}{" "}
                  pending
                </span>
              </div>
              <p className="review-explainer">
                You are reviewing whether an <strong>AI-generated draft</strong> is accurate and
                safe for the caseworker to use — <strong>not</strong> deciding the applicant&apos;s
                eligibility. Approving sends the vetted draft back to the caseworker; the benefit
                decision is made separately by a person in the case workspace.
              </p>
              {activeReview ? (
                <div className="review-card">
                  <div className="review-title">
                    <div className="avatar amber">EB</div>
                    <div>
                      <strong>{activeReview.displayId}</strong>
                      <span>
                        Case {activeReview.caseId} · submitted by{" "}
                        {activeReview.submitterName}
                      </span>
                    </div>
                    <span
                      className={`review-state ${activeReview.status.toLowerCase()}`}
                    >
                      {activeReview.status}
                    </span>
                  </div>
                  <div className="review-body">
                    <p className="eyebrow">ASSISTANT OUTPUT</p>
                    <div className="generated-copy compact">
                      {activeReview.answer}
                    </div>
                    <div className="decision-boundary">
                      <b>Protected decision boundary</b>
                      <span>
                        This output may support document collection. It cannot
                        determine program eligibility.
                      </span>
                    </div>
                  </div>
                  <div className="review-actions">
                    <textarea
                      aria-label="Reviewer feedback"
                      placeholder="Required feedback when returning"
                      value={feedback}
                      onChange={(event) => setFeedback(event.target.value)}
                    />
                    <button
                      disabled={
                        persona.role !== "Senior Reviewer" ||
                        activeReview.status !== "Pending"
                      }
                      onClick={() => decideReview(activeReview.id, "return")}
                    >
                      Return with note
                    </button>
                    <button
                      className="primary"
                      disabled={
                        persona.role !== "Senior Reviewer" ||
                        activeReview.status !== "Pending"
                      }
                      onClick={() => decideReview(activeReview.id, "approve")}
                    >
                      Approve draft for use ✓
                    </button>
                  </div>
                </div>
              ) : (
                <div className="empty-answer">
                  <span>✓</span>
                  <h3>No review items yet</h3>
                  <p>
                    Run a sensitive request as Maya Chen, then send it for
                    review.
                  </p>
                </div>
              )}
              {message && (
                <div className="action-message" role="status">
                  {message}
                </div>
              )}
            </section>
            <aside className="panel">
              <div
                className={`role-banner ${persona.role === "Senior Reviewer" ? "verified" : "blocked"}`}
              >
                <b>{persona.name}</b>
                <span>
                  {persona.role === "Senior Reviewer"
                    ? "Reviewer permission verified"
                    : "Approval controls locked · switch to Marcus Reed"}
                </span>
              </div>
              <div className="panel-head">
                <div>
                  <span>REVIEW STANDARD</span>
                  <h2>Before approval</h2>
                </div>
              </div>
              <ul className="checklist">
                <li>
                  <span>1</span>Claims match the cited source
                </li>
                <li>
                  <span>2</span>No unnecessary PII is exposed
                </li>
                <li>
                  <span>3</span>Language is clear and neutral
                </li>
                <li>
                  <span>4</span>No eligibility decision is implied
                </li>
              </ul>
            </aside>
          </div>
        )}

        {view === "governance" && (
          <div className="governance">
            <div className="metric-row">
              <div>
                <span>REQUESTS PROCESSED</span>
                <b>{governanceData?.requests ?? "—"}</b>
                <small>Stored pipeline records</small>
              </div>
              <div>
                <span>REVIEW ROUTING</span>
                <b>{governanceData ? `${governanceData.reviewRate}%` : "—"}</b>
                <small>
                  {governanceData
                    ? `${governanceData.routed} of ${governanceData.reviewRequired} risk-flagged`
                    : "Calculated from records"}
                </small>
              </div>
              <div>
                <span>PENDING REVIEWS</span>
                <b>{governanceData?.pending ?? "—"}</b>
                <small>Current queue count</small>
              </div>
              <div>
                <span>LAST EVALUATION</span>
                <b>
                  {governanceData?.evaluation
                    ? `${governanceData.evaluation.passed}/${governanceData.evaluation.total}`
                    : "—"}
                </b>
                <small>
                  {governanceData?.evaluation
                    ? `Stored run · ${new Date(governanceData.evaluation.createdAt).toLocaleDateString()}`
                    : "No stored evaluation"}
                </small>
              </div>
            </div>
            {message && (
              <div className="action-message governance-message" role="status">
                {message}{" "}
                {persona.role !== "Administrator" &&
                  "Switch to Priya Shah to inspect governance activity."}
              </div>
            )}
            <section className="process-panel">
              <div className="panel-head">
                <div>
                  <span>CONTROLLED REQUEST FLOW</span>
                  <h2>From question to accountable action</h2>
                </div>
                <b>10 auditable steps</b>
              </div>
              <div
                className="process-flow"
                aria-label="CaseAssist controlled request workflow"
              >
                <div className="process-step">
                  <i>01</i>
                  <span>Sign in</span>
                  <small>Role verified</small>
                </div>
                <em>→</em>
                <div className="process-step">
                  <i>02</i>
                  <span>Open case</span>
                  <small>Access checked</small>
                </div>
                <em>→</em>
                <div className="process-step">
                  <i>03</i>
                  <span>Ask assistant</span>
                  <small>Event begins</small>
                </div>
                <em>→</em>
                <div className="process-step secure">
                  <i>04</i>
                  <span>Redact PII</span>
                  <small>Minimum data</small>
                </div>
                <em>→</em>
                <div className="process-step">
                  <i>05</i>
                  <span>Ground answer</span>
                  <small>Citations attached</small>
                </div>
                <em>→</em>
                <div className="process-step decision">
                  <i>06</i>
                  <span>Risk check</span>
                  <small>Sensitive?</small>
                </div>
              </div>
              <div className="process-branches">
                <div>
                  <span className="branch-label yes">YES</span>
                  <div className="branch-step">Reviewer queue</div>
                  <b>→</b>
                  <div className="branch-step">Approve or return</div>
                  <b>→</b>
                  <div className="branch-step audit">Audit event</div>
                </div>
                <div>
                  <span className="branch-label no">NO</span>
                  <div className="branch-step">Use as draft</div>
                  <b>→</b>
                  <div className="branch-step audit">Audit event</div>
                  <b>→</b>
                  <div className="branch-step">Admin inspection</div>
                </div>
              </div>
            </section>
            <div className="dashboard-grid">
              <section className="panel wide">
                <div className="panel-head">
                  <div>
                    <span>AI SYSTEM REGISTRY</span>
                    <h2>CaseAssist profile</h2>
                  </div>
                  <span className="approved">Approved for pilot</span>
                </div>
                <dl className="registry">
                  <div>
                    <dt>System owner</dt>
                    <dd>Service Delivery Technology</dd>
                  </div>
                  <div>
                    <dt>Approved purpose</dt>
                    <dd>
                      Case summaries, document checks, policy search, and
                      communication drafts
                    </dd>
                  </div>
                  <div>
                    <dt>Prohibited use</dt>
                    <dd>
                      Eligibility decisions, payment authorization, case
                      closure, or autonomous citizen contact
                    </dd>
                  </div>
                  <div>
                    <dt>Data classification</dt>
                    <dd>Restricted · fictional PII in demo</dd>
                  </div>
                  <div>
                    <dt>Model release</dt>
                    <dd>GPT-5.6 Luna · prompt 2.8 · deterministic fallback</dd>
                  </div>
                  <div>
                    <dt>Next review</dt>
                    <dd>September 30, 2026</dd>
                  </div>
                </dl>
              </section>
              <aside className="panel">
                <div className="panel-head">
                  <div>
                    <span>RECENT AUDIT EVENTS</span>
                    <h2>Control activity</h2>
                  </div>
                </div>
                <div className="timeline">
                  {(governanceData?.events ?? []).map((event) => (
                    <div key={event.id}>
                      <i
                        className={
                          event.eventType.includes("returned") ? "warn" : "good"
                        }
                      />
                      <p>
                        <b>{event.eventType.replaceAll(".", " ")}</b>
                        <span>
                          {event.actorName} · {event.detail} ·{" "}
                          {new Date(event.createdAt).toLocaleString()}
                        </span>
                      </p>
                    </div>
                  ))}
                  {governanceData && governanceData.events.length === 0 && (
                    <p className="muted-copy">
                      No user events have been recorded yet.
                    </p>
                  )}
                </div>
              </aside>
            </div>
          </div>
        )}
      </section>
    </main>
  );
}
