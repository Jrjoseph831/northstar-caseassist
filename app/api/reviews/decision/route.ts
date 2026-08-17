import { getChatGPTUser } from "../../../chatgpt-auth";
import { jsonOrError, northstarFetch, personaId } from "../../../../lib/azure-bff";

export async function POST(request: Request) {
  try {
    const signedIn = await getChatGPTUser();
    if (!signedIn && new URL(request.url).hostname !== "localhost")
      return Response.json({ error: "Authentication required." }, { status: 401 });
    const body = (await request.json()) as {
      reviewId?: string;
      action?: "approve" | "return";
      note?: string;
      personaId?: string;
    };
    if (!body.reviewId || !body.action)
      return Response.json({ error: "Review and decision are required." }, { status: 400 });
    const result = await jsonOrError<{ status: string; decidedAt: string }>(
      await northstarFetch(
        `/api/v1/reviews/${encodeURIComponent(body.reviewId)}/decision`,
        personaId(body.personaId),
        {
          method: "POST",
          body: JSON.stringify({
            decision: body.action === "approve" ? "Approve" : "Return",
            note: body.note?.trim() || null,
          }),
        },
      ),
    );
    return Response.json(result);
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Decision failed." },
      { status: 400 },
    );
  }
}
