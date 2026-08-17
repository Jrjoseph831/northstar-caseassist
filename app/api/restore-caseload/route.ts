import { getChatGPTUser } from "../../chatgpt-auth";
import { jsonOrError, northstarFetch, personaId } from "../../../lib/azure-bff";

export async function POST(request: Request) {
  try {
    const signedIn = await getChatGPTUser();
    if (!signedIn && new URL(request.url).hostname !== "localhost")
      return Response.json({ error: "Authentication required." }, { status: 401 });
    const body = (await request.json().catch(() => ({}))) as { personaId?: string };
    const result = await jsonOrError<{ restored: number }>(
      await northstarFetch("/api/v1/demo/restore-caseload", personaId(body.personaId), {
        method: "POST",
        body: JSON.stringify({}),
      }),
    );
    return Response.json(result);
  } catch (error) {
    return Response.json(
      { error: error instanceof Error ? error.message : "Restore failed." },
      { status: 400 },
    );
  }
}
