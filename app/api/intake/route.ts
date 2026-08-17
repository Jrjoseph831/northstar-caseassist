import { getChatGPTUser } from "../../chatgpt-auth";
import { jsonOrError, northstarFetch, personaId } from "../../../lib/azure-bff";

type IntakeBody = {
  personaId?: string;
  channel?: "citizen" | "employee";
  programCode?: string;
  syntheticDisplayName?: string;
  email?: string;
  phone?: string;
  address?: string;
  householdSize?: number;
  convertToCase?: boolean;
  documents?: string[];
  situation?: string;
};

const programNames: Record<string, string> = {
  UTILITY_RELIEF: "Utility Relief",
  HOUSING_STABILITY: "Housing Stability",
  WORKFORCE_TRAINING: "Workforce Training",
};

// Builds a short, natural case-background narrative from the intake selections. Used both as
// the assistant's case context and as the workspace "Case background" blurb.
function buildBackground(
  programCode: string | undefined,
  channel: "citizen" | "employee" | undefined,
  householdSize: number | undefined,
  situation: string | undefined,
  attachedDocuments: string[],
): string {
  const program = programNames[programCode ?? ""] ?? "assistance";
  const household = householdSize && householdSize > 0 ? householdSize : 1;
  const channelLabel =
    channel === "employee" ? "employee-assisted intake" : "self-service intake";
  const problem = situation && situation.trim()
    ? `In the applicant's words: "${situation.trim()}"`
    : "No situation description was provided at intake.";
  const docLine = attachedDocuments.length
    ? `Documents provided: ${attachedDocuments.join(", ")}.`
    : "No supporting documents were attached.";
  return `${program} application submitted through ${channelLabel} for a household of ${household}. ${problem} ${docLine}`;
}

export async function POST(request: Request) {
  try {
    const signedIn = await getChatGPTUser();
    if (!signedIn && new URL(request.url).hostname !== "localhost")
      return Response.json({ error: "Authentication required." }, { status: 401 });
    const body = (await request.json()) as IntakeBody;
    const persona = personaId(body.personaId);
    if (body.channel === "employee" && persona !== "maya-chen" && persona !== "priya-shah")
      return Response.json({ error: "Employee-assisted intake requires an authorized employee role." }, { status: 403 });
    const application = await jsonOrError<{
      id: string;
      applicationNumber: string;
      status: string;
    }>(
      await northstarFetch("/api/v1/applications", persona, {
        method: "POST",
        headers: { "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify({
          programCode: body.programCode,
          submissionChannel: body.channel === "employee" ? "EmployeeAssisted" : "CitizenDemo",
          scenarioCode: `${body.channel ?? "citizen"}-portal-intake`,
          applicant: {
            syntheticDisplayName: body.syntheticDisplayName,
            email: body.email,
            phone: body.phone,
            address: body.address,
            householdSize: body.householdSize,
          },
        }),
      }),
    );
    if (!body.convertToCase) return Response.json({ application }, { status: 201 });
    const caseRecord = await jsonOrError<{
      id: string;
      caseNumber: string;
      status: string;
    }>(
      await northstarFetch(
        `/api/v1/applications/${application.id}/convert-to-case`,
        persona,
        {
          method: "POST",
          body: JSON.stringify({ assignedWorkerId: "maya.chen", priority: "Standard" }),
        },
      ),
    );
    // Attach the chosen synthetic documents. The document *type* is carried in the file
    // name so the assistant can later compare what is on file against policy requirements.
    // An intentionally partial set lets the reviewer test the AI's gap analysis.
    const requestedDocuments = Array.isArray(body.documents)
      ? body.documents.filter((label) => typeof label === "string" && label.trim().length > 0)
      : [];
    const attachedLabels: string[] = [];
    for (const label of requestedDocuments) {
      const safeLabel = label.replace(/[\\/:*?"<>|]/g, "").trim().slice(0, 80);
      if (!safeLabel) continue;
      const content = `Synthetic ${safeLabel} for case ${caseRecord.caseNumber}. Fictional demo document containing no real personal information.`;
      const form = new FormData();
      form.set("file", new File([content], `${safeLabel}.txt`, { type: "text/plain" }));
      const uploaded = await northstarFetch(
        `/api/v1/cases/${caseRecord.id}/documents`,
        persona,
        { method: "POST", body: form },
      );
      if (uploaded.ok) attachedLabels.push(safeLabel);
    }

    // Write a real, program-specific background note. The assistant reads this as case
    // context and the workspace surfaces it, so intake cases read as naturally as the
    // seeded ones instead of a generic placeholder line.
    const background = buildBackground(
      body.programCode,
      body.channel,
      body.householdSize,
      body.situation,
      attachedLabels,
    );
    await northstarFetch(`/api/v1/cases/${caseRecord.id}/notes`, persona, {
      method: "POST",
      body: JSON.stringify({
        noteType: "CaseBackground",
        content: background,
        sourceRequestId: null,
      }),
    });

    return Response.json(
      { application, case: caseRecord, attachedDocuments: attachedLabels.length },
      { status: 201 },
    );
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Intake failed." },
      { status: 400 },
    );
  }
}
