import { getChatGPTUser } from "../../chatgpt-auth";
import { jsonOrError, northstarFetch, personaId } from "../../../lib/azure-bff";

type Body = {
  azureCaseId?: string;
  status?: string;
  version?: number;
  personaId?: string;
};

export async function POST(request: Request) {
  try {
    const signedIn = await getChatGPTUser();
    if (!signedIn && new URL(request.url).hostname !== "localhost")
      return Response.json({ error: "Authentication required." }, { status: 401 });
    const body = (await request.json()) as Body;
    if (!body.azureCaseId || !body.status)
      return Response.json({ error: "A case and status are required." }, { status: 400 });
    const summary = await jsonOrError<unknown>(
      await northstarFetch(
        `/api/v1/cases/${encodeURIComponent(body.azureCaseId)}/status`,
        personaId(body.personaId),
        {
          method: "PATCH",
          headers: { "If-Match": `"${body.version ?? 1}"` },
          body: JSON.stringify({ status: body.status }),
        },
      ),
    );
    return Response.json({ case: summary });
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Status update failed." },
      { status: 400 },
    );
  }
}
