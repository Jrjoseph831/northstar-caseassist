import { getChatGPTUser } from "../../chatgpt-auth";
import {
  jsonOrError,
  northstarFetch,
  personaId,
  personaName,
} from "../../../lib/azure-bff";

async function authorized(request: Request) {
  return Boolean(await getChatGPTUser()) || new URL(request.url).hostname === "localhost";
}

export async function GET(request: Request) {
  try {
    if (!(await authorized(request)))
      return Response.json({ error: "Authentication required." }, { status: 401 });
    const persona = personaId(new URL(request.url).searchParams.get("persona"));
    const rows = await jsonOrError<
      Array<{
        id: string;
        aiRequestId: string;
        caseId: string;
        submittedByUserId: string;
        assignedReviewerId: string;
        status: string;
        assistantOutput: string;
        reviewerNote: string | null;
        decidedAt: string | null;
      }>
    >(await northstarFetch("/api/v1/reviews", persona));
    return Response.json({
      reviews: rows
        .map((row) => ({
          id: row.id,
          requestId: row.aiRequestId,
          displayId: `REVIEW-${row.id.slice(0, 8).toUpperCase()}`,
          caseId:
            row.caseId === "c5e6ef52-1e03-47de-b58e-060e57f8ea31"
              ? "NS-1048"
              : row.caseId,
          answer: row.assistantOutput,
          status: row.status,
          submitterId: row.submittedByUserId,
          submitterName: personaName(row.submittedByUserId),
          reviewerName: row.decidedAt
            ? personaName(row.assignedReviewerId)
            : null,
          returnNote: row.reviewerNote,
          updatedAt: row.decidedAt ?? new Date(0).toISOString(),
        }))
        .sort((left, right) =>
          left.status === right.status ? 0 : left.status === "Pending" ? -1 : 1,
        ),
    });
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Could not load reviews." },
      { status: 400 },
    );
  }
}

export async function POST(request: Request) {
  if (!(await authorized(request)))
    return Response.json({ error: "Authentication required." }, { status: 401 });
  return Response.json(
    { error: "Sensitive requests are routed automatically by the Azure risk classifier." },
    { status: 409 },
  );
}
