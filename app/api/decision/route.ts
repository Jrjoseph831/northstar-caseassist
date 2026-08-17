import { getChatGPTUser } from "../../chatgpt-auth";
import { jsonOrError, northstarFetch, personaId } from "../../../lib/azure-bff";

type Body = {
  azureCaseId?: string;
  decision?: "Approve" | "Deny";
  note?: string;
  personaId?: string;
};

export async function POST(request: Request) {
  try {
    const signedIn = await getChatGPTUser();
    if (!signedIn && new URL(request.url).hostname !== "localhost")
      return Response.json({ error: "Authentication required." }, { status: 401 });
    const body = (await request.json()) as Body;
    if (!body.azureCaseId || (body.decision !== "Approve" && body.decision !== "Deny"))
      return Response.json({ error: "A case and a valid decision are required." }, { status: 400 });
    const summary = await jsonOrError<unknown>(
      await northstarFetch(
        `/api/v1/cases/${encodeURIComponent(body.azureCaseId)}/decision`,
        personaId(body.personaId),
        {
          method: "POST",
          body: JSON.stringify({ decision: body.decision, note: body.note ?? null }),
        },
      ),
    );
    return Response.json({ case: summary });
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Decision failed." },
      { status: 400 },
    );
  }
}
