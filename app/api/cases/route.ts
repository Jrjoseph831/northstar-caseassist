import { getChatGPTUser } from "../../chatgpt-auth";
import { jsonOrError, northstarFetch, personaId } from "../../../lib/azure-bff";

type CaseSummary = {
  id: string;
  caseNumber: string;
  programCode: string;
  status: string;
  priority: string;
  assignedWorkerId: string;
  submittedAt: string;
  version: number;
};

// Short human narratives per seeded case. Keyed by case number, which the seeder sets
// deterministically. Falls back to a generic line for any generated case.
const caseNarratives: Record<string, string> = {
  "NS-1048": "Hours reduced at work; shutoff scheduled in 8 days.",
  "NS-1061": "Lease renewal assistance with completed income review.",
  "NS-1074": "Training grant application for licensed practical nursing.",
  "NS-1082": "Past-due electric bill with hardship documentation.",
};

const workerNames: Record<string, string> = {
  "maya.chen": "Maya Chen",
  "marcus.reed": "Marcus Reed",
  "priya.shah": "Priya Shah",
};

// A compact first-sentence preview for the case card, so the list stays scannable.
function firstSentence(text: string | null): string | null {
  if (!text) return null;
  const match = text.match(/^.*?[.!?](\s|$)/);
  const sentence = (match ? match[0] : text).trim();
  return sentence.length > 110 ? `${sentence.slice(0, 107)}…` : sentence;
}

export async function GET(request: Request) {
  try {
    const signedIn = await getChatGPTUser();
    if (!signedIn && new URL(request.url).hostname !== "localhost")
      return Response.json({ error: "Authentication required." }, { status: 401 });
    const persona = personaId(new URL(request.url).searchParams.get("persona"));
    const summaries = await jsonOrError<CaseSummary[]>(
      await northstarFetch("/api/v1/cases", persona),
    );
    const details = await Promise.all(
      summaries.map(async (summary) => {
        const detail = await jsonOrError<{
          application: { applicant: { syntheticDisplayName: string; householdSize: number } };
          background: string | null;
        }>(await northstarFetch(`/api/v1/cases/${summary.id}`, persona));
        const program = summary.programCode
          .toLowerCase()
          .replaceAll("_", " ")
          .replace(/\b\w/g, (letter) => letter.toUpperCase());
        const fullBackground = detail.background ?? null;
        // Card list stays a compact index (short line, no PII dump); the full intake
        // narrative is carried separately for the case detail pane.
        const cardSummary =
          caseNarratives[summary.caseNumber] ??
          firstSentence(fullBackground) ??
          `${program} application`;
        return {
          azureId: summary.id,
          id: summary.caseNumber,
          initials: detail.application.applicant.syntheticDisplayName
            .split(/\s+/)
            .map((part) => part[0])
            .join("")
            .slice(0, 2)
            .toUpperCase(),
          name: detail.application.applicant.syntheticDisplayName,
          householdSize: detail.application.applicant.householdSize,
          program,
          status: summary.status,
          priority: summary.priority,
          assignedWorker: workerNames[summary.assignedWorkerId] ?? summary.assignedWorkerId,
          submittedAt: summary.submittedAt,
          version: summary.version,
          summary: cardSummary,
          background: fullBackground ?? cardSummary,
          updated: new Date(summary.submittedAt).toLocaleDateString("en-US"),
        };
      }),
    );
    return Response.json({ cases: details });
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Could not load cases." },
      { status: 400 },
    );
  }
}
